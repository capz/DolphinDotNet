#include "dnd_draw.h"
#include "dnd_async.h"
#include "dnd_performance.h"
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <limits.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#endif
#include "dnd_texture_png.h"
#define SLOTS 512
#define BUDGET (16*1024*1024)
enum { BUFFER=1,TEXTURE,MESH,CANVAS,EFFECT };
typedef struct {
    void *allocation;
    unsigned char *data;
    int kind,bytes,generation;
    bool pending;
    int effect;
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
int dnd_effect_resolve(int token,const DndDrawEffect **result) {
    if(!result)return -2;
    *result=NULL;
    if(!token)return 0;
    Resource *resource=find(token,EFFECT);
    if(!resource)return -2;
    *result=(const DndDrawEffect*)resource->data;
    return 0;
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
static int texture_rgba(int w,int h,const unsigned char *src,int linear) {
    int bytes=texture_size(w,h,6);
    if(dnd_device_active() || bytes<0 || !src)return 0;
    int token=create(TEXTURE,bytes,32);Resource *r=find(token,TEXTURE);if(!r)return 0;
    r->width=w;r->height=h;r->format=6;r->linear=linear!=0;
    int blocks=(w+3)/4;
    for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
        int block=((y/4)*blocks+x/4)*64,at=((y&3)*4+(x&3))*2,source=(y*w+x)*4;
        r->data[block+at]=src[source+3];r->data[block+at+1]=src[source];
        r->data[block+32+at]=src[source+1];r->data[block+33+at]=src[source+2];
    }
    dnd_draw_backend_flush(r->data,bytes,2);return token;
}
int dnd_texture_create(int w,int h,DndArray *rgba,int linear) {
    if(texture_size(w,h,6)<0 || !rgba || rgba->element_size!=1 || rgba->elements_are_references || rgba->length!=(uint32_t)(w*h*4))return 0;
    return texture_rgba(w,h,rgba->data,linear);
}
int dnd_texture_png(DndArray *png,int linear) {
    if(dnd_device_active() || dnd_raw_depth() || !png || png->element_size!=1 || png->elements_are_references || png->length>1024*1024)return 0;
    const unsigned char *data=png->data;
    if(!png_envelope(data,png->length))return 0;
    int w=0,h=0,channels=0;
    if(!stbi_info_from_memory(data,(int)png->length,&w,&h,&channels) || texture_size(w,h,6)<0)return 0;
    unsigned char *rgba=stbi_load_from_memory(data,(int)png->length,&w,&h,&channels,4);
    if(!rgba)return 0;
    int token=texture_rgba(w,h,rgba,linear);
    stbi_image_free(rgba);
    return token;
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
int dnd_mesh_draw_effect(int token,int tex,DndArray *world,uint32_t color,int flags,float cutoff,int effect) {
    Resource *r=find(token,MESH);DndDrawTexture tx;float transform[16];
    if(dnd_device_pass()!=1||!r||!texture(tex,&tx)||!matrix(world,transform)||flags<0||flags>127||!isfinite(cutoff)||cutoff<0||cutoff>1)return -1;
    /* Reflection coordinates require authored normals, not the fallback +Z used
     * by color-only meshes. Reject before any submission or triangle counters. */
    if((flags&64)&&(!tex||r->format!=2))return -1;
    Resource *fx=effect?find(effect,EFFECT):NULL;
    if(effect&&(!fx||(((DndDrawEffect*)fx->data)->texture&&!tex)))return -1;
    DndDrawMaterial material={color,flags,cutoff,fx?(DndDrawEffect*)fx->data:NULL};
    DndDrawVertex *v=(DndDrawVertex*)r->data;uint16_t *idx=(uint16_t*)(r->data+r->vertices*sizeof(DndDrawVertex));
    int count=r->indices?r->indices:r->vertices;
    for(int start=0;start<count;) {
        int n=count-start;if(n>768)n=768;
        for(int j=0;j<n;j++)staging[j]=v[r->indices?idx[start+j]:start+j];
        dnd_draw_backend_vertices(staging,n,0x90,tex?&tx:NULL,&material,transform);start+=n;
    }
    dnd_performance_add_triangles((uint32_t)count/3);return 0;
}
int dnd_mesh_draw(int token,int tex,DndArray *world,uint32_t color,int flags,float cutoff) {
    return dnd_mesh_draw_effect(token,tex,world,color,flags,cutoff,0);
}
int dnd_mesh_primitives(DndArray *a,int offset,int count,int format,int primitive,DndArray *world,uint32_t color,int flags) {
    DndDrawMaterial material={color,flags,0,NULL};float transform[16];
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
    active_canvas=token;r->count=0;r->texture=0;r->effect=0;r->blend=1;r->draws=0;r->flushes=0;r->width=w;r->height=h;
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
        Resource *fx=NULL;
        if(r->effect){int index=(r->effect&1023)-1;
            if(index<0||index>=SLOTS||slots[index].kind!=EFFECT||slots[index].generation!=(r->effect>>10))return -1;
            fx=&slots[index];
        }
        DndDrawMaterial mat={0xffffffffu,r->blend|8|16,0,fx?(DndDrawEffect*)fx->data:NULL};
        dnd_draw_backend_vertices((DndDrawVertex*)r->data,r->count,0x90,r->texture?&tx:NULL,&mat,identity);
        dnd_performance_add_triangles((uint32_t)r->count/3);r->count=0;r->draws++;r->flushes++;
    }
    return 0;
}
int dnd_canvas_vertices(int token,int tex,DndArray *a,int count,uint32_t color) {
    Resource *r=find(token,CANVAS);DndDrawTexture tx;
    if(!r||token!=active_canvas||dnd_device_pass()!=2||!a||a->element_size!=4||a->elements_are_references||count<0||count>r->capacity||count%3||a->length<(uint32_t)count*4||!texture(tex,&tx))return -1;
    Resource *fx=r->effect?find(r->effect,EFFECT):NULL;
    if(r->effect&&(!fx||(((DndDrawEffect*)fx->data)->texture&&!tex)))return -1;
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

/* Programs are immutable. Uniform writes are forbidden while a frame is active. */
int dnd_effect_create(DndArray *a) {
    dnd_async_require_application_thread();if(dnd_exception_pending())return 0;
    if(dnd_device_active()||dnd_raw_depth()||!a||a->element_size!=1||a->elements_are_references||a->length<184||a->length>304)return 0;
    const unsigned char *p=a->data;
    unsigned count=le32(p+8),texture_flag=le32(p+12),uniforms=le32(p+16);
    if(memcmp(p,"DNEF",4)||le32(p+4)!=1||count<1||count>16||texture_flag>1||uniforms>4||a->length!=176+count*8||le32(p+20)||le32(p+24)||le32(p+28))return 0;
    for(unsigned i=0;i<4;i++) {
        const unsigned char *name=p+32+i*36;int end=0;
        for(int j=0;j<32;j++){if(!name[j])end=1;else if(end||name[j]<33||name[j]>126)return 0;}
        if(!end||(i<uniforms?!name[0]:name[0]!=0))return 0;
        for(unsigned j=0;j<i&&i<uniforms;j++)if(!strcmp((const char*)name,(const char*)p+32+j*36))return 0;
    }
    unsigned initialized=0;
    for(unsigned i=0;i<count;i++) {
        const DndTevStage *s=(const DndTevStage*)(p+176+i*8);
        if((s->op!=0&&s->op!=8)||s->dest>3||s->reserved||(s->konst!=0&&(s->konst<12||s->konst>31)))return 0;
        const unsigned char inputs[]={s->a,s->b,s->c,s->d};
        for(int j=0;j<4;j++) {
            unsigned v=inputs[j];
            if(v==0||v==2||v==4||v==6){if(!(initialized&(1u<<(v/2))))return 0;}
            else if(v==14){unsigned k=s->konst;if(k<12||k>31||((k-12)%4)>=uniforms)return 0;}
            else if(v==8){if(!texture_flag)return 0;}
            else if(v!=10&&v!=12&&v!=15)return 0;
        }
        initialized|=1u<<s->dest;
    }
    if(p[176+(count-1)*8+5]!=0)return 0;
    int token=create(EFFECT,sizeof(DndDrawEffect),32);Resource *r=find(token,EFFECT);if(!r)return 0;
    DndDrawEffect *effect=(DndDrawEffect*)r->data;
    effect->count=(int)count;effect->texture=(int)texture_flag;effect->uniforms=(int)uniforms;
    for(unsigned i=0;i<uniforms;i++){const unsigned char *c=p+64+i*36;effect->colors[i]=(unsigned)c[0]<<24|(unsigned)c[1]<<16|(unsigned)c[2]<<8|c[3];}
    memcpy(effect->stages,p+176,count*8);return token;
}
int dnd_effect_color(int token,int slot,uint32_t color) {
    dnd_async_require_application_thread();if(dnd_exception_pending())return -1;
    Resource *r=find(token,EFFECT);
    if(!r||dnd_device_active()||dnd_raw_depth())return -1;
    DndDrawEffect *effect=(DndDrawEffect*)r->data;
    if(slot<0||slot>=effect->uniforms)return -1;
    effect->colors[slot]=color;return 0;
}
int dnd_canvas_effect(int token,int effect) {
    Resource *r=find(token,CANVAS),*fx=effect?find(effect,EFFECT):NULL;
    if(!r||token!=active_canvas||(effect&&!fx)||dnd_device_pass()!=2)return -1;
    if(r->effect==effect)return 0;
    if(dnd_canvas_flush(token)<0)return -1;
    r->effect=effect;return 0;
}
