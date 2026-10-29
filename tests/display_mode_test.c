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
static GXRModeObj *mode=&TVNtsc480IntDf;static void *xfb[2];static unsigned fb;static bool first_frame;
static int dtv,allocations,fail_at,live,waits,gpu_waits,configured,pending;
static void *allocate(GXRModeObj *m){assert(m);allocations++;if(allocations==fail_at)return NULL;live++;return malloc(16);}
static void release(void *p){if(p){assert(!pending);live--;free(p);}}
static void dnd_graphics_init(void){}static bool dnd_exception_pending(void){return false;}
static int VIDEO_HaveComponentCable(void){return dtv;}
static void GX_DrawDone(void){gpu_waits++;}static void VIDEO_SetBlack(int b){assert(b==TRUE);}
static void VIDEO_Flush(void){}static void VIDEO_WaitVSync(void){waits++;pending=0;}
static void VIDEO_Configure(GXRModeObj *m){assert(m==mode);configured++;}
static void VIDEO_SetNextFramebuffer(void *p){assert(p==xfb[0]);pending=1;}
static float GX_GetYScaleFactor(int a,int b){return (float)b/a;}static void GX_SetDispCopyYScale(float s){assert(s>0);}
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
 for(int r=-1;r<=4;r++)for(int s=-1;s<=3;s++){
  int result=select_display_mode(r,s,&next);
  bool invalid=r<0||r>3||s<0||s>2||(r==1&&s==1)||((r==0||r>=2)&&s==2);
  assert((result<0)==invalid);
 }
 dtv=1;assert(select_display_mode(2,2,&next)==0&&next.viTVMode==2);
 assert(select_display_mode(3,2,&next)==0&&next.xfbHeight==576&&next.efbHeight==480);
 mode=&TVPal576IntDfScale;assert(select_display_mode(2,1,&next)==0&&next.viTVMode==20);
 assert(select_display_mode(1,2,&next)==0&&next.fbWidth==320&&next.xfbHeight==240&&next.viTVMode==21);
 mode=&TVNtsc480IntDf;xfb[0]=allocate(mode);xfb[1]=allocate(mode);assert(live==2);
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
 release(xfb[0]);release(xfb[1]);assert(live==0);
 puts("Display mode combinations, DTV restrictions, standard selection, transactional allocation, scanout lifetime and widescreen-only no-op passed.");
}
