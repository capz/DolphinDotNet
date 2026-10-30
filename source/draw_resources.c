#include "dnd_draw.h"
#include "dnd_async.h"
#include "dnd_performance.h"
#include "dnd_crc32.h"
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <limits.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#endif
#include "dnd_texture_png.h"
/* Isolated host fixtures can link texture/resource plumbing without GX. */
#if !defined(DND_GAMECUBE_STORAGE)
__attribute__((weak)) void dnd_draw_backend_publish_texture(void){}
__attribute__((weak)) void dnd_draw_backend_mesh_changed(void){}
__attribute__((weak)) void dnd_draw_backend_2d_projection(float w,float h){dnd_draw_backend_2d((int)w,(int)h);}
__attribute__((weak)) bool dnd_draw_backend_mesh(const DndDrawVertex *v,const uint16_t *i,int n,uint32_t c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){(void)v;(void)i;(void)n;(void)c;(void)t;(void)m;(void)w;return false;}
#endif

#define SLOTS 512
#define BUDGET (16*1024*1024)
enum { BUFFER=1,TEXTURE,MESH,CANVAS,EFFECT };
typedef struct {
    void *allocation;
    unsigned char *data;
    int kind,bytes,generation;
    bool pending,io_busy;
    bool uniform_color;
    uint32_t mesh_color;
    int holds;
    float logical_width,logical_height;
    int effect;
    int levels;
    bool sampler_set;
    int wrap_s,wrap_t,min_filter,mag_filter;
    float lod_bias;
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
        memset(r,0,sizeof(*r)); r->generation=generation; r->kind=kind; r->levels=1; r->bytes=bytes; r->allocation=base;
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
    if(dnd_device_active() || r->holds) r->pending=true;
    else { dnd_draw_backend_wait(); release(r); }
    return 0;
}
void dnd_draw_collect(void) { for(int i=0;i<SLOTS;i++) if(slots[i].pending && !slots[i].holds) release(&slots[i]); }
/* DMA pins survive managed Dispose, but do not permit new GPU submissions. */
int dnd_buffer_hold(int token) {
    Resource *r=find(token,BUFFER);
    if(!r || r->io_busy || r->holds==INT_MAX)return -1;
    ++r->holds;return 0;
}
int dnd_buffer_unhold(int token) {
    int index=(token&1023)-1;
    if(index<0 || index>=SLOTS || token<=0)return -1;
    Resource *r=&slots[index];
    if(r->kind!=BUFFER || r->generation!=(token>>10) || r->holds<=0)return -1;
    if(--r->holds==0 && r->pending && !dnd_device_active()){
        dnd_draw_backend_wait();release(r);
    }
    return 0;
}
uint64_t dnd_buffer_held_address(int token,int offset,int count) {
    int index=(token&1023)-1;
    if(index<0 || index>=SLOTS || token<=0 || offset<0 || count<0)return 0;
    Resource *r=&slots[index];
    if(r->kind!=BUFFER || r->generation!=(token>>10) || r->holds<=0 || r->io_busy ||
       offset>r->bytes || count>r->bytes-offset)return 0;
    return (uint64_t)(uintptr_t)(r->data+offset);
}
void dnd_draw_shutdown(void) { active_canvas=0;for(int i=0;i<SLOTS;i++) if(slots[i].kind) release(&slots[i]); }
int dnd_draw_live(void) { int n=0;for(int i=0;i<SLOTS;i++) if(slots[i].kind)n++;return n; }
int dnd_draw_total_bytes(void) { return total_bytes; }
int dnd_draw_bytes(int token) { Resource *r=find(token,0);return r?r->bytes:-1; }
uint64_t dnd_buffer_io_begin(int token,int offset,int count) {
    Resource *r=find(token,BUFFER);
    if(!r || r->holds || r->io_busy || offset<0 || count<0 || offset>r->bytes || count>r->bytes-offset)return 0;
    r->holds++;r->io_busy=true;return (uint64_t)(uintptr_t)(r->data+offset);
}
void dnd_buffer_io_end(int token) {
    int index=(token&1023)-1;
    if(index<0||index>=SLOTS)return;
    Resource *r=&slots[index];
    if(r->kind!=BUFFER||r->generation!=(token>>10)||!r->io_busy)return;
    r->io_busy=false;--r->holds;
    /* This exclusive BUFFER lease never permits GPU/DMA use while owned. */
    if(!r->holds&&r->pending&&!dnd_device_active())release(r);
}
int dnd_buffer_create(int bytes,int alignment) { return create(BUFFER,bytes,alignment); }
uint64_t dnd_buffer_address(int token,int offset) {
    Resource *r=find(token,BUFFER);
    if(!r || r->io_busy || offset<0 || offset>r->bytes) { dnd_exception_throw(DND_EXCEPTION_OBJECT_DISPOSED,"Invalid native buffer address.");return 0; }
    return (uint64_t)(uintptr_t)(r->data+offset);
}
static bool array_range(DndArray *a,int index,int count) {
    return a && !a->elements_are_references && index>=0 && count>=0 && (uint32_t)index<=a->length && (uint32_t)count<=a->length-(uint32_t)index && a->element_size<=36;
}
int dnd_buffer_copy(int token,int offset,DndArray *array,int index,int count,int write) {
    Resource *r=find(token,BUFFER);
    if(dnd_raw_depth() || dnd_device_active() || !r || r->io_busy || !array_range(array,index,count) || offset<0 || offset>r->bytes || (size_t)count*array->element_size>(size_t)(r->bytes-offset)) return -1;
    void *p=(unsigned char*)array->data+(size_t)index*array->element_size;
    if(write) memcpy(r->data+offset,p,(size_t)count*array->element_size);
    else memcpy(p,r->data+offset,(size_t)count*array->element_size);
    return 0;
}
int dnd_buffer_flush(int token,int offset,int count,int invalidate) {
    Resource *r=find(token,BUFFER);
    if(dnd_raw_depth() || !r || r->io_busy || offset<0 || count<0 || offset>r->bytes || count>r->bytes-offset) return -1;
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
    unsigned version=le32(p+4),levels=version==1?1:le32(p+24);
    if(memcmp(p,"DNTX",4) || (version!=1&&version!=2) || le32(p+28)!=0 ||
       (version==1&&le32(p+24)!=0) || levels<1 || levels>11)return 0;
    int format=(int)le32(p+8),w=(int)le32(p+12),h=(int)le32(p+16);
    int bytes=texture_size(w,h,format);
    if(bytes<0)return 0;
    if(version==2){
        if((w&(w-1))||(h&(h-1)))return 0;
        int mw=w,mh=h;
        for(unsigned level=1;level<levels;level++){
            if(mw==1&&mh==1)return 0;
            mw=mw>1?mw/2:1;mh=mh>1?mh/2:1;
            bytes+=texture_size(mw,mh,format);
        }
    }
    if(le32(p+20)!=(uint32_t)bytes || a->length-32!=(unsigned)bytes)return 0;
    int token=create(TEXTURE,bytes,32);Resource *r=find(token,TEXTURE);if(!r)return 0;
    r->levels=(int)levels;r->width=w;r->height=h;r->format=format;r->linear=linear!=0;memcpy(r->data,p+32,(size_t)bytes);
    dnd_draw_backend_flush(r->data,bytes,2);return token;
}
/* Allocates GX-native tiled texture storage; upload only between frames. */
int dnd_texture_allocate_native(int w,int h,int format,int linear) {
    int bytes=texture_size(w,h,format);
    if(dnd_device_active() || dnd_raw_depth() || bytes<0 || (linear!=0&&linear!=1))return 0;
    int token=create(TEXTURE,bytes,32);
    Resource *r=find(token,TEXTURE);
    if(!r)return 0;
    r->width=w;r->height=h;r->format=format;r->linear=linear;
    dnd_draw_backend_flush(r->data,bytes,2);
    return token;
}
int dnd_texture_update_native(int token,DndArray *payload,int source_offset,int count) {
    Resource *r=find(token,TEXTURE);
    if(dnd_device_active() || dnd_raw_depth() || !r || !payload ||
       payload->elements_are_references || payload->element_size!=1 ||
       source_offset<0 || count!=r->bytes ||
       (uint32_t)source_offset>payload->length ||
       (uint32_t)count>payload->length-(uint32_t)source_offset)return -1;
    /* Conservative synchronization: prevent stale queued GX reads before replacement. */
    dnd_draw_backend_wait();
    memcpy(r->data,(unsigned char*)payload->data+source_offset,(size_t)count);
    dnd_draw_backend_flush(r->data,count,2);
    return 0;
}
/* Native MEM1 -> GX texture copy. Allows ARAM/storage promotion without a
 * managed byte-array roundtrip. Source must remain valid for this call. */
