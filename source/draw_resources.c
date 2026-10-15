#include "dnd_draw.h"
#include "dnd_performance.h"
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <limits.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#endif
#define SLOTS 512
#define BUDGET (16*1024*1024)
enum { BUFFER=1,TEXTURE,MESH,CANVAS };
typedef struct {
    void *allocation;
    unsigned char *data;
    int kind,bytes,generation;
    bool pending;
    int width,height,format,linear,vertices,indices,dynamic,capacity,count,texture,blend,draws,flushes;
} Resource;
static Resource slots[SLOTS];
static int total_bytes,active_canvas;
static float identity[16]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
static DndDrawVertex staging[768];
static Resource *find(int token,int kind) {
    int index=(token&1023)-1;
    if(token<=0 || index<0 || index>=SLOTS) return NULL;
    Resource *r=&slots[index];
    return r->kind && !r->pending && r->generation==(token>>10) && (!kind || r->kind==kind) ? r : NULL;
}
static int create(int kind,int bytes,int alignment) {
    if(dnd_raw_depth() || bytes<=0 || bytes>BUDGET-total_bytes || alignment<32 || alignment>4096 || (alignment&(alignment-1))) return 0;
    for(int i=0;i<SLOTS;i++) if(!slots[i].kind && slots[i].generation<0x1fffff) {
        Resource *r=&slots[i];
        size_t padded=((size_t)bytes+31)&~(size_t)31;
        void *base=malloc(padded+(unsigned)alignment-1);
        if(!base) return 0;
        int generation=r->generation+1;
        memset(r,0,sizeof(*r)); r->generation=generation; r->kind=kind; r->bytes=bytes; r->allocation=base;
        r->data=(unsigned char*)(((uintptr_t)base+(unsigned)alignment-1)&~((uintptr_t)alignment-1));
        memset(r->data,0,padded); total_bytes+=bytes;
        return (generation<<10)|(i+1);
    }
    return 0;
}
static void release(Resource *r) {
    total_bytes-=r->bytes; free(r->allocation);
    int generation=r->generation; memset(r,0,sizeof(*r)); r->generation=generation;
}
int dnd_draw_release(int token) {
    if(dnd_raw_depth() || token==active_canvas) return -1;
    Resource *r=find(token,0); if(!r) return -1;
    if(dnd_device_active()) r->pending=true;
    else { dnd_draw_backend_wait(); release(r); }
    return 0;
}
void dnd_draw_collect(void) { for(int i=0;i<SLOTS;i++) if(slots[i].pending) release(&slots[i]); }
void dnd_draw_shutdown(void) { active_canvas=0;for(int i=0;i<SLOTS;i++) if(slots[i].kind) release(&slots[i]); }
int dnd_draw_live(void) { int n=0;for(int i=0;i<SLOTS;i++) if(slots[i].kind)n++;return n; }
int dnd_draw_total_bytes(void) { return total_bytes; }
int dnd_draw_bytes(int token) { Resource *r=find(token,0);return r?r->bytes:-1; }
int dnd_buffer_create(int bytes,int alignment) { return create(BUFFER,bytes,alignment); }
uint64_t dnd_buffer_address(int token,int offset) {
    Resource *r=find(token,BUFFER);
    if(!r || offset<0 || offset>r->bytes) { dnd_exception_throw(DND_EXCEPTION_OBJECT_DISPOSED,"Invalid native buffer address.");return 0; }
    return (uint64_t)(uintptr_t)(r->data+offset);
}
static bool array_range(DndArray *a,int index,int count) {
    return a && !a->elements_are_references && index>=0 && count>=0 && (uint32_t)index<=a->length && (uint32_t)count<=a->length-(uint32_t)index && a->element_size<=36;
}
int dnd_buffer_copy(int token,int offset,DndArray *array,int index,int count,int write) {
    Resource *r=find(token,BUFFER);
    if(dnd_raw_depth() || dnd_device_active() || !r || !array_range(array,index,count) || offset<0 || offset>r->bytes || (size_t)count*array->element_size>(size_t)(r->bytes-offset)) return -1;
    void *p=(unsigned char*)array->data+(size_t)index*array->element_size;
    if(write) memcpy(r->data+offset,p,(size_t)count*array->element_size);
    else memcpy(p,r->data+offset,(size_t)count*array->element_size);
    return 0;
}
int dnd_buffer_flush(int token,int offset,int count,int invalidate) {
    Resource *r=find(token,BUFFER);
    if(dnd_raw_depth() || !r || offset<0 || count<0 || offset>r->bytes || count>r->bytes-offset) return -1;
    dnd_draw_backend_flush(r->data+offset,count,invalidate);return 0;
}
static int texture_size(int w,int h,int format) {
    int bw=4,bh=4,bytes=32;
    if(w<1 || h<1 || w>1024 || h>1024) return -1;
    switch(format) {
        case 0: case 14: bw=8;bh=8;break;
        case 1: case 2: bw=8;bh=4;break;
        case 3: case 4: case 5: break;
        case 6: bytes=64;break;
        default:return -1;
    }
    return ((w+bw-1)/bw)*((h+bh-1)/bh)*bytes;
}
static uint32_t le32(const unsigned char *p) { return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24; }
int dnd_texture_package(DndArray *a,int linear) {
    if(dnd_device_active() || !a || a->element_size!=1 || a->elements_are_references || a->length<32) return 0;
    const unsigned char *p=a->data;
    if(memcmp(p,"DNTX",4) || le32(p+4)!=1 || le32(p+24)!=0 || le32(p+28)!=0) return 0;
    int format=(int)le32(p+8),w=(int)le32(p+12),h=(int)le32(p+16);
    int bytes=texture_size(w,h,format);
    if(bytes<0 || le32(p+20)!=(uint32_t)bytes || a->length-32!=(unsigned)bytes) return 0;
    int token=create(TEXTURE,bytes,32);Resource *r=find(token,TEXTURE);if(!r)return 0;
    r->width=w;r->height=h;r->format=format;r->linear=linear!=0;memcpy(r->data,p+32,(size_t)bytes);
    dnd_draw_backend_flush(r->data,bytes,2);return token;
}
int dnd_texture_create(int w,int h,DndArray *rgba,int linear) {
    int bytes=texture_size(w,h,6);
    if(dnd_device_active() || bytes<0 || !rgba || rgba->element_size!=1 || rgba->elements_are_references || rgba->length!=(uint32_t)(w*h*4))return 0;
    int token=create(TEXTURE,bytes,32);Resource *r=find(token,TEXTURE);if(!r)return 0;
    r->width=w;r->height=h;r->format=6;r->linear=linear!=0;
    const unsigned char *src=rgba->data;int blocks=(w+3)/4;
    for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
        int block=((y/4)*blocks+x/4)*64,at=((y&3)*4+(x&3))*2,source=(y*w+x)*4;
        r->data[block+at]=src[source+3];r->data[block+at+1]=src[source];
        r->data[block+32+at]=src[source+1];r->data[block+33+at]=src[source+2];
    }
    dnd_draw_backend_flush(r->data,bytes,2);return token;
}
int dnd_texture_dimension(int token,int axis) { Resource *r=find(token,TEXTURE);return !r?-1:axis==0?r->width:axis==1?r->height:r->format; }
static bool matrix(DndArray *a,float *out) {
    if(!a || a->element_size!=4 || a->elements_are_references || a->length!=16) return false;
    memcpy(out,a->data,64);for(int i=0;i<16;i++)if(!isfinite(out[i]))return false;return true;
}
static int vertex_size(int format) { return format==0?16:format==1?24:format==2?36:0; }
static bool vertices(DndArray *a,int first,int count,int format,DndDrawVertex *out) {
    int size=vertex_size(format);
    if(!size || !array_range(a,first,count) || a->element_size!=(uint32_t)size)return false;
    for(int i=0;i<count;i++) {
        const unsigned char *p=(unsigned char*)a->data+(size_t)(first+i)*size;
        DndDrawVertex *v=&out[i];memset(v,0,sizeof(*v));v->nz=1;memcpy(&v->x,p,12);
        int color=12;
        if(format==2){memcpy(&v->nx,p+12,12);color=24;}
        v->color=(uint32_t)p[color]<<24|(uint32_t)p[color+1]<<16|(uint32_t)p[color+2]<<8|p[color+3];
        if(format)memcpy(&v->u,p+color+4,8);
        float values[8]={v->x,v->y,v->z,v->nx,v->ny,v->nz,v->u,v->v};
        for(int j=0;j<8;j++)if(!isfinite(values[j]))return false;
    }
    return true;
}
int dnd_mesh_create(DndArray *a,DndArray *indices,int format,int dynamic) {
    if(dnd_device_active() || !a || a->length<1 || a->length>65535 || !vertex_size(format)) return 0;
    int n=(int)a->length,m=indices?(int)indices->length:0;
    if((m?m:n)%3 || m>3*65535 || (indices && (!array_range(indices,0,m)||indices->element_size!=2))) return 0;
    int token=create(MESH,n*(int)sizeof(DndDrawVertex)+m*2,32);Resource *r=find(token,MESH);if(!r)return 0;
    r->vertices=n;r->indices=m;r->dynamic=dynamic!=0;r->format=format;
    if(!vertices(a,0,n,format,(DndDrawVertex*)r->data)){release(r);return 0;}
    uint16_t *index=(uint16_t*)(r->data+n*sizeof(DndDrawVertex));
    if(m){memcpy(index,indices->data,(size_t)m*2);for(int i=0;i<m;i++)if(index[i]>=n){release(r);return 0;}}
    dnd_draw_backend_flush(r->data,r->bytes,0);return token;
}
int dnd_mesh_update(int token,DndArray *a,int offset,int count,int format) {
    Resource *r=find(token,MESH);
    if(dnd_device_active()||!r||!r->dynamic||r->format!=format||offset<0||count<0||offset>r->vertices||count>r->vertices-offset||!array_range(a,0,count))return -1;
    // Validate the entire update before committing any part of it.
    for(int start=0;start<count;start+=768) { int n=count-start;if(n>768)n=768;if(!vertices(a,start,n,format,staging))return -1; }
    for(int start=0;start<count;start+=768) { int n=count-start;if(n>768)n=768;(void)vertices(a,start,n,format,staging);memcpy(r->data+(size_t)(offset+start)*sizeof(DndDrawVertex),staging,(size_t)n*sizeof(DndDrawVertex)); }
    dnd_draw_backend_flush(r->data,r->bytes,0);return 0;
}
static bool texture(int token,DndDrawTexture *out) {
    memset(out,0,sizeof(*out));if(!token)return true;
    Resource *r=find(token,TEXTURE);if(!r)return false;
    *out=(DndDrawTexture){r->data,(uint32_t)r->bytes,r->width,r->height,r->format,r->linear!=0};return true;
}
int dnd_mesh_draw(int token,int tex,DndArray *world,uint32_t color,int flags,float cutoff) {
    Resource *r=find(token,MESH);DndDrawTexture tx;float transform[16];
    if(dnd_device_pass()!=1||!r||!texture(tex,&tx)||!matrix(world,transform)||flags<0||flags>63||!isfinite(cutoff)||cutoff<0||cutoff>1)return -1;
    DndDrawMaterial material={color,flags,cutoff};
    DndDrawVertex *v=(DndDrawVertex*)r->data;uint16_t *idx=(uint16_t*)(r->data+r->vertices*sizeof(DndDrawVertex));
    int count=r->indices?r->indices:r->vertices;
    for(int start=0;start<count;) {
        int n=count-start;if(n>768)n=768;
        for(int j=0;j<n;j++)staging[j]=v[r->indices?idx[start+j]:start+j];
        dnd_draw_backend_vertices(staging,n,0x90,tex?&tx:NULL,&material,transform);start+=n;
    }
    dnd_performance_add_triangles((uint32_t)count/3);return 0;
}
int dnd_mesh_primitives(DndArray *a,int offset,int count,int format,int primitive,DndArray *world,uint32_t color,int flags) {
    DndDrawMaterial material={color,flags,0};float transform[16];
    if(dnd_device_pass()!=1||count<0||count>768||!matrix(world,transform)||!vertices(a,offset,count,format,staging))return -1;
    if((primitive==0x90&&count%3)||(primitive==0xa8&&count%2)||(primitive!=0x90&&primitive!=0xa8&&primitive!=0xb8))return -1;
    if(count)dnd_draw_backend_vertices(staging,count,primitive,NULL,&material,transform);
    if(primitive==0x90)dnd_performance_add_triangles((uint32_t)count/3);
    return 0;
}
int dnd_canvas_create(int capacity) {
    if(capacity<6||capacity>65532)return 0;
    int token=create(CANVAS,capacity*(int)sizeof(DndDrawVertex),32);Resource *r=find(token,CANVAS);if(r)r->capacity=capacity;return token;
}
int dnd_canvas_begin(int token,int w,int h) {
    Resource *r=find(token,CANVAS);if(!r||w<1||h<1||w>16384||h>16384||dnd_device_pass_begin(2)<0)return -1;
    active_canvas=token;r->count=0;r->texture=0;r->blend=1;r->draws=0;r->flushes=0;r->width=w;r->height=h;
    dnd_draw_backend_2d(w,h);return 0;
}
int dnd_canvas_flush(int token) {
    Resource *r=find(token,CANVAS);if(!r||token!=active_canvas||dnd_device_pass()!=2)return -1;
    if(r->count) {
        DndDrawTexture tx;
        Resource *retired=NULL;
        int ti=(r->texture&1023)-1;
        if(r->texture>0&&ti>=0&&ti<SLOTS&&slots[ti].generation==(r->texture>>10)&&slots[ti].kind==TEXTURE)retired=&slots[ti];
        if(retired)tx=(DndDrawTexture){retired->data,(uint32_t)retired->bytes,retired->width,retired->height,retired->format,retired->linear!=0};
        else if(!texture(r->texture,&tx))return -1;
        DndDrawMaterial mat={0xffffffffu,r->blend|8|16,0};
        dnd_draw_backend_vertices((DndDrawVertex*)r->data,r->count,0x90,r->texture?&tx:NULL,&mat,identity);
        dnd_performance_add_triangles((uint32_t)r->count/3);r->count=0;r->draws++;r->flushes++;
    }
    return 0;
}
int dnd_canvas_vertices(int token,int tex,DndArray *a,int count,uint32_t color) {
    Resource *r=find(token,CANVAS);DndDrawTexture tx;
    if(!r||token!=active_canvas||dnd_device_pass()!=2||!a||a->element_size!=4||a->elements_are_references||count<0||count>r->capacity||count%3||a->length<(uint32_t)count*4||!texture(tex,&tx))return -1;
    const float *p=(const float*)a->data;for(int i=0;i<count*4;i++)if(!isfinite(p[i]))return -1;
    if(r->count && (r->texture!=tex || r->count+count>r->capacity))if(dnd_canvas_flush(token)<0)return -1;
    r->texture=tex;DndDrawVertex *v=(DndDrawVertex*)r->data+r->count;
    for(int i=0;i<count;i++)v[i]=(DndDrawVertex){p[i*4],p[i*4+1],0,0,0,1,p[i*4+2],p[i*4+3],color};
    r->count+=count;return 0;
}
int dnd_canvas_end(int token) { if(dnd_canvas_flush(token)<0)return -1;int result=dnd_device_pass_end(2);if(result==0)active_canvas=0;return result; }
int dnd_canvas_clip(int token,int x,int y,int w,int h) {
    Resource *r=find(token,CANVAS);
    if(!r||x<0||y<0||w<0||h<0||x>r->width-w||y>r->height-h||dnd_canvas_flush(token)<0)return -1;
    int screen_w=dnd_device_width(),screen_h=dnd_device_height();
    int left=x*screen_w/r->width,top=y*screen_h/r->height;
    int right=(x+w)*screen_w/r->width,bottom=(y+h)*screen_h/r->height;
    dnd_draw_backend_scissor(left,top,right-left,bottom-top);return 0;
}
int dnd_canvas_blend(int token,int blend) {
    Resource *r=find(token,CANVAS);if(!r||blend<0||blend>3||dnd_canvas_flush(token)<0)return -1;r->blend=blend;return 0;
}
int dnd_canvas_stats(int token,int kind) { Resource *r=find(token,CANVAS);return !r?-1:kind==0?r->draws:kind==1?r->flushes:r->count; }
int dnd_draw_copy_texture(int token,int x,int y,int w,int h) {
    Resource *r=find(token,TEXTURE);DndDrawTexture tx;
    if(!r||!dnd_device_active()||dnd_device_pass()||!texture(token,&tx)||r->format!=6||w!=r->width||h!=r->height||x<0||y<0||x>dnd_device_width()-w||y>dnd_device_height()-h)return -1;
    dnd_draw_backend_copy(&tx,x,y,w,h);return 0;
}

