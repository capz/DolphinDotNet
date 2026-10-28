#include "dnd_draw.h"
#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
static int pass,draws,options,scissor[4];static float translation;
bool dnd_device_active(void){return pass!=0;}int dnd_device_pass(void){return pass;}
int dnd_device_pass_begin(int p){if(pass)return -1;pass=p;return 0;}int dnd_device_pass_end(int p){if(pass!=p)return -1;pass=0;return 0;}
int dnd_device_width(void){return 640;}int dnd_device_height(void){return 480;}int dnd_raw_depth(void){return 0;}
void dnd_draw_backend_wait(void){}void dnd_draw_backend_flush(void *p,int n,int i){(void)p;(void)n;(void)i;}
void dnd_draw_backend_2d(int w,int h){assert(w==640&&h==480);}
void dnd_draw_backend_viewport(int x,int y,int w,int h){assert(x>=0&&y>=0&&w>0&&h>0);}
void dnd_draw_backend_scissor(int x,int y,int w,int h){assert(w>0&&h>0);scissor[0]=x;scissor[1]=y;scissor[2]=w;scissor[3]=h;}
void dnd_draw_backend_options(const float *p,int scene){(void)p;(void)scene;options++;}
void dnd_performance_add_triangles(uint32_t n){assert(n>0);}
void dnd_exception_throw(DndExceptionKind k,const char *m){(void)k;(void)m;abort();}
bool dnd_draw_backend_mesh(const DndDrawVertex *v,const uint16_t *i,int n,uint32_t c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){(void)v;(void)i;(void)c;(void)t;(void)m;assert(n==3);draws++;translation=w[12];return true;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){(void)v;(void)m;(void)w;assert(n==6&&primitive==0x90&&t);draws++;}
static DndArray *array(unsigned n,unsigned size){DndArray *a=calloc(1,sizeof(*a)+n*size);assert(a);a->length=n;a->element_size=size;return a;}
int main(void){
 DndArray *v=array(3,16),*ids=array(2,4),*matrices=array(2,sizeof(DndArray*)),*m0=array(16,4),*m1=array(16,4),*rgba=array(64,1),*vertices=array(48,4),*colors=array(2,4),*style=array(20,4);
 for(int i=0;i<3;i++){uint32_t white=0xffffffff;memcpy(v->data+i*16+12,&white,4);}
 int mesh=dnd_mesh_create(v,NULL,0,0),tex=dnd_texture_create(4,4,rgba,0);assert(mesh&&tex);int *tokens=(int*)ids->data;tokens[0]=tokens[1]=mesh;
 matrices->elements_are_references=true;DndArray **rows=(DndArray**)matrices->data;rows[0]=m0;rows[1]=m1;for(int i=0;i<2;i++)for(int j=0;j<16;j++)((float*)rows[i]->data)[j]=j%5==0?1:0;float *w=(float*)m1->data;w[12]=2;
 pass=1;tokens[1]=-1;assert(dnd_mesh_draw_many(ids,matrices,2,0,0,0xffffffff,0,0,0)<0&&draws==0);tokens[1]=mesh;
 w[15]=NAN;assert(dnd_mesh_draw_many(ids,matrices,2,0,0,0xffffffff,0,0,0)<0&&draws==0);w[15]=1;
 assert(dnd_mesh_draw_many(ids,matrices,2,0,0,0xffffffff,0,0,0)==0&&draws==2&&translation==2);
 ((float*)style->data)[0]=3;assert(dnd_render_material(style)<0&&options==0);((float*)style->data)[0]=2;
 assert(dnd_render_material(style)==0&&options==1);pass=0;assert(dnd_render_material(style)<0&&options==1);
 int canvas=dnd_canvas_create(6);assert(canvas);assert(dnd_canvas_begin(canvas,640,480)==0);draws=0;
 assert(dnd_canvas_viewport(canvas,100,50,320,240)==0);assert(dnd_canvas_clip(canvas,20,40,100,80)==0);assert(scissor[0]==110&&scissor[1]==70&&scissor[2]==50&&scissor[3]==40);
 tokens[0]=tokens[1]=tex;
 assert(dnd_canvas_clip(canvas,0,0,1,2)==0);
 assert(dnd_canvas_sprites(canvas,ids,vertices,colors,2)==0&&dnd_canvas_stats(canvas,2)==0);
 assert(dnd_canvas_clip(canvas,0,0,2,1)==0);
 assert(dnd_canvas_sprites(canvas,ids,vertices,colors,2)==0&&dnd_canvas_stats(canvas,2)==0);
 assert(dnd_canvas_clip(canvas,20,40,100,80)==0);
 tokens[0]=tex;tokens[1]=-1;assert(dnd_canvas_sprites(canvas,ids,vertices,colors,2)<0&&draws==0&&dnd_canvas_stats(canvas,2)==0);tokens[1]=tex;
 ((float*)vertices->data)[47]=NAN;assert(dnd_canvas_sprites(canvas,ids,vertices,colors,2)<0&&draws==0);((float*)vertices->data)[47]=0;
 assert(dnd_canvas_clip(canvas,0,0,0,1)==0);assert(dnd_canvas_sprites(canvas,ids,vertices,colors,2)==0&&dnd_canvas_stats(canvas,2)==0);
 assert(dnd_canvas_clip(canvas,0,0,640,480)==0);assert(dnd_canvas_sprites(canvas,ids,vertices,colors,2)==0&&draws==1&&dnd_canvas_stats(canvas,2)==6);
 assert(dnd_draw_release(tex)==0);assert(dnd_canvas_sprites(canvas,ids,vertices,colors,1)<0);assert(dnd_canvas_end(canvas)==0&&draws==2);dnd_draw_collect();
 assert(dnd_draw_release(mesh)==0&&dnd_draw_release(canvas)==0&&dnd_draw_total_bytes()==0);
 free(v);free(ids);free(matrices);free(m0);free(m1);free(rgba);free(vertices);free(colors);free(style);
 puts("Bulk indexed submission and sprites: whole-block preflight, viewport mapping, empty clips, capacity flush and deferred texture lifetime passed.");
}
