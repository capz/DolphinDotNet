#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static bool use_indexed;static int indexed_calls;
static bool frame;static int pass,draws,copies;static uint32_t tint;static float world_x;
bool dnd_device_active(void){return frame||pass!=0;}
int dnd_device_pass(void){return pass;}
int dnd_device_pass_begin(int p){if(pass)return -1;pass=p;return 0;}
int dnd_device_pass_end(int p){assert(pass==p);pass=0;return 0;}
int dnd_device_width(void){return 640;}int dnd_device_height(void){return 480;}int dnd_raw_depth(void){return 0;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_flush(void *p,int n,int i){(void)p;(void)n;(void)i;}
void dnd_draw_backend_2d(int w,int h){assert(w==640&&h==480);}
void dnd_draw_backend_scissor(int x,int y,int w,int h){(void)x;(void)y;assert(w>0&&h>0);}
void dnd_performance_add_triangles(uint32_t n){assert(n>0);}
void dnd_exception_throw(DndExceptionKind k,const char *m){(void)k;(void)m;abort();}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *tx,const DndDrawMaterial *m,const float *world){
 assert(v&&n==3&&primitive==0x90&&!tx);assert(v[1].x==2);tint=m->color;world_x=world[12];draws++;
}
void dnd_draw_backend_copy(const DndDrawTexture *tx,int x,int y,int w,int h){assert(tx->levels==1&&x==0&&y==0&&w==8&&h==8&&tx->width==4&&tx->height==4);copies++;}
bool dnd_draw_backend_mesh(const DndDrawVertex *v,const uint16_t *indices,int count,uint32_t uniform,
                           const DndDrawTexture *tx,const DndDrawMaterial *material,const float *world){
 if(!use_indexed)return false;
 assert(v&&v[1].x==2&&!indices&&count==3&&uniform==0&&!tx);
 indexed_calls++;draws++;tint=material->color;world_x=world[12];return true;
}
static DndArray *array(unsigned count,unsigned stride){DndArray *a=calloc(1,sizeof(*a)+(size_t)count*stride);assert(a);a->length=count;a->element_size=stride;return a;}
static void put(DndArray *a,int at,unsigned value){for(int i=0;i<4;i++)a->data[at+i]=(unsigned char)(value>>(i*8));}
static DndArray *package(void){DndArray *a=array(224,1);memcpy(a->data,"DNTX",4);put(a,4,2);put(a,8,6);put(a,12,4);put(a,16,4);put(a,20,192);put(a,24,3);return a;}
int main(void){
 DndArray *p=package();int tex=dnd_texture_package(p,1);assert(tex&&dnd_texture_dimension(tex,3)==3);
 DndDrawTexture tx;assert(dnd_texture_resolve(tex,&tx)==0&&tx.levels==3&&tx.bytes==192);
 DndArray *rgba=array(64,1);assert(dnd_texture_replace_rgba(tex,rgba,0)<0);
 frame=true;assert(dnd_draw_copy_texture(tex,0,0,4,4)<0);frame=false;
 assert(dnd_draw_release(tex)==0);
 for(int at=4;at<=28;at+=4){unsigned char saved[4];memcpy(saved,p->data+at,4);put(p,at,at==24?4:0xffffffffu);assert(!dnd_texture_package(p,0));memcpy(p->data+at,saved,4);}
 p->length--;assert(!dnd_texture_package(p,0));p->length++;
 put(p,12,3);assert(!dnd_texture_package(p,0));put(p,12,4);
 // v1 remains readable with zero reserved words.
 put(p,4,1);put(p,20,64);put(p,24,0);p->length=96;tex=dnd_texture_package(p,0);assert(tex&&dnd_texture_dimension(tex,3)==1);
 DndArray *v=array(3,16),*matrix=array(16,4);float *m=(float*)matrix->data;for(int j=0;j<16;j++)m[j]=j%5==0?1:0;m[12]=7;
 float x=2;memcpy(v->data+16,&x,4);int mesh=dnd_mesh_create(v,NULL,0,0);assert(mesh);
 int canvas=dnd_canvas_create(6);assert(dnd_canvas_begin(canvas,640,480)==0);
 assert(dnd_canvas_geometry(canvas,mesh,0,matrix,0x12345678)==0&&draws==1&&tint==0x12345678&&world_x==7);
 use_indexed=true;
 assert(dnd_canvas_geometry(canvas,mesh,0,matrix,0x12345678)==0&&draws==2&&indexed_calls==1&&world_x==7);
 m[0]=NAN;assert(dnd_canvas_geometry(canvas,mesh,0,matrix,0)<0&&draws==2);m[0]=1;
 assert(dnd_canvas_clip(canvas,0,0,0,0)==0);assert(dnd_canvas_geometry(canvas,mesh,0,matrix,0)==0&&draws==2);
 assert(dnd_canvas_clip(canvas,0,0,640,480)==0);
 assert(dnd_draw_release(mesh)==0);assert(dnd_canvas_geometry(canvas,mesh,0,matrix,0)<0);
 assert(dnd_canvas_end(canvas)==0);dnd_draw_collect();pass=4;
 assert(dnd_draw_copy_texture(tex,0,0,8,8)<0);pass=0;
 frame=true;assert(dnd_draw_copy_texture(tex,0,0,8,8)==0&&copies==1);
 assert(dnd_draw_copy_texture(tex,0,0,12,12)<0&&copies==1);
 frame=false;assert(copies==1&&dnd_draw_release(tex)==0&&dnd_draw_release(canvas)==0&&dnd_draw_total_bytes()==0);
 free(p);free(rgba);free(v);free(matrix);puts("v1/v2 mip validation and retained native geometry contracts passed.");
}
