#ifndef DND_FRAMEBUFFER_GX_INCLUDED
#define DND_FRAMEBUFFER_GX_INCLUDED
/* GX quantizes the vertical copy scale. Some libogc versions copy 483
 * rows for the 242-line EFB in a 480-line AA mode. Size storage for that
 * extent without changing the VI mode, stride or allocation alignment. */
static void *allocate_display_framebuffer(const GXRModeObj *display) {
    GXRModeObj storage=*display;
    int copied=GX_GetNumXfbLines(display->efbHeight,
        GX_GetYScaleFactor(display->efbHeight,display->xfbHeight));
    if(copied>storage.xfbHeight)storage.xfbHeight=copied;
    return SYS_AllocateFramebuffer(&storage);
}
#endif
