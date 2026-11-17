#ifndef DND_FRAMEBUFFER_GX_INCLUDED
#define DND_FRAMEBUFFER_GX_INCLUDED
/* GX quantizes the vertical copy scale. Some libogc versions copy 483
 * rows for the 242-line EFB in a 480-line AA mode. Size storage for that
 * extent without changing the VI mode, stride or allocation alignment. */
static int display_framebuffer_rows(const GXRModeObj *display) {
    int copied=GX_GetNumXfbLines(display->efbHeight,
        GX_GetYScaleFactor(display->efbHeight,display->xfbHeight));
    return copied>display->xfbHeight?copied:display->xfbHeight;
}
static void *allocate_display_framebuffer(const GXRModeObj *display) {
    GXRModeObj storage=*display;
    storage.xfbHeight=display_framebuffer_rows(display);
    return SYS_AllocateFramebuffer(&storage);
}
#endif