int dnd_texture_update_buffer(int texture_token,int buffer_token,int source_offset,int count) {
    Resource *texture=find(texture_token,TEXTURE),*source=find(buffer_token,BUFFER);
    if(dnd_device_active() || dnd_raw_depth() || !texture || !source || source->io_busy ||
       source_offset<0 || count!=texture->bytes || source_offset>source->bytes ||
       count>source->bytes-source_offset)return -1;
    dnd_draw_backend_wait();
    memcpy(texture->data,source->data+source_offset,(size_t)count);
    dnd_draw_backend_flush(texture->data,count,2);
    return 0;
}
/* Detached upload: the managed double buffer guarantees that this token is not
 * bound in any in-flight GPU command. Do not touch GX on the worker executor. */
int dnd_texture_stage_array(int token,DndArray *payload,int offset,int count) {
    Resource *r=find(token,TEXTURE);
    if(!r || !payload || payload->elements_are_references || payload->element_size!=1 ||
       offset<0 || count!=r->bytes || (uint32_t)offset>payload->length ||
       (uint32_t)count>payload->length-(uint32_t)offset)return -1;
    memcpy(r->data,(unsigned char*)payload->data+offset,(size_t)count);
    dnd_draw_backend_flush(r->data,count,0);return 0;
}
int dnd_texture_stage_buffer(int token,int buffer_token,int offset,int count) {
    Resource *r=find(token,TEXTURE);
    if(!r||offset<0||count!=r->bytes)return -1;
    /* Held native source stays readable even if managed Dispose occurred
     * after the asynchronous transfer was queued. */
    uint64_t address=dnd_buffer_held_address(buffer_token,offset,count);
    if(!address)return -1;
    memcpy(r->data,(const void*)(uintptr_t)address,(size_t)count);
    dnd_draw_backend_flush(r->data,count,0);return 0;
}
int dnd_texture_publish_staged(int token) {
    dnd_async_require_application_thread();
    if(dnd_raw_depth() || dnd_device_pass()==3 || !find(token,TEXTURE))return -1;
    /* The backend issues GX_InvalidateTexAll only; no GPU completion wait. */
    dnd_draw_backend_publish_texture();
    return 0;
}
static void texture_convert_rgba(Resource *r,const unsigned char *src) {
    int w=r->width,h=r->height;
    memset(r->data,0,(size_t)r->bytes);
    int blocks=(w+3)/4;
    for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
        int block=((y/4)*blocks+x/4)*64,at=((y&3)*4+(x&3))*2,source=(y*w+x)*4;
        r->data[block+at]=src[source+3];r->data[block+at+1]=src[source];
        r->data[block+32+at]=src[source+1];r->data[block+33+at]=src[source+2];
    }
 }
