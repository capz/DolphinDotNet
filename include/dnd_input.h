#ifndef DND_INPUT_H
#define DND_INPUT_H
#include <stdint.h>
enum { DND_PAD_BUTTON_START = 0x1000 };
typedef struct { uint16_t held, down, up; int8_t stick_x, stick_y, cstick_x, cstick_y; uint8_t trigger_l, trigger_r; } DndGamePad;
void dnd_input_init(void);
void dnd_input_poll(void);
const DndGamePad *dnd_input_gamepad(unsigned channel);
#endif
