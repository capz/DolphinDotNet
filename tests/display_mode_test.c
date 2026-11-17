#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
enum{VI_INTERLACE=0,VI_PAL=1,VI_MPAL=2,VI_EURGB60=5,GX_TRUE=1,GX_FALSE=0,GX_ENABLE=1,GX_DISABLE=0,TRUE=1};
typedef struct{unsigned viTVMode;int fbWidth,efbHeight,xfbHeight,viHeight;int aa,field_rendering;unsigned char sample_pattern[12][2],vfilter[7];}GXRModeObj;
#define MODE(name,tv,w,eh,xh,vh) static GXRModeObj name={.viTVMode=tv,.fbWidth=w,.efbHeight=eh,.xfbHeight=xh,.viHeight=vh}
MODE(TVNtsc240Ds,1,640,240,240,480);MODE(TVMpal240Ds,9,640,240,240,480);MODE(TVEurgb60Hz240Ds,21,640,240,240,480);
MODE(TVNtsc480IntDf,0,640,480,480,480);MODE(TVNtsc480Prog,2,640,480,480,480);
MODE(TVMpal480IntDf,8,640,480,480,480);MODE(TVMpal480Prog,10,640,480,480,480);
MODE(TVEurgb60Hz480IntDf,20,640,480,480,480);MODE(TVEurgb60Hz480Prog,22,640,480,480,480);
MODE(TVPal576IntDfScale,4,640,480,576,576);MODE(TVPal576ProgScale,6,640,480,576,576);
MODE(TVNtsc480IntAa,0,640,242,480,480);MODE(TVNtsc480ProgAa,2,640,242,480,480);
MODE(TVMpal480IntAa,8,640,242,480,480);MODE(TVMpal480ProgAa,10,640,242,480,480);
MODE(TVEurgb60Hz480IntAa,20,640,242,480,480);MODE(TVEurgb60Hz480ProgAa,22,640,242,480,480);
static GXRModeObj *mode=&TVNtsc480IntDf;static void *xfb[2];static unsigned fb;static bool first_frame;
static int dtv,allocations,fail_at,live,waits,gpu_waits,configured,pending;
static int copy_rows_override;
typedef struct{void *data;size_t bytes;GXRModeObj storage;} Allocation;
static Allocation buffers[8];
static Allocation *allocation(void *p){for(unsigned i=0;i<sizeof buffers/sizeof buffers[0];i++)if(buffers[i].data==p)return &buffers[i];assert(!"Unknown framebuffer");return NULL;}
static void *allocate(GXRModeObj *m){
 assert(m);allocations++;if(allocations==fail_at)return NULL;
 for(unsigned i=0;i<sizeof buffers/sizeof buffers[0];i++)if(!buffers[i].data){
  size_t bytes=(size_t)((m->fbWidth+15)&~15)*m->xfbHeight*2;
  unsigned char *data=malloc(bytes+32);assert(data);memset(data,0,bytes);memset(data+bytes,0xa5,32);
  buffers[i]=(Allocation){data,bytes,*m};live++;return data;
 }
 assert(!"Too many live framebuffers");return NULL;
}
static void guard(Allocation *a){for(int i=0;i<32;i++)assert(((unsigned char *)a->data)[a->bytes+i]==0xa5);}
static void release(void *p){if(p){assert(!pending);Allocation *a=allocation(p);guard(a);a->data=NULL;live--;free(p);}}
static void copy_rows(void *p,int rows){Allocation *a=allocation(p);size_t bytes=(size_t)((a->storage.fbWidth+15)&~15)*rows*2;assert(bytes<=a->bytes);memset(p,0x3c,bytes);guard(a);}
static void dnd_graphics_init(void){}static bool dnd_exception_pending(void){return false;}
static int VIDEO_HaveComponentCable(void){return dtv;}
static void GX_DrawDone(void){gpu_waits++;}static void VIDEO_SetBlack(int b){assert(b==TRUE);}
static void VIDEO_Flush(void){}static void VIDEO_WaitVSync(void){waits++;pending=0;}
static void VIDEO_Configure(GXRModeObj *m){assert(m==mode);configured++;}
static void VIDEO_SetNextFramebuffer(void *p){assert(p==xfb[0]);pending=1;}
static float GX_GetYScaleFactor(int a,int b){return (float)b/a;}
static int GX_GetNumXfbLines(int efbHeight,float scale){
 if(copy_rows_override)return copy_rows_override;
 /* The pinned PPC libogc contract produces 483 rows from its 242-line AA
  * preset. Keep this independent of the production storage calculation. */
 return efbHeight==242?483:(int)(efbHeight*scale+.5f);
}
static void GX_SetDispCopyYScale(float s){assert(s>0);}
static void GX_SetDispCopySrc(int x,int y,int w,int h){assert(!x&&!y&&w==mode->fbWidth&&h==mode->efbHeight);}
static void GX_SetDispCopyDst(int w,int h){assert(w==mode->fbWidth&&h==mode->xfbHeight);}
static void GX_SetCopyFilter(int a,unsigned char p[12][2],int b,unsigned char f[7]){(void)a;(void)p;(void)f;assert(b==GX_TRUE);}
static void GX_SetFieldMode(int a,int b){(void)a;(void)b;}
static void dnd_draw_backend_viewport(int x,int y,int w,int h){assert(!x&&!y&&w==mode->fbWidth&&h==mode->efbHeight);}
static void dnd_draw_backend_scissor(int x,int y,int w,int h){dnd_draw_backend_viewport(x,y,w,h);}
static void dnd_draw_backend_invalidate_cache(void){}static void dnd_performance_clear_invalidated(void){}
#define SYS_AllocateFramebuffer allocate
#define MEM_K0_TO_K1(p) (p)
#define MEM_K1_TO_K0(p) (p)
#define free release
#include "dnd_display_gx.inc"
#undef free
int main(void){
 GXRModeObj next;
 /* Both startup and mode switching use this helper. Cover older libogc's
  * 483-line result and a newer reciprocal quantizer's 479-line result. */
 GXRModeObj request=TVNtsc480ProgAa;request.aa=1;request.sample_pattern[0][0]=7;
 for(int rows=479;rows<=483;rows+=4){
  copy_rows_override=rows;void *p=allocate_display_framebuffer(&request);Allocation *a=allocation(p);
  assert(request.xfbHeight==480&&request.efbHeight==242&&request.aa==1);
  assert(a->storage.xfbHeight==(rows>480?rows:480)&&a->storage.efbHeight==242&&a->storage.aa==1&&a->storage.sample_pattern[0][0]==7);
  assert(a->bytes==(size_t)640*(rows>480?rows:480)*2);copy_rows(p,rows);release(p);
 }
 request.fbWidth=641;copy_rows_override=511;
 void *p=allocate_display_framebuffer(&request);assert(allocation(p)->bytes==(size_t)656*511*2&&request.xfbHeight==480);copy_rows(p,511);release(p);
 copy_rows_override=0;
 GXRModeObj *standard[]={&TVNtsc240Ds,&TVNtsc480IntDf,&TVNtsc480Prog,&TVPal576IntDfScale,&TVPal576ProgScale};
 for(unsigned i=0;i<sizeof standard/sizeof standard[0];i++){
  p=allocate_display_framebuffer(standard[i]);assert(allocation(p)->storage.xfbHeight==standard[i]->xfbHeight);copy_rows(p,standard[i]->xfbHeight);release(p);
 }
 assert(!live);
 for(int r=-1;r<=4;r++)for(int s=-1;s<=3;s++){
  int result=select_display_mode(r,s,&next);
  bool invalid=r<0||r>3||s<0||s>2||(r==1&&s==1)||((r==0||r>=2)&&s==2);
  assert((result<0)==invalid);
 }
 dtv=1;assert(select_display_mode(2,2,&next)==0&&next.viTVMode==2);
 assert(select_display_mode(3,2,&next)==0&&next.xfbHeight==576&&next.efbHeight==480);
 mode=&TVPal576IntDfScale;assert(select_display_mode(2,1,&next)==0&&next.viTVMode==20);
 assert(select_display_mode(1,2,&next)==0&&next.fbWidth==320&&next.xfbHeight==240&&next.viTVMode==21);
 mode=&TVNtsc480IntDf;xfb[0]=allocate_display_framebuffer(mode);xfb[1]=allocate_display_framebuffer(mode);assert(live==2);
 allocations=0;assert(dnd_graphics_configure_display(0,0)==0&&allocations==0&&waits==0&&gpu_waits==0);
 for(int failure=1;failure<=2;failure++){
  allocations=0;fail_at=failure;void *a=xfb[0],*b=xfb[1];GXRModeObj *old=mode;
  assert(dnd_graphics_configure_display(3,2)==-2);assert(xfb[0]==a&&xfb[1]==b&&mode==old&&live==2&&!waits&&!gpu_waits);
 }
 fail_at=0;allocations=0;assert(dnd_graphics_configure_display(3,2)==0);
 assert(live==2&&waits==2&&gpu_waits==1&&configured==1&&fb==0&&first_frame);
 assert(dnd_graphics_display_stat(0)==3&&dnd_graphics_display_stat(1)==2&&dnd_graphics_display_stat(3)==576&&dnd_graphics_display_stat(5)==480&&dnd_graphics_display_stat(6)==50000);
 int before=allocations;assert(dnd_graphics_configure_display(0,0)==0&&allocations==before&&gpu_waits==1);
 assert(dnd_graphics_configure_display(1,1)<0&&allocations==before);
 assert(dnd_graphics_configure_display(1,2)==0&&mode->fbWidth==320&&mode->xfbHeight==240);
 TVNtsc480ProgAa.aa=TVNtsc480IntAa.aa=TVEurgb60Hz480ProgAa.aa=TVEurgb60Hz480IntAa.aa=1;TVEurgb60Hz480ProgAa.sample_pattern[0][0]=3;
 for(int failure=1;failure<=2;failure++){
  allocations=0;fail_at=failure;GXRModeObj *before_aa=mode;void *a=xfb[0],*b=xfb[1];int old_waits=waits,old_gpu_waits=gpu_waits,old_configured=configured;
  assert(dnd_graphics_configure_quality(2,2,1)==-2&&mode==before_aa&&!mode->aa&&live==2&&xfb[0]==a&&xfb[1]==b);
  assert(waits==old_waits&&gpu_waits==old_gpu_waits&&configured==old_configured);
 }
 fail_at=0;
 assert(dnd_graphics_configure_quality(2,2,1)==0&&mode->aa&&mode->efbHeight==242&&mode->sample_pattern[0][0]==3);
 assert(dnd_graphics_display_stat(8)==1&&dnd_graphics_display_stat(5)==242&&dnd_graphics_display_stat(3)==480);
 for(int i=0;i<2;i++){assert(allocation(xfb[i])->storage.xfbHeight==483&&allocation(xfb[i])->bytes==(size_t)640*483*2);copy_rows(xfb[i],483);}
 /* Do not shrink buffers below the VI extent on libogc versions whose
  * quantized copy returns fewer rows than requested. */
 assert(dnd_graphics_configure_quality(0,0,0)==0);copy_rows_override=479;
 assert(dnd_graphics_configure_quality(2,2,1)==0&&mode->xfbHeight==480&&mode->efbHeight==242);
 for(int i=0;i<2;i++){assert(allocation(xfb[i])->storage.xfbHeight>=480);copy_rows(xfb[i],479);}
 copy_rows_override=0;
 assert(dnd_graphics_configure_quality(3,2,1)<0&&mode->aa);
 assert(dnd_graphics_configure_quality(0,0,0)==0&&!mode->aa&&mode->efbHeight==480);
 assert(dnd_graphics_configure_quality(1,2,1)==0&&mode->aa&&mode->fbWidth==320&&mode->efbHeight==240);
 assert(dnd_graphics_configure_quality(0,0,0)==0&&!mode->aa&&mode->fbWidth==320);
 assert(dnd_graphics_configure_quality(2,2,0)==0);before=allocations;
 assert(dnd_graphics_configure_quality(2,2,2)==0&&mode->aa&&mode->efbHeight==248);
 assert(allocations==before);
 assert(dnd_graphics_display_stat(8)==2&&dnd_graphics_display_stat(9)==2&&dnd_graphics_display_stat(5)==480);
 for(int i=0;i<2;i++){assert(allocation(xfb[i])->bytes==(size_t)640*480*2);copy_rows(xfb[i],480);}
 before=allocations;assert(dnd_graphics_configure_quality(0,0,2)==0&&allocations==before);
 assert(dnd_graphics_configure_quality(1,2,2)<0&&dnd_graphics_display_stat(8)==2);
 assert(dnd_graphics_configure_quality(3,2,2)<0&&dnd_graphics_display_stat(8)==2);
 assert(dnd_graphics_configure_quality(0,0,0)==0&&!mode->aa&&mode->efbHeight==480);
 assert(dnd_graphics_display_stat(9)==1);
 release(xfb[0]);release(xfb[1]);assert(live==0);
 puts("Display copy bounds (483/479 rows), VI dimensions, padded stride, mode combinations, transactional allocation, scanout lifetime and widescreen-only no-op passed.");
}
