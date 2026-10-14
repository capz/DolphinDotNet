#ifndef DND_INPUT_H
#define DND_INPUT_H
#include <stdbool.h>
#include <stdint.h>
enum { DND_PAD_BUTTON_START = 0x1000 };
enum { DND_INPUT_CONNECTED=1, DND_INPUT_CONNECTED_NOW=2,
       DND_INPUT_DISCONNECTED_NOW=4, DND_INPUT_RUMBLE=8 };
typedef struct {
    bool connected, supports_rumble;
    uint16_t held;
    int8_t stick_x, stick_y, cstick_x, cstick_y;
    uint8_t trigger_l, trigger_r;
} DndInputSample;
typedef struct {
    uint16_t held, down, up;
    int flags;
    float axes[6]; /* stick XY, C-stick XY, left/right trigger */
} DndGamePad;
void dnd_input_init(void);
void dnd_input_poll(void);
void dnd_input_shutdown(void);
const DndGamePad *dnd_input_gamepad(unsigned channel);
int dnd_input_flags(int port);
int dnd_input_buttons(int port, int kind);
float dnd_input_axis(int port, int axis);
void dnd_input_configure(int port, float stick_deadzone, float cstick_deadzone,
    float stick_maximum, float cstick_maximum, float trigger_minimum, float trigger_maximum);
bool dnd_input_rumble(int port, float seconds);
void dnd_input_stop_rumble(int port);
/* Platform backend. Alarm callbacks perform native motor commands only. */
void dnd_input_backend_init(void);
void dnd_input_backend_shutdown(void);
void dnd_input_backend_sample(DndInputSample samples[4]);
bool dnd_input_backend_rumble(int port, uint32_t microseconds);
void dnd_input_backend_stop_rumble(int port);
#endif
