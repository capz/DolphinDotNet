#include "dnd_platform.h"
#include <gccore.h>
#include <stdio.h>
#include <stdlib.h>

static void *framebuffer;
static GXRModeObj *video_mode;

void dnd_platform_init(void) {
    VIDEO_Init();
    PAD_Init();

    video_mode = VIDEO_GetPreferredMode(NULL);
    framebuffer = MEM_K0_TO_K1(SYS_AllocateFramebuffer(video_mode));
    console_init(framebuffer, 20, 20,
                 video_mode->fbWidth, video_mode->xfbHeight,
                 video_mode->fbWidth * VI_DISPLAY_PIX_SZ);

    VIDEO_Configure(video_mode);
    VIDEO_SetNextFramebuffer(framebuffer);
    VIDEO_SetBlack(FALSE);
    VIDEO_Flush();
    VIDEO_WaitVSync();
    if (video_mode->viTVMode & VI_NON_INTERLACE) VIDEO_WaitVSync();
}

void dnd_platform_write(const char *text) {
    fputs(text, stdout);
}

void dnd_platform_write_int(int32_t value) {
    printf("%ld", (long)value);
}

void dnd_platform_wait_forever(void) {
    for (;;) {
        PAD_ScanPads();
        if (PAD_ButtonsDown(0) & PAD_BUTTON_START) {
            exit(0);
        }
        VIDEO_WaitVSync();
    }
}
