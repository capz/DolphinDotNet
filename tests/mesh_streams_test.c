#include "dnd_draw.h"
#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <math.h>
static int pass,draws,triangles;static const DndDrawVertex *seen;static uint32_t seen_color;
bool dnd_device_active(void){return pass!=0;}int dnd_device_pass(void){return pass;}int dnd_raw_depth(void){return 0;}
void dnd_draw_backend_wait(void){}void dnd_draw_backend_flush(void *p,int n,int i){(void)p;(void)n;(void)i;}
void dnd_performance_add_triangles(uint32_t n){triangles+=n;}void dnd_performance_drawing_api(void){}
void dnd_exception_throw(DndExceptionKind k,const char *m){(void)k;(void)m;abort();}
bool dnd_draw_backend_mesh(const DndDrawVertex *v,const uint16_t *i,int n,uint32_t c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){(void)i;(void)t;(void)m;(void)w;assert(n==3);seen=v;seen_color=c;draws++;return true;}
bool dnd_draw_backend_mesh_color_stream(const DndDrawVertex *v,const uint16_t *i,int n,const uint32_t *c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){(void)i;(void)t;(void)m;(void)w;assert(n==3&&c[0]==0xff0000ff&&c[1]==0x00ff00ff&&c[2]==0x0000ffff);if(m->flags&32)return false;seen=v;draws++;return true;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int p,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){(void)t;(void)m;(void)w;assert(n==3&&p==0x90&&v[0].x==42&&v[0].color==0xff0000ff&&v[1].color==0x00ff00ff&&v[2].color==0x0000ffff);draws++;}
bool dnd_draw_backend_mesh_passes(const DndDrawVertex *v,uint32_t c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w,const unsigned char *d,int bytes,int count,const unsigned char *colors,const float *uv,int passes){
 (void)v;(void)c;(void)m;(void)w;assert(t&&bytes==32&&count==3&&passes==2&&colors[0]==255&&uv[0]==1);
 const unsigned char expected[]={0x90,0,3,0,0,0,0,0,1,0,1,0,2,0,2};assert(memcmp(d,expected,sizeof expected)==0);for(int i=sizeof expected;i<bytes;i++)assert(d[i]==0);draws+=passes;return true;
}
static DndArray *array(unsigned n,unsigned size){DndArray *a=calloc(1,sizeof(*a)+n*size);assert(a);a->length=n;a->element_size=size;return a;}
int main(void){
 DndArray *v=array(3,16),*c=array(3,4),*w=array(16,4),*rgba=array(64,1),*uv=array(12,4);
 for(int i=0;i<3;i++){memset(v->data+i*16+12,255,4);memset(c->data+i*4,255,4);}for(int i=0;i<16;i++)((float*)w->data)[i]=i%5==0?1:0;
 int mesh=dnd_mesh_create(v,NULL,0,1),view=dnd_mesh_color_view(mesh,c);assert(mesh&&view&&dnd_draw_bytes(view)==12&&dnd_draw_total_bytes()==120);
 assert(!dnd_mesh_color_view(view,c));pass=1;assert(!dnd_mesh_color_view(mesh,c)&&dnd_mesh_color_update(view,c)<0&&dnd_mesh_prepare_passes(mesh)<0);pass=0;
 unsigned old=c->length;c->length=2;assert(dnd_mesh_color_update(view,c)<0);c->length=old;
 pass=1;assert(dnd_mesh_draw_material(mesh,0,0,w,0xffffffff,0,0,0)==0);const DndDrawVertex *base=seen;
 assert(dnd_mesh_draw_material(view,0,0,w,0xffffffff,0,0,0)==0&&seen==base&&seen_color==0xffffffff);pass=0;
 ((float*)v->data)[0]=42;assert(dnd_mesh_update(mesh,v,0,3,0)==0);
 const unsigned char rgb[]={255,0,0,255,0,255,0,255,0,0,255,255};memcpy(c->data,rgb,12);assert(dnd_mesh_color_update(view,c)==0);
 assert(dnd_draw_release(mesh)==0&&dnd_draw_live()==2);pass=1;assert(dnd_mesh_draw_material(view,0,0,w,0xffffffff,0,0,0)==0&&seen==base&&seen[0].x==42);assert(dnd_mesh_draw_material(view,0,0,w,0xffffffff,32,0,0)==0);pass=0;
 assert(dnd_draw_release(view)==0&&dnd_draw_total_bytes()==0&&dnd_draw_live()==0);
 mesh=dnd_mesh_create(v,NULL,0,1);assert(dnd_mesh_prepare_passes(mesh)==0&&dnd_draw_bytes(mesh)==140);assert(dnd_mesh_prepare_passes(mesh)==0&&dnd_draw_bytes(mesh)==140);
 int tex=dnd_texture_create(4,4,rgba,0);assert(tex);memset(c->data,255,12);((float*)uv->data)[0]=((float*)uv->data)[3]=((float*)uv->data)[6]=((float*)uv->data)[9]=1;
 pass=1;int before=draws,t=triangles;((float*)uv->data)[11]=NAN;
 assert(dnd_mesh_draw_passes(mesh,w,c,uv,2,tex,0,0xffffffff,0,0,0)<0&&draws==before&&triangles==t);((float*)uv->data)[11]=0;
 assert(dnd_mesh_draw_passes(mesh,w,c,uv,2,tex,0,0xffffffff,32,0,0)<0&&draws==before);
 assert(dnd_mesh_draw_passes(mesh,w,c,uv,2,tex,0,0xffffffff,0,0,0)==0&&draws==before+2&&triangles==t+2);pass=0;
 assert(dnd_draw_release(mesh)==0&&dnd_draw_release(tex)==0&&dnd_draw_total_bytes()==0);
 free(v);free(c);free(w);free(rgba);free(uv);puts("Shared mesh colors and ordered passes: geometry identity/update/lifetime, accounting, command bytes, padding and atomic preflight passed.");
}