int dnd_texture_replace_rgba(int token,DndArray *rgba,int offset) {
    Resource *r=find(token,TEXTURE);
    if(dnd_device_active()||dnd_raw_depth()||!r||r->format!=6||r->levels!=1||!rgba||rgba->elements_are_references||rgba->element_size!=1||offset<0||(uint32_t)offset>rgba->length||(uint32_t)(r->width*r->height*4)>rgba->length-(uint32_t)offset)return -1;
    dnd_draw_backend_wait();texture_convert_rgba(r,rgba->data+offset);
    dnd_draw_backend_flush(r->data,r->bytes,2);return 0;
}
static int texture_rgba(int w,int h,const unsigned char *src,int linear) {
    int bytes=texture_size(w,h,6);
    if(dnd_device_active() || bytes<0 || !src)return 0;
    int token=create(TEXTURE,bytes,32);Resource *r=find(token,TEXTURE);if(!r)return 0;
    r->width=w;r->height=h;r->format=6;r->linear=linear!=0;
    texture_convert_rgba(r,src);
    dnd_draw_backend_flush(r->data,bytes,2);return token;
}
int dnd_texture_create_range(int w,int h,DndArray *rgba,int offset,int linear) {
    if(texture_size(w,h,6)<0||!rgba||rgba->element_size!=1||rgba->elements_are_references||offset<0||(uint32_t)offset>rgba->length||(uint32_t)(w*h*4)>rgba->length-(uint32_t)offset)return 0;
    return texture_rgba(w,h,rgba->data+offset,linear);
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
int dnd_texture_dimension(int token,int axis) { Resource *r=find(token,TEXTURE);return !r?-1:axis==0?r->width:axis==1?r->height:axis==3?r->levels:r->format; }
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
static void mesh_color(Resource *r) {
    const DndDrawVertex *v=(const DndDrawVertex*)r->data;
    r->mesh_color=v[0].color;r->uniform_color=true;
    for(int i=1;i<r->vertices;i++)if(v[i].color!=r->mesh_color){r->uniform_color=false;break;}
}
int dnd_mesh_create_range(DndArray *a,int n,DndArray *indices,int m,int format,int dynamic) {
    if(dnd_device_active() || !a || n<1 || n>65535 || !array_range(a,0,n) || m<0 || (!indices && m) || !vertex_size(format)) return 0;
    if((m?m:n)%3 || m>3*65535 || (indices && (!array_range(indices,0,m)||indices->element_size!=2))) return 0;
    int token=create(MESH,n*(int)sizeof(DndDrawVertex)+m*2,32);Resource *r=find(token,MESH);if(!r)return 0;
    r->vertices=n;r->indices=m;r->dynamic=dynamic!=0;r->format=format;
    if(!vertices(a,0,n,format,(DndDrawVertex*)r->data)){release(r);return 0;}
    uint16_t *index=(uint16_t*)(r->data+n*sizeof(DndDrawVertex));
    if(m){memcpy(index,indices->data,(size_t)m*2);for(int i=0;i<m;i++)if(index[i]>=n){release(r);return 0;}}
    mesh_color(r);dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();return token;
}
int dnd_mesh_create(DndArray *a,DndArray *indices,int format,int dynamic) {
    return dnd_mesh_create_range(a,a?(int)a->length:0,indices,indices?(int)indices->length:0,format,dynamic);
}

int dnd_mesh_update(int token,DndArray *a,int offset,int count,int format) {
    Resource *r=find(token,MESH);
    if(dnd_device_active()||!r||!r->dynamic||r->format!=format||offset<0||count<0||offset>r->vertices||count>r->vertices-offset||!array_range(a,0,count))return -1;
    // Validate the entire update before committing any part of it.
    for(int start=0;start<count;start+=768) { int n=count-start;if(n>768)n=768;if(!vertices(a,start,n,format,staging))return -1; }
    for(int start=0;start<count;start+=768) { int n=count-start;if(n>768)n=768;(void)vertices(a,start,n,format,staging);memcpy(r->data+(size_t)(offset+start)*sizeof(DndDrawVertex),staging,(size_t)n*sizeof(DndDrawVertex)); }
    mesh_color(r);dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();return 0;
}
int dnd_texture_sampler(int token,int wrap_s,int wrap_t,int min_filter,int mag_filter,float lod_bias) {
    Resource *r=find(token,TEXTURE);
    if(!r || dnd_device_active() || dnd_raw_depth() ||
       wrap_s<0 || wrap_s>2 || wrap_t<0 || wrap_t>2 ||
       min_filter<0 || min_filter>5 || mag_filter<0 || mag_filter>1 ||
       !isfinite(lod_bias) || lod_bias < -4.0f || lod_bias > 3.99f ||
       (r->levels<=1 && min_filter>=2))return -1;
    r->sampler_set=true;
    r->wrap_s=wrap_s;r->wrap_t=wrap_t;
    r->min_filter=min_filter;r->mag_filter=mag_filter;r->lod_bias=lod_bias;
    dnd_draw_backend_mesh_changed();
    return 0;
}
static bool texture(int token,DndDrawTexture *out) {
    memset(out,0,sizeof(*out));if(!token)return true;
    Resource *r=find(token,TEXTURE);if(!r)return false;
    *out=(DndDrawTexture){r->data,(uint32_t)r->bytes,r->width,r->height,r->format,r->linear!=0,r->levels,r->sampler_set,r->wrap_s,r->wrap_t,r->min_filter,r->mag_filter,r->lod_bias};return true;
}
int dnd_texture_resolve(int token,DndDrawTexture *out){return texture(token,out)?0:-1;}
static int mesh_submit(Resource *r,int tex,DndDrawTexture tx,DndDrawTexture extra,int secondary,const float *transform,uint32_t color,int flags,float cutoff,const DndDrawEffect *effect) {
    DndDrawMaterial material={color,flags,cutoff,effect,secondary?&extra:NULL};
    DndDrawVertex *v=(DndDrawVertex*)r->data;uint16_t *idx=(uint16_t*)(r->data+r->vertices*sizeof(DndDrawVertex));
    int count=r->indices?r->indices:r->vertices;
    if(r->uniform_color&&dnd_draw_backend_mesh(v,r->indices?idx:NULL,count,r->mesh_color,tex?&tx:NULL,&material,transform))
    {dnd_performance_add_triangles((uint32_t)count/3);return 0;}
    for(int start=0;start<count;) {
        int n=count-start;if(n>768)n=768;
        for(int j=0;j<n;j++)staging[j]=v[r->indices?idx[start+j]:start+j];
        dnd_draw_backend_vertices(staging,n,0x90,tex?&tx:NULL,&material,transform);start+=n;
    }
    dnd_performance_add_triangles((uint32_t)count/3);return 0;
}
int dnd_mesh_draw_effect(int token,int tex,DndArray *world,uint32_t color,int flags,float cutoff,int effect) {
    return dnd_mesh_draw_material(token,tex,0,world,color,flags,cutoff,effect);
}
int dnd_mesh_draw_material(int token,int tex,int secondary,DndArray *world,uint32_t color,int flags,float cutoff,int effect) {
    Resource *r=find(token,MESH);DndDrawTexture tx,extra;float transform[16];
    if(dnd_device_pass()!=1||!r||!texture(tex,&tx)||!matrix(world,transform)||flags<0||flags>255||(flags&192)==192||!texture(secondary,&extra)||!isfinite(cutoff)||cutoff<0||cutoff>1)return -1;
    /* Reflection coordinates require authored normals, not the fallback +Z used
     * by color-only meshes. Reject before any submission or triangle counters. */
    if((flags&192)&&((!tex&&!secondary)||r->format!=2))return -1;
    Resource *fx=effect?find(effect,EFFECT):NULL;
    if(effect&&(!fx||((((DndDrawEffect*)fx->data)->texture&1)&&!tex)||((((DndDrawEffect*)fx->data)->texture&2)&&!secondary)))return -1;
    return mesh_submit(r,tex,tx,extra,secondary,transform,color,flags,cutoff,fx?(DndDrawEffect*)fx->data:NULL);
}

/* A merged GX stream for meshes with one material and the SAME world matrix.
 * Validates every source before submitting any draw. 768 vertices per GX_Begin
 * preserves the existing staging limit, and keeps triangle boundaries intact. */
int dnd_mesh_draw_batch(DndArray *mesh_tokens,int count,int tex,int secondary,DndArray *world,
                        uint32_t color,int flags,float cutoff,int effect) {
    DndDrawTexture tx,extra;float transform[16];
    if(dnd_device_pass()!=1 || !mesh_tokens || mesh_tokens->elements_are_references ||
       mesh_tokens->element_size!=4 || count<2 || count>128 ||
       (uint32_t)count>mesh_tokens->length || !matrix(world,transform) ||
       !texture(tex,&tx) || !texture(secondary,&extra) ||
       flags<0 || flags>255 || (flags&192)==192 ||
       !isfinite(cutoff) || cutoff<0 || cutoff>1)return -1;
    Resource *fx=effect?find(effect,EFFECT):NULL;
    if(effect&&(!fx||((((DndDrawEffect*)fx->data)->texture&1)&&!tex)||
       ((((DndDrawEffect*)fx->data)->texture&2)&&!secondary)))return -1;
    const int *tokens=(const int*)mesh_tokens->data;
    uint32_t triangles=0;
    for(int i=0;i<count;i++) {
        Resource *r=find(tokens[i],MESH);
        if(!r || ((flags&192) && ((!tex&&!secondary)||r->format!=2)))return -1;
        int n=r->indices?r->indices:r->vertices;
        if(n<3 || n%3 || (uint32_t)n>UINT32_MAX-3*triangles)return -1;
        triangles+=(uint32_t)n/3;
    }
    DndDrawMaterial mat={color,flags,cutoff,fx?(DndDrawEffect*)fx->data:NULL,secondary?&extra:NULL};
    int staged=0;
    for(int i=0;i<count;i++) {
        Resource *r=find(tokens[i],MESH);
        const DndDrawVertex *src=(const DndDrawVertex*)r->data;
        const uint16_t *indices=(const uint16_t*)(r->data+r->vertices*sizeof(DndDrawVertex));
        int n=r->indices?r->indices:r->vertices;
        for(int j=0;j<n;j++) {
            staging[staged++]=src[r->indices?indices[j]:j];
            if(staged==768) {
                dnd_draw_backend_vertices(staging,staged,0x90,tex?&tx:NULL,&mat,transform);
                staged=0;
            }
        }
    }
    if(staged)dnd_draw_backend_vertices(staging,staged,0x90,tex?&tx:NULL,&mat,transform);
    dnd_performance_add_triangles(triangles);
    return 0;
}
int dnd_mesh_draw(int token,int tex,DndArray *world,uint32_t color,int flags,float cutoff) {
    return dnd_mesh_draw_effect(token,tex,world,color,flags,cutoff,0);
}
int dnd_mesh_primitives(DndArray *a,int offset,int count,int format,int primitive,DndArray *world,uint32_t color,int flags) {
    DndDrawMaterial material={color,flags,0,NULL,NULL};float transform[16];
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
static int canvas_viewport[4];
static bool canvas_empty_clip;
int dnd_canvas_begin(int token,int w,int h) {
    Resource *r=find(token,CANVAS);if(!r||w<1||h<1||w>16384||h>16384||dnd_device_pass_begin(2)<0)return -1;
    active_canvas=token;r->count=0;r->texture=0;r->effect=0;r->blend=1;r->draws=0;r->flushes=0;r->width=w;r->height=h;r->logical_width=(float)w;r->logical_height=(float)h;
    dnd_draw_backend_2d(w,h);canvas_empty_clip=false;canvas_viewport[0]=canvas_viewport[1]=0;canvas_viewport[2]=dnd_device_width();canvas_viewport[3]=dnd_device_height();return 0;
}
int dnd_canvas_flush(int token) {
    Resource *r=find(token,CANVAS);if(!r||token!=active_canvas||dnd_device_pass()!=2)return -1;
    if(r->count) {
        DndDrawTexture tx;
        Resource *retired=NULL;
        int ti=(r->texture&1023)-1;
        if(r->texture>0&&ti>=0&&ti<SLOTS&&slots[ti].generation==(r->texture>>10)&&slots[ti].kind==TEXTURE)retired=&slots[ti];
        if(retired)tx=(DndDrawTexture){retired->data,(uint32_t)retired->bytes,retired->width,retired->height,retired->format,retired->linear!=0,retired->levels,retired->sampler_set,retired->wrap_s,retired->wrap_t,retired->min_filter,retired->mag_filter,retired->lod_bias};
        else if(!texture(r->texture,&tx))return -1;
        Resource *fx=NULL;
        if(r->effect){int index=(r->effect&1023)-1;
            if(index<0||index>=SLOTS||slots[index].kind!=EFFECT||slots[index].generation!=(r->effect>>10))return -1;
            fx=&slots[index];
        }
        DndDrawMaterial mat={0xffffffffu,r->blend|8|16,0,fx?(DndDrawEffect*)fx->data:NULL,NULL};
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
    if(canvas_empty_clip)return 0;
    if(r->count && (r->texture!=tex || r->count+count>r->capacity))if(dnd_canvas_flush(token)<0)return -1;
    r->texture=tex;DndDrawVertex *v=(DndDrawVertex*)r->data+r->count;
    for(int i=0;i<count;i++)v[i]=(DndDrawVertex){p[i*4],p[i*4+1],0,0,0,1,p[i*4+2],p[i*4+3],color};
    r->count+=count;return 0;
}
int dnd_canvas_rectangle(int token,float x,float y,float w,float h,uint32_t color) {
    Resource *r=find(token,CANVAS);
    if(!r||token!=active_canvas||dnd_device_pass()!=2||!isfinite(x)||!isfinite(y)||!isfinite(w)||!isfinite(h)||w<0||h<0||!isfinite(x+w)||!isfinite(y+h))return -1;
    if(w==0||h==0||canvas_empty_clip)return 0;
    Resource *fx=r->effect?find(r->effect,EFFECT):NULL;
    if(r->effect&&(!fx||((DndDrawEffect*)fx->data)->texture))return -1;
    if(r->count&&(r->texture||r->count+6>r->capacity))if(dnd_canvas_flush(token)<0)return -1;
    r->texture=0;DndDrawVertex *v=(DndDrawVertex*)r->data+r->count;
    v[0]=(DndDrawVertex){x,y,0,0,0,1,0,0,color};v[1]=v[0];v[1].x=x+w;v[2]=v[1];v[2].y=y+h;
    v[3]=v[0];v[4]=v[2];v[5]=v[0];v[5].y=y+h;r->count+=6;return 0;
}
int dnd_canvas_end(int token) { if(dnd_canvas_flush(token)<0)return -1;int result=dnd_device_pass_end(2);if(result==0)active_canvas=0;return result; }
int dnd_canvas_clip(int token,int x,int y,int w,int h) {
    Resource *r=find(token,CANVAS);
    if(!r||x<0||y<0||w<0||h<0||x>r->width-w||y>r->height-h||dnd_canvas_flush(token)<0)return -1;
    canvas_empty_clip=w==0||h==0;if(canvas_empty_clip)return 0;
    int screen_w=canvas_viewport[2],screen_h=canvas_viewport[3];
    int left=(int)(x*screen_w/r->logical_width),top=(int)(y*screen_h/r->logical_height);
    int right=(int)((x+w)*screen_w/r->logical_width),bottom=(int)((y+h)*screen_h/r->logical_height);
    if(right>screen_w)right=screen_w;
    if(bottom>screen_h)bottom=screen_h;
    /* Positive logical clips may disappear under viewport scaling. GX uses
     * inclusive endpoints and must never receive zero scissor extents. */
    canvas_empty_clip=right<=left||bottom<=top;
    if(canvas_empty_clip)return 0;
    dnd_draw_backend_scissor(left+canvas_viewport[0],top+canvas_viewport[1],right-left,bottom-top);return 0;
}
int dnd_canvas_blend(int token,int blend) {
    Resource *r=find(token,CANVAS);if(!r||blend<0||blend>3||dnd_canvas_flush(token)<0)return -1;r->blend=blend;return 0;
}
int dnd_canvas_stats(int token,int kind) { Resource *r=find(token,CANVAS);return !r?-1:kind==0?r->draws:kind==1?r->flushes:r->count; }
int dnd_draw_copy_texture(int token,int x,int y,int w,int h) {
    Resource *r=find(token,TEXTURE);DndDrawTexture tx;
    if(!r||!dnd_device_active()||dnd_device_pass()||!texture(token,&tx)||r->format!=6||r->levels!=1||((r->width|r->height)&3)||((x|y)&1)||!((w==r->width&&h==r->height)||(w==r->width*2&&h==r->height*2))||x<0||y<0||x>dnd_device_width()-w||y>dnd_device_height()-h)return -1;
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
uint32_t dnd_buffer_crc32(int token,int offset,int count,uint32_t initial) {
    Resource *r=find(token,BUFFER);
    if(dnd_device_active()||dnd_raw_depth()||!r||r->io_busy||offset<0||count<0||offset>r->bytes||count>r->bytes-offset){
        dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Invalid native checksum range or active frame.");return 0;
    }
    return dnd_crc32(r->data+offset,(size_t)count,initial);
}

/* Programs are immutable. Uniform writes are forbidden while a frame is active. */
int dnd_effect_create(DndArray *a) {
    dnd_async_require_application_thread();if(dnd_exception_pending())return 0;
    if(dnd_device_active()||dnd_raw_depth()||!a||a->element_size!=1||a->elements_are_references||a->length<184||a->length>304)return 0;
    const unsigned char *p=a->data;
    unsigned version=le32(p+4),count=le32(p+8),texture_flag=le32(p+12),uniforms=le32(p+16);
    if(memcmp(p,"DNEF",4)||(version!=1&&version!=2)||count<1||count>16||texture_flag>(version==1?1u:3u)||uniforms>4||a->length!=176+count*8||le32(p+20)||le32(p+24)||le32(p+28))return 0;
    for(unsigned i=0;i<4;i++) {
        const unsigned char *name=p+32+i*36;int end=0;
        for(int j=0;j<32;j++){if(!name[j])end=1;else if(end||name[j]<33||name[j]>126)return 0;}
        if(!end||(i<uniforms?!name[0]:name[0]!=0))return 0;
        for(unsigned j=0;j<i&&i<uniforms;j++)if(!strcmp((const char*)name,(const char*)p+32+j*36))return 0;
    }
    unsigned initialized=0;
    for(unsigned i=0;i<count;i++) {
        const DndTevStage *s=(const DndTevStage*)(p+176+i*8);
        if((s->op!=0&&s->op!=8)||s->dest>3||s->reserved>(version==1?0:1)||(s->konst!=0&&(s->konst<12||s->konst>31)))return 0;
        if(s->reserved&&!(texture_flag&2))return 0;
        const unsigned char inputs[]={s->a,s->b,s->c,s->d};
        for(int j=0;j<4;j++) {
            unsigned v=inputs[j];
            if(v==0||v==2||v==4||v==6){if(!(initialized&(1u<<(v/2))))return 0;}
            else if(v==14){unsigned k=s->konst;if(k<12||k>31||((k-12)%4)>=uniforms)return 0;}
            else if(v==8){if(!(texture_flag&(1u<<s->reserved)))return 0;}
            else if(v!=10&&v!=12&&v!=15)return 0;
        }
        initialized|=1u<<s->dest;
    }
    if(p[176+(count-1)*8+5]!=0)return 0;
    /* Version 2 ends on TEX0 so auxiliary alpha never changes base opacity. */
    if(version==2&&p[176+(count-1)*8+7]!=0)return 0;
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
    if(!r||token!=active_canvas||(effect&&(!fx||(((DndDrawEffect*)fx->data)->texture&2)))||dnd_device_pass()!=2)return -1;
    if(r->effect==effect)return 0;
    if(dnd_canvas_flush(token)<0)return -1;
    r->effect=effect;return 0;
}

int dnd_canvas_viewport(int token,int x,int y,int w,int h) {
    Resource *r=find(token,CANVAS);
    if(!r||token!=active_canvas||x<0||y<0||w<=0||h<=0||x>dnd_device_width()-w||y>dnd_device_height()-h||dnd_canvas_flush(token)<0)return -1;
    canvas_empty_clip=false;canvas_viewport[0]=x;canvas_viewport[1]=y;canvas_viewport[2]=w;canvas_viewport[3]=h;
    dnd_draw_backend_viewport(x,y,w,h);dnd_draw_backend_scissor(x,y,w,h);return 0;
}
int dnd_canvas_sprites(int token,DndArray *textures,DndArray *a,DndArray *colors,int count) {
    Resource *r=find(token,CANVAS);DndDrawTexture tx;
    if(!r||token!=active_canvas||dnd_device_pass()!=2||count<0||count>128||
       !textures||!colors||!a||textures->element_size!=4||colors->element_size!=4||a->element_size!=4||
       textures->elements_are_references||colors->elements_are_references||a->elements_are_references||
       textures->length<(unsigned)count||colors->length<(unsigned)count||a->length<(unsigned)count*24)return -1;
    const int *ids=(const int*)textures->data;const uint32_t *tints=(const uint32_t*)colors->data;const float *p=(const float*)a->data;
    Resource *fx=r->effect?find(r->effect,EFFECT):NULL;
    if(r->effect&&!fx)return -1;
    for(int i=0;i<count;i++)if(!ids[i]||!texture(ids[i],&tx))return -1;
    for(int i=0;i<count*24;i++)if(!isfinite(p[i]))return -1;
    if(canvas_empty_clip)return 0;
    for(int i=0;i<count;i++){
        if(r->count&&(r->texture!=ids[i]||r->count+6>r->capacity))if(dnd_canvas_flush(token)<0)return -1;
        r->texture=ids[i];DndDrawVertex *v=(DndDrawVertex*)r->data+r->count;
        for(int j=0;j<6;j++){const float *q=p+i*24+j*4;v[j]=(DndDrawVertex){q[0],q[1],0,0,0,1,q[2],q[3],tints[i]};}r->count+=6;
    }return 0;
}

#include "dnd_render_state.h"
__attribute__((weak)) void dnd_draw_backend_options(const float *p,int scene){(void)p;(void)scene;}
static int render_options(DndArray *a,int scene) {
    if(dnd_device_pass()!=1&&dnd_device_pass()!=2)return -1;
    const float *p=NULL;
    if(a){if(a->element_size!=4||a->elements_are_references||a->length!=(scene?136u:20u))return -1;
        p=(const float*)a->data;if(scene?!dnd_render_scene_valid(p):!dnd_render_style_valid(p))return -1;}
    dnd_draw_backend_options(p,scene);return 0;
}
int dnd_render_scene(DndArray *a){return render_options(a,1);}
int dnd_render_material(DndArray *a){return render_options(a,0);}
int dnd_mesh_draw_many(DndArray *a,DndArray *w,int count,int tex,int secondary,uint32_t color,int flags,float cutoff,int effect){
    DndDrawTexture tx,extra;
    if(dnd_device_pass()!=1||!a||!w||a->element_size!=4||w->element_size!=sizeof(DndArray*)||a->elements_are_references||!w->elements_are_references||count<0||count>128||a->length<(unsigned)count||w->length<(unsigned)count||flags<0||flags>255||(flags&192)==192||!isfinite(cutoff)||cutoff<0||cutoff>1||!texture(tex,&tx)||!texture(secondary,&extra))return -1;
    Resource *fx=effect?find(effect,EFFECT):NULL;
    if(effect&&(!fx||((((DndDrawEffect*)fx->data)->texture&1)&&!tex)||((((DndDrawEffect*)fx->data)->texture&2)&&!secondary)))return -1;
    const int *ids=(const int*)a->data;DndArray *const *world=(DndArray*const*)w->data;
    for(int i=0;i<count;i++){
        Resource *r=find(ids[i],MESH);if(!r||((flags&192)&&((!tex&&!secondary)||r->format!=2)))return -1;
        if(!world[i]||world[i]->element_size!=4||world[i]->elements_are_references||world[i]->length!=16)return -1;
        const float *m=(const float*)world[i]->data;for(int j=0;j<16;j++)if(!isfinite(m[j]))return -1;
        float determinant=m[0]*(m[5]*m[10]-m[9]*m[6])-m[4]*(m[1]*m[10]-m[9]*m[2])+m[8]*(m[1]*m[6]-m[5]*m[2]);
        if((flags&224)&&(!isfinite(determinant)||determinant==0))return -1;
    }
    for(int i=0;i<count;i++)mesh_submit(find(ids[i],MESH),tex,tx,extra,secondary,(const float*)world[i]->data,color,flags,cutoff,fx?(DndDrawEffect*)fx->data:NULL);
    return 0;
}

/* Retained 2D triangles reside in an immutable mesh resource. Flush painter
 * order, then consume native geometry directly without managed vertex staging. */
int dnd_canvas_geometry(int token,int mesh,int tex,DndArray *world,uint32_t color){
    Resource *canvas=find(token,CANVAS),*geometry=find(mesh,MESH);
    DndDrawTexture tx;float transform[16];
    if(!canvas||token!=active_canvas||dnd_device_pass()!=2||!geometry||geometry->indices||
       !texture(tex,&tx)||!matrix(world,transform))return -1;
    Resource *fx=canvas->effect?find(canvas->effect,EFFECT):NULL;
    if(canvas->effect&&(!fx||(((DndDrawEffect*)fx->data)->texture&&!tex)))return -1;
    if(canvas_empty_clip)return 0;
    if(dnd_canvas_flush(token)<0)return -1;
    DndDrawMaterial material={color,canvas->blend|8|16,0,fx?(DndDrawEffect*)fx->data:NULL,NULL};
    const DndDrawVertex *vertices=(const DndDrawVertex*)geometry->data;
    if(!geometry->uniform_color||!dnd_draw_backend_mesh(vertices,NULL,geometry->vertices,
            geometry->mesh_color,tex?&tx:NULL,&material,transform))
        dnd_draw_backend_vertices(vertices,geometry->vertices,0x90,tex?&tx:NULL,&material,transform);
    dnd_performance_add_triangles((uint32_t)geometry->vertices/3);
    canvas->draws++;return 0;
}

int dnd_canvas_layout(int token,float w,float h){
    Resource *r=find(token,CANVAS);
    if(!r||token!=active_canvas||dnd_device_pass()!=2||!isfinite(w)||!isfinite(h)||w<1||h<1||w>16384||h>16384)return -1;
    if(dnd_canvas_flush(token)<0)return -1;
    r->logical_width=w;r->logical_height=h;r->width=(int)ceilf(w);r->height=(int)ceilf(h);
    dnd_draw_backend_2d_projection(w,h);return 0;
}
