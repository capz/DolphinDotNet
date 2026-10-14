#include "dnd_input.h"
#include <math.h>
#include <string.h>

typedef struct {
    float stick_deadzone, cstick_deadzone, stick_maximum, cstick_maximum;
    float trigger_minimum, trigger_maximum;
} Options;
static DndGamePad pads[4];
static Options options[4];
static bool initialized;

void dnd_input_init(void) {
    if (initialized) return;
    memset(pads, 0, sizeof(pads));
    for (unsigned i=0; i<4; i++) options[i]=(Options){0.15f,0.15f,80,65,30,180};
    dnd_input_backend_init();
    initialized=true;
}
static void stick(float *out, int x, int y, float maximum, float deadzone) {
    float nx=x/maximum, ny=y/maximum, length=sqrtf(nx*nx+ny*ny);
    out[0]=out[1]=0;
    if (length<=deadzone) return;
    float magnitude=((length>1 ? 1 : length)-deadzone)/(1-deadzone);
    out[0]=nx*magnitude/length;
    out[1]=ny*magnitude/length;
}
static float trigger(int value, const Options *o) {
    if (value<=o->trigger_minimum) return 0;
    if (value>=o->trigger_maximum) return 1;
    return (value-o->trigger_minimum)/(o->trigger_maximum-o->trigger_minimum);
}
void dnd_input_poll(void) {
    dnd_input_init();
    DndInputSample samples[4]={0};
    dnd_input_backend_sample(samples);
    for (unsigned i=0; i<4; i++) {
        const DndInputSample *raw=&samples[i];
        DndGamePad *pad=&pads[i];
        bool previous=(pad->flags&DND_INPUT_CONNECTED)!=0;
        uint16_t held=pad->held;
        memset(pad, 0, sizeof(*pad));
        if (!raw->connected) {
            if (previous) { pad->flags=DND_INPUT_DISCONNECTED_NOW; dnd_input_backend_stop_rumble((int)i); }
            continue;
        }
        pad->flags=DND_INPUT_CONNECTED | (previous ? 0 : DND_INPUT_CONNECTED_NOW)
            | (raw->supports_rumble ? DND_INPUT_RUMBLE : 0);
        pad->held=raw->held & 0x1f7f;
        /* Connect establishes a baseline, not a synthetic button press. */
        if (previous) { pad->down=pad->held & (uint16_t)~held; pad->up=held & (uint16_t)~pad->held; }
        stick(pad->axes, raw->stick_x, raw->stick_y, options[i].stick_maximum, options[i].stick_deadzone);
        stick(pad->axes+2, raw->cstick_x, raw->cstick_y, options[i].cstick_maximum, options[i].cstick_deadzone);
        pad->axes[4]=trigger(raw->trigger_l, &options[i]);
        pad->axes[5]=trigger(raw->trigger_r, &options[i]);
    }
}
const DndGamePad *dnd_input_gamepad(unsigned port) { return port<4 ? &pads[port] : NULL; }
int dnd_input_flags(int port) { return port>=0 && port<4 ? pads[port].flags : 0; }
int dnd_input_buttons(int port, int kind) {
    if (port<0 || port>=4) return 0;
    return kind==0 ? pads[port].held : kind==1 ? pads[port].down : kind==2 ? pads[port].up : 0;
}
float dnd_input_axis(int port, int axis) { return port>=0 && port<4 && axis>=0 && axis<6 ? pads[port].axes[axis] : 0; }
void dnd_input_configure(int port, float sd, float cd, float sm, float cm, float tm, float tx) {
    if (port<0 || port>=4 || !(sd>=0 && sd<1 && cd>=0 && cd<1 && sm>0 && sm<=127 && cm>0 && cm<=127
        && tm>=0 && tm<tx && tx<=255)) return;
    dnd_input_init();
    options[port]=(Options){sd,cd,sm,cm,tm,tx};
}
bool dnd_input_rumble(int port, float seconds) {
    if (port<0 || port>=4 || !(seconds>0 && seconds<=60)
        || (pads[port].flags&(DND_INPUT_CONNECTED|DND_INPUT_RUMBLE))!=(DND_INPUT_CONNECTED|DND_INPUT_RUMBLE)) return false;
    uint32_t duration=(uint32_t)(seconds*1000000);
    return dnd_input_backend_rumble(port, duration ? duration : 1);
}
void dnd_input_stop_rumble(int port) { if (initialized && port>=0 && port<4) dnd_input_backend_stop_rumble(port); }
void dnd_input_shutdown(void) {
    if (!initialized) return;
    for (int i=0; i<4; i++) dnd_input_backend_stop_rumble(i);
    dnd_input_backend_shutdown();
    memset(pads, 0, sizeof(pads));
    initialized=false;
}
