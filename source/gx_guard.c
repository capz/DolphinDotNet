#include "dnd_gx_raw.h"
#include "dnd_async.h"
void dnd_raw_command(int operation,const uint64_t *args,int count) {
    (void)args;(void)count;dnd_async_require_application_thread();
    if(dnd_device_active() && dnd_device_pass()!=3) {dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Raw GX requires an exclusive raw pass.");return;}
    if(dnd_device_active()){switch(operation){case 68: case 169: case 189: case 191: case 198: case 268: case 287: case 290: case 291: case 293: case 294: dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Raw command would replace managed device ownership.");return;default:break;}}
    if(dnd_raw_depth()&1) {
        switch(operation) {
            case 10: break;
            case 11: break;
            case 12: break;
            case 13: break;
            case 14: break;
            case 15: break;
            case 16: break;
            case 23: break;
            case 123: break;
            case 124: break;
            case 125: break;
            case 126: break;
            case 127: break;
            case 128: break;
            case 142: break;
            case 143: break;
            case 144: break;
            case 145: break;
            case 146: break;
            case 147: break;
            case 148: break;
            case 149: break;
            case 150: break;
            case 151: break;
            case 152: break;
            case 153: break;
            case 253: break;
            case 254: break;
            case 255: break;
            case 256: break;
            case 257: break;
            case 258: break;
            case 259: break;
            case 260: break;
            case 261: break;
            case 262: break;
            case 263: break;
            case 264: break;
            default:dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Only vertex attributes are allowed inside GX_Begin/GX_End.");return;
        }
    }
    switch(operation) {
        case 23: if(!(dnd_raw_depth()&1))dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"GX_End without GX_Begin.");break;
        case 67: if(dnd_device_claim_external()<0)dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"GX_Init cannot replace a managed frame.");break;
        default:break;
    }
}
#ifndef DND_GRAPHICS_TEST
uint64_t dnd_raw_record(int operation,const uint64_t *args,int count){(void)operation;(void)args;(void)count;return 0;}
#endif
