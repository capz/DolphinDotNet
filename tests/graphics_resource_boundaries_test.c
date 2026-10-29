#include "dnd_draw.h"
#include <assert.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static bool frame;
static int pass, copies, submissions, counts[8];
bool dnd_device_active(void){return frame||pass!=0;}
int dnd_device_pass(void){return pass;}
int dnd_device_pass_begin(int p){if(pass)return -1;pass=p;return 0;}
int dnd_device_pass_end(int p){assert(pass==p);pass=0;return 0;}
int dnd_device_width(void){return 640;}
int dnd_device_height(void){return 480;}
int dnd_raw_depth(void){return 0;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_flush(void *p,int n,int i){(void)p;(void)n;(void)i;}
void dnd_draw_backend_2d(int w,int h){assert(w==640&&h==480);}
void dnd_performance_add_triangles(uint32_t n){assert(n>0);}
void dnd_exception_throw(DndExceptionKind k,const char *m){(void)k;(void)m;abort();}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){
    (void)t;(void)m;(void)w;assert(v&&primitive==0x90&&submissions<8);counts[submissions++]=n;
}
void dnd_draw_backend_copy(const DndDrawTexture *t,int x,int y,int w,int h){
    assert(t->format==6&&t->levels==1&&x>=0&&y>=0&&w>0&&h>0);copies++;
}
static DndArray *array(unsigned count,unsigned stride){
    DndArray *a=calloc(1,sizeof(*a)+(size_t)count*stride);assert(a);a->length=count;a->element_size=stride;return a;
}
static void put(DndArray *a,int at,unsigned n){for(int i=0;i<4;i++)a->data[at+i]=(unsigned char)(n>>(i*8));}
static DndArray *package(int format,int levels,int bytes){
    DndArray *a=array(32+bytes,1);memcpy(a->data,"DNTX",4);put(a,4,2);put(a,8,format);
    put(a,12,16);put(a,16,8);put(a,20,bytes);put(a,24,levels);
    for(int i=0;i<bytes;i++)a->data[32+i]=(unsigned char)(i*37+11);
    return a;
}
static void mip_chains(void){
    /* Independent GX block sizes for a rectangular 16x8 -> 1x1 chain. */
    const int formats[]={0,1,2,3,4,5,6,14};
    const int sizes[][5]={{64,32,32,32,32},{128,32,32,32,32},{128,32,32,32,32},
        {256,64,32,32,32},{256,64,32,32,32},{256,64,32,32,32},{512,128,64,64,64},{64,32,32,32,32}};
    for(unsigned f=0;f<sizeof(formats)/sizeof(formats[0]);f++){
        int bytes=0;
        for(int level=1;level<=5;level++){
            bytes+=sizes[f][level-1];DndArray *a=package(formats[f],level,bytes);
            int t=dnd_texture_package(a,1);DndDrawTexture tx;assert(t&&dnd_texture_resolve(t,&tx)==0);
            assert(tx.width==16&&tx.height==8&&tx.levels==level&&tx.bytes==(unsigned)bytes);
            assert(!memcmp(tx.data,a->data+32,(size_t)bytes));assert(dnd_draw_release(t)==0);
            int before=dnd_draw_total_bytes();
            a->length--;assert(!dnd_texture_package(a,0));a->length++;
            put(a,24,6);assert(!dnd_texture_package(a,0));put(a,24,level);
            frame=true;assert(!dnd_texture_package(a,0));frame=false;
            assert(dnd_draw_total_bytes()==before);free(a);
        }
    }
}
static void capture_bounds(void){
    int target=dnd_texture_allocate_native(4,4,6,0);assert(target);
    assert(dnd_draw_copy_texture(target,0,0,4,4)<0&&copies==0);
    frame=true;pass=2;assert(dnd_draw_copy_texture(target,0,0,4,4)<0);pass=0;
    const int invalid[][4]={{1,0,4,4},{0,1,4,4},{-1,0,4,4},{0,-1,4,4},
        {638,0,4,4},{0,478,4,4},{INT_MAX,0,4,4},{0,INT_MAX,4,4},
        {0,0,0,0},{0,0,8,4},{0,0,4,8},{0,0,12,12}};
    for(unsigned i=0;i<sizeof(invalid)/sizeof(invalid[0]);i++){
        const int *r=invalid[i];assert(dnd_draw_copy_texture(target,r[0],r[1],r[2],r[3])<0&&copies==0);
    }
    assert(dnd_draw_copy_texture(target,636,476,4,4)==0&&copies==1);
    assert(dnd_draw_copy_texture(target,632,472,8,8)==0&&copies==2);
    frame=false;
    int odd=dnd_texture_allocate_native(3,4,6,0),format=dnd_texture_allocate_native(4,4,4,0);assert(odd&&format);
    frame=true;assert(dnd_draw_copy_texture(odd,0,0,3,4)<0&&copies==2);
    assert(dnd_draw_copy_texture(format,0,0,4,4)<0&&copies==2);
    assert(dnd_draw_release(target)==0);assert(dnd_draw_copy_texture(target,0,0,4,4)<0&&copies==2);
    frame=false;dnd_draw_collect();assert(dnd_draw_release(odd)==0&&dnd_draw_release(format)==0);
}
static void retained_painter_order(void){
    DndArray *v=array(3,16),*matrix=array(16,4);float *m=(float*)matrix->data;
    for(int i=0;i<16;i++)m[i]=i%5==0?1:0;
    int mesh=dnd_mesh_create(v,NULL,0,0),tex=dnd_texture_allocate_native(4,4,6,0),canvas=dnd_canvas_create(12);assert(mesh&&tex&&canvas);
    frame=true;assert(dnd_canvas_begin(canvas,640,480)==0);
    assert(dnd_canvas_rectangle(canvas,0,0,2,2,0xffffffff)==0&&submissions==0);
    m[5]=NAN;assert(dnd_canvas_geometry(canvas,mesh,tex,matrix,0xffffffff)<0&&submissions==0);m[5]=1;
    assert(dnd_canvas_geometry(canvas,mesh,tex,matrix,0xffffffff)==0&&submissions==2);
    assert(counts[0]==6&&counts[1]==3);
    assert(dnd_canvas_rectangle(canvas,0,0,2,2,0xffffffff)==0&&submissions==2);
    assert(dnd_draw_release(tex)==0);
    assert(dnd_canvas_geometry(canvas,mesh,tex,matrix,0xffffffff)<0&&submissions==2);
    assert(dnd_canvas_end(canvas)==0&&submissions==3&&counts[2]==6);
    assert(dnd_draw_release(mesh)==0);frame=false;dnd_draw_collect();
    assert(dnd_draw_release(canvas)==0);free(v);free(matrix);
}
int main(void){
    mip_chains();capture_bounds();retained_painter_order();
    assert(!pass&&dnd_draw_live()==0&&dnd_draw_total_bytes()==0);
    puts("Graphics boundaries: rectangular mip prefixes, rejected-input allocation, capture alignment and retained painter order passed.");
}