uint64_t dnd_buffer_uncached(int token) {
    uint64_t address=dnd_buffer_address(token,0);
#ifdef DND_GAMECUBE_STORAGE
    return (uint64_t)(uintptr_t)MEM_K0_TO_K1((void*)(uintptr_t)address);
#else
    return address;
#endif
}
int dnd_native_object_size(int kind) {
#ifdef DND_GAMECUBE_STORAGE
    switch(kind) {
        case 0:return sizeof(GXFifoObj);case 1:return sizeof(GXTexObj);case 2:return sizeof(GXTlutObj);
        case 3:return sizeof(GXLightObj);case 4:return sizeof(GXTexRegion);case 5:return sizeof(GXTlutRegion);
        case 6:return sizeof(GXFogAdjTbl);case 7:return sizeof(GXRModeObj);case 8:return sizeof(GXVtxDesc);
        case 9:return sizeof(GXVtxAttrFmt);case 10:return sizeof(Mtx);case 11:return sizeof(Mtx44);
        case 12:return sizeof(Mtx33);case 13:return sizeof(guVector);case 14:return sizeof(guQuaternion);
        default:return -1;
    }
#else
    static const int sizes[]={128,32,12,64,16,16,20,64,8,20,48,64,36,12,16};
    return kind>=0&&kind<15?sizes[kind]:-1;
#endif
}

int dnd_buffer_write(int token,int offset,DndArray *array,int index,int count){return dnd_buffer_copy(token,offset,array,index,count,1);}
int dnd_buffer_read(int token,int offset,DndArray *array,int index,int count){return dnd_buffer_copy(token,offset,array,index,count,0);}
