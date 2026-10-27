#include "dnd_draw.h"
#include "dnd_frame_clear.h"
#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
static bool active;static int raw,waits,flushes;
bool dnd_device_active(void){return active;}
int dnd_raw_depth(void){return raw;}
void dnd_draw_backend_wait(void){waits++;}
void dnd_draw_backend_flush(void *p,int n,int invalidate){assert(p&&n>0&&invalidate==2);flushes++;}
void dnd_exception_throw(DndExceptionKind k,const char *m){(void)k;(void)m;abort();}
static DndArray *array(unsigned n){DndArray *a=calloc(1,sizeof(DndArray)+n);assert(a);a->length=n;a->element_size=1;return a;}
static void check_texture(int w,int h){
 int n=w*h*4;DndArray *a=array(n),*b=array(n+7);
 for(int i=0;i<n;i++){a->data[i]=(unsigned char)(i*37);b->data[i+7]=(unsigned char)(i*71+19);}
 int before=waits;int t=dnd_texture_create(w,h,a,0);assert(t);int bytes=dnd_draw_total_bytes(),live=dnd_draw_live();
 assert(dnd_texture_replace_rgba(t,b,7)==0);assert(waits==before+1);assert(dnd_draw_total_bytes()==bytes&&dnd_draw_live()==live);
 DndDrawTexture out;assert(dnd_texture_resolve(t,&out)==0);
 /* Independent GX RGBA8 tile oracle, including zero-filled edge padding. */
 for(int by=0;by<(h+3)/4;by++)for(int bx=0;bx<(w+3)/4;bx++)
  for(int y=0;y<4;y++)for(int x=0;x<4;x++){
   int block=(by*((w+3)/4)+bx)*64,at=(y*4+x)*2,px=bx*4+x,py=by*4+y;
   const unsigned char *src=px<w&&py<h?b->data+7+(py*w+px)*4:NULL;
   const unsigned char *dst=out.data;
   assert(dst[block+at]==(src?src[3]:0)&&dst[block+at+1]==(src?src[0]:0));
   assert(dst[block+32+at]==(src?src[1]:0)&&dst[block+33+at]==(src?src[2]:0));
  }unsigned char *copy=malloc(out.bytes);assert(copy);memcpy(copy,out.data,out.bytes);
 memcpy(a->data,b->data+7,n);int expected=dnd_texture_create_range(w,h,b,7,0);assert(expected);
 assert(!dnd_texture_create_range(w,h,b,8,0));assert(!dnd_texture_create_range(w,h,b,-1,0));
 assert(dnd_texture_create(w,h,b,0)==0);
 DndDrawTexture reference;assert(dnd_texture_resolve(expected,&reference)==0);assert(out.bytes==reference.bytes&&!memcmp(copy,reference.data,out.bytes));
 active=true;assert(dnd_texture_replace_rgba(t,b,7)==-1);active=false;
 raw=1;assert(dnd_texture_replace_rgba(t,b,7)==-1);raw=0;
 assert(dnd_texture_replace_rgba(t,b,8)==-1);assert(dnd_texture_replace_rgba(t,b,-1)==-1);
 b->elements_are_references=1;assert(dnd_texture_replace_rgba(t,b,7)==-1);b->elements_are_references=0;
 b->element_size=4;assert(dnd_texture_replace_rgba(t,b,7)==-1);b->element_size=1;
 assert(!memcmp(copy,out.data,out.bytes));assert(dnd_draw_release(t)==0);assert(dnd_texture_replace_rgba(t,b,7)==-1);assert(dnd_draw_release(expected)==0);
 free(copy);free(a);free(b);
}
static void check_clear(void){
 DndFrameClear s={0};assert(dnd_frame_clear_begin(&s,true,123));
 dnd_frame_clear_end(&s);assert(!dnd_frame_clear_begin(&s,true,123));
 dnd_frame_clear_end(&s);assert(dnd_frame_clear_begin(&s,true,124));
 dnd_frame_clear_end(&s);assert(dnd_frame_clear_begin(&s,false,124));
 dnd_frame_clear_end(&s);dnd_frame_clear_invalidate(&s);dnd_frame_clear_end(&s);
 assert(dnd_frame_clear_begin(&s,true,124));
 assert(dnd_frame_clear_begin(&s,true,124)); /* aborted frame cannot supply proof */
 dnd_frame_clear_invalidate(&s);dnd_frame_clear_end(&s);assert(dnd_frame_clear_begin(&s,true,124));
}
int main(void){check_texture(1,1);check_texture(3,9);check_texture(5,7);check_texture(32,32);check_texture(128,128);assert(flushes==15);assert(dnd_draw_total_bytes()==0);check_clear();puts("Texture replacement: exact tiled pixels/padding, stable allocation, fence, bounds, ownership; frame clear proof invalidation passed.");}
