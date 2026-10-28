#include "dnd_gx_raw.h"
#include "dnd_async.h"
#if !defined(DND_GAMECUBE_STORAGE)
__attribute__((weak)) void dnd_draw_backend_invalidate_efb(void){}
#endif
void dnd_raw_command(int operation,const uint64_t *args,int count) {
    (void)args;(void)count;dnd_async_require_application_thread();
    if(dnd_device_active() && dnd_device_pass()!=3) {dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Raw GX requires an exclusive raw pass.");return;}
    dnd_draw_backend_invalidate_efb();
    if(dnd_device_active()){switch(operation){case DND_RAW_GX_INITFIFOBASE: case DND_RAW_GX_SETCPUFIFO: case DND_RAW_GX_SETDRAWDONECALLBACK: case DND_RAW_GX_SETDRAWSYNCCALLBACK: case DND_RAW_GX_SETGPFIFO: case DND_RAW_VIDEO_CONFIGURE: case DND_RAW_VIDEO_INIT: case DND_RAW_VIDEO_SETBLACK: case DND_RAW_VIDEO_SETNEXTFRAMEBUFFER: case DND_RAW_VIDEO_SETPOSTRETRACECALLBACK: case DND_RAW_VIDEO_SETPRERETRACECALLBACK: dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Raw command would replace managed device ownership.");return;default:break;}}
    if(dnd_raw_depth()&1) {
        switch(operation) {
            case DND_RAW_GX_COLOR1U16: break;
            case DND_RAW_GX_COLOR1U32: break;
            case DND_RAW_GX_COLOR1X16: break;
            case DND_RAW_GX_COLOR1X8: break;
            case DND_RAW_GX_COLOR3F32: break;
            case DND_RAW_GX_COLOR3U8: break;
            case DND_RAW_GX_COLOR4U8: break;
            case DND_RAW_GX_ENDDISPLIST:if(dnd_raw_depth()!=2)dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"No matching display list.");break;
        case DND_RAW_GX_RESTOREWRITEGATHERPIPE:if(dnd_raw_depth()!=4)dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"No matching write-gather redirection.");break;
        case DND_RAW_GX_END: break;
            case DND_RAW_GX_MATRIXINDEX1X8: break;
            case DND_RAW_GX_NORMAL1X16: break;
            case DND_RAW_GX_NORMAL1X8: break;
            case DND_RAW_GX_NORMAL3F32: break;
            case DND_RAW_GX_NORMAL3S16: break;
            case DND_RAW_GX_NORMAL3S8: break;
            case DND_RAW_GX_POSITION1X16: break;
            case DND_RAW_GX_POSITION1X8: break;
            case DND_RAW_GX_POSITION2F32: break;
            case DND_RAW_GX_POSITION2S16: break;
            case DND_RAW_GX_POSITION2S8: break;
            case DND_RAW_GX_POSITION2U16: break;
            case DND_RAW_GX_POSITION2U8: break;
            case DND_RAW_GX_POSITION3F32: break;
            case DND_RAW_GX_POSITION3S16: break;
            case DND_RAW_GX_POSITION3S8: break;
            case DND_RAW_GX_POSITION3U16: break;
            case DND_RAW_GX_POSITION3U8: break;
            case DND_RAW_GX_TEXCOORD1F32: break;
            case DND_RAW_GX_TEXCOORD1S16: break;
            case DND_RAW_GX_TEXCOORD1S8: break;
            case DND_RAW_GX_TEXCOORD1U16: break;
            case DND_RAW_GX_TEXCOORD1U8: break;
            case DND_RAW_GX_TEXCOORD1X16: break;
            case DND_RAW_GX_TEXCOORD1X8: break;
            case DND_RAW_GX_TEXCOORD2F32: break;
            case DND_RAW_GX_TEXCOORD2S16: break;
            case DND_RAW_GX_TEXCOORD2S8: break;
            case DND_RAW_GX_TEXCOORD2U16: break;
            case DND_RAW_GX_TEXCOORD2U8: break;
            default:dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Only vertex attributes are allowed inside GX_Begin/GX_End.");return;
        }
    }
    switch(operation) {
        case DND_RAW_GX_ENDDISPLIST:if(dnd_raw_depth()!=2)dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"No matching display list.");break;
        case DND_RAW_GX_RESTOREWRITEGATHERPIPE:if(dnd_raw_depth()!=4)dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"No matching write-gather redirection.");break;
        case DND_RAW_GX_END: if(!(dnd_raw_depth()&1))dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"GX_End without GX_Begin.");break;
        case DND_RAW_GX_INIT: if(dnd_device_claim_external()<0)dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"GX_Init cannot replace a managed frame.");break;
        default:break;
    }
}
#ifndef DND_GRAPHICS_TEST
uint64_t dnd_raw_record(int operation,const uint64_t *args,int count){(void)operation;(void)args;(void)count;return 0;}
#endif
