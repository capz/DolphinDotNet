/* Optional native GX-copy qualification. Run with Dolphin XFB-to-RAM copies;
 * the red zone safely exposes a too-small legacy allocation without corrupting
 * the heap. The production helper itself is included without modification. */
#include <gccore.h>
#include <malloc.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include "dnd_framebuffer_gx.inc"

#define CASES 12
#define GUARD_BYTES (16 * 640 * 2)
#define SENTINEL 0xcd

typedef struct {
    u32 width,efb_rows,vi_rows,predicted_rows,storage_rows;
    u32 claimed_bytes,last_written_byte,red_zone_changed,legacy;
} CanaryRow;
volatile CanaryRow dnd_framebuffer_canary_rows[CASES];
volatile u32 dnd_framebuffer_canary_stage;
volatile u32 dnd_framebuffer_canary_complete;
static u32 allocation_bytes,allocation_rows;

/* --wrap records the helper's requested size and adds a separate red zone.
 * Width padding and row sizes remain the real pinned libogc calculation. */
void *__wrap_SYS_AllocateFramebuffer(const GXRModeObj *mode) {
    allocation_bytes=VIDEO_GetFrameBufferSize(mode);
    allocation_rows=mode->xfbHeight;
    u8 *memory=memalign(32,allocation_bytes+GUARD_BYTES);
    if(memory){memset(memory,SENTINEL,allocation_bytes+GUARD_BYTES);DCFlushRange(memory,allocation_bytes+GUARD_BYTES);}
    return memory;
}

__attribute__((noinline)) void dnd_framebuffer_canary_done(void) {
    dnd_framebuffer_canary_complete=0x41414346;
    for(;;)__asm__ volatile("nop");
}

static void measure(u32 index,GXRModeObj mode,int legacy,void *warmup) {
    u32 predicted=GX_GetNumXfbLines(mode.efbHeight,GX_GetYScaleFactor(mode.efbHeight,mode.xfbHeight));
    void *target=legacy?__wrap_SYS_AllocateFramebuffer(&mode):allocate_display_framebuffer(&mode);
    u32 claimed=allocation_bytes,storage=allocation_rows;
    if(!target){dnd_framebuffer_canary_stage=0xe0000000|index;dnd_framebuffer_canary_done();}
    GX_SetPixelFmt(mode.aa?GX_PF_RGB565_Z16:GX_PF_RGB8_Z24,GX_ZC_LINEAR);
    GX_SetCopyFilter(mode.aa,mode.sample_pattern,GX_TRUE,mode.vfilter);
    GX_SetDispCopySrc(0,0,mode.fbWidth,mode.efbHeight);
    GX_SetDispCopyYScale(GX_GetYScaleFactor(mode.efbHeight,mode.xfbHeight));
    GX_SetDispCopyDst(mode.fbWidth,mode.xfbHeight);
    GX_SetFieldMode(mode.field_rendering,mode.viHeight==2*mode.xfbHeight?GX_ENABLE:GX_DISABLE);
    GX_SetCopyClear((GXColor){0,0,0,255},0x00ffffff);
    GX_SetZMode(GX_TRUE,GX_LEQUAL,GX_TRUE);GX_SetColorUpdate(GX_TRUE);GX_SetAlphaUpdate(GX_TRUE);
    /* Clear the EFB before measuring, making every written black YUYV byte
     * different from SENTINEL. Both buffers have space for the full write. */
    GX_CopyDisp(MEM_K0_TO_K1(warmup),GX_TRUE);GX_DrawDone();
    GX_CopyDisp(MEM_K0_TO_K1(target),GX_TRUE);GX_DrawDone();
    volatile u8 *read=(volatile u8*)MEM_K0_TO_K1(target);
    u32 last=0,changed=0;
    for(u32 at=0;at<claimed+GUARD_BYTES;at++)if(read[at]!=SENTINEL){last=at+1;if(at>=claimed)changed++;}
    dnd_framebuffer_canary_rows[index]=(CanaryRow){mode.fbWidth,mode.efbHeight,mode.xfbHeight,predicted,storage,claimed,last,changed,legacy};
    free(target);
}

int main(void) {
    VIDEO_Init();void *fifo=memalign(32,256*1024);memset(fifo,0,256*1024);GX_Init(fifo,256*1024);
    GXRModeObj *modes[]={&TVNtsc240Ds,&TVNtsc480IntDf,&TVNtsc480Prog,&TVNtsc480IntAa,&TVNtsc480IntAa,&TVNtsc480ProgAa,&TVNtsc480ProgAa,&TVPal576IntDfScale,&TVPal576ProgScale,&TVNtsc480ProgAa,&TVNtsc240Ds,&TVNtsc480IntDf};
    for(u32 i=0;i<CASES;i++){
        dnd_framebuffer_canary_stage=i+1;GXRModeObj mode=*modes[i];
        if(i==0||i==10)mode.fbWidth=320;
        if(i==10){mode.aa=GX_TRUE;memcpy(mode.sample_pattern,TVNtsc480ProgAa.sample_pattern,sizeof mode.sample_pattern);}
        void *warmup=allocate_display_framebuffer(&mode);
        if(!warmup){dnd_framebuffer_canary_stage=0xe1000000|i;dnd_framebuffer_canary_done();}
        VIDEO_Configure(&mode);VIDEO_SetNextFramebuffer(MEM_K0_TO_K1(warmup));VIDEO_SetBlack(GX_TRUE);VIDEO_Flush();VIDEO_WaitVSync();
        measure(i,mode,i==3||i==5,warmup);
        GX_DrawDone();free(warmup);
    }
    dnd_framebuffer_canary_done();return 0;
}
