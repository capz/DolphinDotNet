#include "dnd_input.h"
#include <gccore.h>
static DndGamePad pads[4];
void dnd_input_init(void) { PAD_Init(); }
void dnd_input_poll(void) {
    PAD_ScanPads();
    for (unsigned i=0;i<4;i++) {
        pads[i].held=PAD_ButtonsHeld(i); pads[i].down=PAD_ButtonsDown(i); pads[i].up=PAD_ButtonsUp(i);
        pads[i].stick_x=PAD_StickX(i); pads[i].stick_y=PAD_StickY(i);
        pads[i].cstick_x=PAD_SubStickX(i); pads[i].cstick_y=PAD_SubStickY(i);
        pads[i].trigger_l=PAD_TriggerL(i); pads[i].trigger_r=PAD_TriggerR(i);
    }
}
const DndGamePad *dnd_input_gamepad(unsigned channel) { return channel < 4 ? &pads[channel] : 0; }
