#include "dnd_draw.h"
#include <assert.h>
#include <float.h>
#include <math.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
static int pass,draws,used;static DndDrawVertex emitted[48];
bool dnd_device_active(void){return pass!=0;}
int dnd_device_pass(void){return pass;}
int dnd_device_pass_begin(int p){if(pass)return -1;pass=p;return 0;}
int dnd_device_pass_end(int p){assert(pass==p);pass=0;return 0;}
int dnd_raw_depth(void){return 0;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_2d(int w,int h){assert(w==640&&h==480);}
void dnd_performance_add_triangles(uint32_t n){assert(n>0);}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *t,const DndDrawMaterial *m,const float *world){assert(n&&primitive==0x90&&!t&&m&&world);assert(used+n<=48);memcpy(emitted+used,v,n*sizeof(*v));used+=n;draws++;}
void dnd_exception_throw(DndExceptionKind k,const char *m){(void)k;(void)m;abort();}
static void reset(void){draws=used=0;memset(emitted,0,sizeof emitted);}
int main(void){
 DndArray *a=calloc(1,sizeof(DndArray)+24*4);assert(a);a->length=24;a->element_size=4;
 const float quad[]={-7,3,0,0, 16,3,0,0, 16,20,0,0, -7,3,0,0, 16,20,0,0, -7,20,0,0};memcpy(a->data,quad,sizeof quad);
 int t=dnd_canvas_create(6);assert(t);assert(dnd_canvas_rectangle(t,0,0,1,1,0)==-1);
 assert(dnd_canvas_begin(t,640,480)==0);assert(dnd_canvas_vertices(t,0,a,6,0x12345678)==0);assert(dnd_canvas_end(t)==0);
 assert(used==6&&draws==1);DndDrawVertex reference[6];memcpy(reference,emitted,sizeof reference);reset();
 assert(dnd_canvas_begin(t,640,480)==0);assert(dnd_canvas_rectangle(t,-7,3,23,17,0x12345678)==0);
 assert(dnd_canvas_rectangle(t,0,0,0,1,1)==0);assert(dnd_canvas_stats(t,2)==6);
 assert(dnd_canvas_rectangle(t,NAN,0,1,1,0)==-1);assert(dnd_canvas_rectangle(t,0,0,-1,1,0)==-1);
 assert(dnd_canvas_rectangle(t,FLT_MAX,0,FLT_MAX,1,0)==-1);assert(dnd_canvas_stats(t,2)==6);
 assert(dnd_canvas_rectangle(t,-7,3,23,17,0x12345678)==0);assert(draws==1);assert(dnd_canvas_end(t)==0);
 assert(used==12&&draws==2&&!memcmp(reference,emitted,sizeof reference)&&!memcmp(reference,emitted+6,sizeof reference));
 assert(dnd_draw_release(t)==0&&dnd_draw_total_bytes()==0);free(a);
 puts("Direct rectangles: exact legacy vertices/color, capacity flush, stable accounting, zero size, finite coordinates, overflow and pass ownership passed.");
}
