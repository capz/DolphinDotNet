/* Native acceptance of the shipped display switch and libogc overlay.
 * Completion is observed in guest RAM through Dolphin's GDB stub. */
#include <gccore.h>
#include <malloc.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>

volatile u32 dnd_video_test_result, dnd_video_test_phase, dnd_video_test_frames;
static GXRModeObj *mode;
static void *xfb[2], *fifo;
static unsigned fb;
static bool first_frame=true;
static void dnd_graphics_init(void) {}
static bool dnd_exception_pending(void) { return false; }
static void dnd_draw_backend_viewport(int x,int y,int w,int h) { GX_SetViewport(x,y,w,h,0,1); }
static void dnd_draw_backend_scissor(int x,int y,int w,int h) { GX_SetScissor(x,y,w,h); }
static void dnd_draw_backend_invalidate_cache(void) {}
static void dnd_performance_clear_invalidated(void) {}
#include "dnd_display_gx.inc"

__attribute__((noinline)) void dnd_video_test_complete(void) { __asm__ volatile("":::"memory"); }

static void present(void) {
    GX_SetDispCopySrc(0,0,mode->fbWidth,mode->efbHeight);
    GX_SetDispCopyYScale(GX_GetYScaleFactor(mode->efbHeight,mode->xfbHeight));
    GX_SetDispCopyDst(mode->fbWidth,mode->xfbHeight);
    GX_SetCopyFilter(mode->aa,mode->sample_pattern,GX_TRUE,mode->vfilter);
    GX_SetPixelFmt(GX_PF_RGB8_Z24,GX_ZC_LINEAR);
    GX_SetCopyClear((GXColor){0,64,128,255},0xffffff);
    GX_SetZMode(GX_TRUE,GX_LEQUAL,GX_TRUE);GX_SetColorUpdate(GX_TRUE);
    GX_CopyDisp(xfb[fb],GX_TRUE);GX_DrawDone();
    VIDEO_SetNextFramebuffer(xfb[fb]);
    if(first_frame) { first_frame=false;VIDEO_SetBlack(FALSE); }
    VIDEO_Flush();VIDEO_WaitVSync();fb^=1;dnd_video_test_frames++;
}

int main(void) {
    VIDEO_Init();mode=&TVNtsc480Prog;
    void *a=SYS_AllocateFramebuffer(mode),*b=SYS_AllocateFramebuffer(mode);
    fifo=memalign(32,256*1024);
    if(!a||!b||!fifo) { dnd_video_test_result=2;dnd_video_test_complete();return 1; }
    xfb[0]=MEM_K0_TO_K1(a);xfb[1]=MEM_K0_TO_K1(b);
    memset(fifo,0,256*1024);GX_Init(fifo,256*1024);
    VIDEO_Configure(mode);VIDEO_SetNextFramebuffer(xfb[0]);VIDEO_SetBlack(TRUE);
    VIDEO_Flush();VIDEO_WaitForFlush();present();present();
    const int changes[][2]={{3,2},{2,2},{3,2},{3,1},{3,2},{2,1},{2,2}};
    for(unsigned i=0;i<sizeof changes/sizeof changes[0];i++) {
        dnd_video_test_phase=i+1;
        if(dnd_graphics_configure_display(changes[i][0],changes[i][1])<0) {
            dnd_video_test_result=3;dnd_video_test_complete();return 1;
        }
        /* A slow first render must not destroy VI timing while output is black.
         * Stock libogc's 576p black timing loses interrupts during this window. */
        VIDEO_WaitForFlush();VIDEO_WaitVSync();present();present();
    }
    dnd_video_test_result=1;dnd_video_test_complete();
    for(;;) VIDEO_WaitVSync();
}
