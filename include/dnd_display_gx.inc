/* Included by the GX backend and the isolated display lifecycle fixture.
 * mode/xfb/fb/first_frame are the backend's actual presentation state. */
static GXRModeObj configured_display_mode;
static int select_display_mode(int resolution,int scan,GXRModeObj *selected) {
    if(resolution<0||resolution>3||scan<0||scan>2)return -1;
    *selected=*mode;
    if(resolution==0&&scan==0)return 0; /* In particular, widescreen-only changes. */
    int current_scan=(mode->viTVMode&3)==VI_INTERLACE?1:2;
    if(resolution==0){
        if(mode->xfbHeight==240)resolution=1;
        else if(mode->xfbHeight==480)resolution=2;
        else if(mode->xfbHeight==576)resolution=3;
        else return -1;
    }
    if(resolution==1){
        if(scan==1)return -1; /* 240p double-strike is not 240i field rendering. */
        unsigned standard=mode->viTVMode>>2;
        *selected=standard==VI_PAL||standard==VI_EURGB60?TVEurgb60Hz240Ds:
                  standard==VI_MPAL?TVMpal240Ds:TVNtsc240Ds;
        selected->fbWidth=320;selected->efbHeight=selected->xfbHeight=240;
        return 0;
    }
    if(scan==0)scan=resolution==2&&mode->xfbHeight==480?current_scan:
                      resolution==3&&mode->xfbHeight==576?current_scan:
                      VIDEO_HaveComponentCable()?2:1;
    if(scan==2&&!VIDEO_HaveComponentCable())return -1;
    if(resolution==3)*selected=scan==2?TVPal576ProgScale:TVPal576IntDfScale;
    else {
        unsigned standard=mode->viTVMode>>2;
        if(standard==VI_PAL||standard==VI_EURGB60)*selected=scan==2?TVEurgb60Hz480Prog:TVEurgb60Hz480IntDf;
        else if(standard==VI_MPAL)*selected=scan==2?TVMpal480Prog:TVMpal480IntDf;
        else *selected=scan==2?TVNtsc480Prog:TVNtsc480IntDf;
    }
    return 0;
}
int dnd_graphics_configure_display(int resolution,int scan) {
    dnd_graphics_init();if(!mode||dnd_exception_pending())return -1;
    GXRModeObj next;if(select_display_mode(resolution,scan,&next)<0)return -1;
    if(!memcmp(mode,&next,sizeof next))return 0;
    /* Allocate both replacements first. An allocation failure cannot destroy
     * the current mode or scanout buffers. */
    void *a=SYS_AllocateFramebuffer(&next),*b=SYS_AllocateFramebuffer(&next);
    if(!a||!b){free(a);free(b);return -2;}
    GX_DrawDone();
    VIDEO_SetBlack(TRUE);VIDEO_Flush();VIDEO_WaitVSync();
    void *old_a=xfb[0],*old_b=xfb[1];
    configured_display_mode=next;mode=&configured_display_mode;
    xfb[0]=MEM_K0_TO_K1(a);xfb[1]=MEM_K0_TO_K1(b);fb=0;first_frame=true;
    VIDEO_Configure(mode);VIDEO_SetNextFramebuffer(xfb[0]);VIDEO_Flush();VIDEO_WaitVSync();
    free(MEM_K1_TO_K0(old_a));free(MEM_K1_TO_K0(old_b));
    GX_SetDispCopyYScale(GX_GetYScaleFactor(mode->efbHeight,mode->xfbHeight));
    GX_SetDispCopySrc(0,0,mode->fbWidth,mode->efbHeight);GX_SetDispCopyDst(mode->fbWidth,mode->xfbHeight);
    GX_SetCopyFilter(mode->aa,mode->sample_pattern,GX_TRUE,mode->vfilter);
    GX_SetFieldMode(mode->field_rendering,mode->viHeight==2*mode->xfbHeight?GX_ENABLE:GX_DISABLE);
    dnd_draw_backend_viewport(0,0,mode->fbWidth,mode->efbHeight);
    dnd_draw_backend_scissor(0,0,mode->fbWidth,mode->efbHeight);
    dnd_draw_backend_invalidate_cache();dnd_performance_clear_invalidated();
    return 0;
}
int dnd_graphics_display_stat(int kind){
    dnd_graphics_init();if(!mode)return -1;
    switch(kind){
        case 0:return mode->fbWidth==320&&mode->xfbHeight==240?1:mode->fbWidth==640&&mode->xfbHeight==480?2:mode->fbWidth==640&&mode->xfbHeight==576?3:0;
        case 1:return (mode->viTVMode&3)==VI_INTERLACE?1:2;
        case 2:return mode->fbWidth;case 3:return mode->xfbHeight;
        case 4:return mode->fbWidth;case 5:return mode->efbHeight;
        case 6:return (mode->viTVMode>>2)==VI_PAL?50000:59940;
        default:return -1;
    }
}
