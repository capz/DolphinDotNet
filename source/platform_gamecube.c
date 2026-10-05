#include "dnd_platform.h"
#include "dnd_console.h"
#include "dnd_graphics.h"
#include "dnd_input.h"
#include <stdio.h>
#include <stdlib.h>

void dnd_platform_init(void) {
    dnd_graphics_init();
    dnd_input_init();
    dnd_console_init();
}
void dnd_platform_write(const char *text) { dnd_console_write(text); }
void dnd_platform_write_int(int32_t value) { char b[24]; snprintf(b,sizeof(b),"%ld",(long)value); dnd_console_write(b); }
void dnd_platform_wait_forever(void) {
    for (;;) {
        dnd_input_poll();
        const DndGamePad *p=dnd_input_gamepad(0);
        if(p && (p->down & DND_PAD_BUTTON_START)) exit(0);
        dnd_graphics_begin_frame(0.025f,0.035f,0.06f,1.0f);
        dnd_graphics_begin_overlay(); dnd_console_render(); dnd_graphics_end_frame();
    }
}
