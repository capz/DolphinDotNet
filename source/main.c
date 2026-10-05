#include "dnd_platform.h"
#include "dnd_managed.h"
#include "dnd_input.h"
#include "dnd_graphics.h"
#include "dnd_console.h"
#include "dnd_network.h"
#include <gccore.h>
#include <stdint.h>
#include <stdlib.h>

static uint8_t managed_heap_memory[256 * 1024];
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);

int main(void) {
    DndManagedHeap heap; float rotation=0.0f; int network_status;
    dnd_platform_init();
    dnd_managed_heap_init(&heap,managed_heap_memory,sizeof(managed_heap_memory));
    dnd_console_write_line("DOLPHINDOTNET");
    dnd_console_write_line("MANAGED C# AOT / OPENGX / PAD / NETWORK");
    network_status=dnd_network_init();
    dnd_console_write_line(network_status>=0?"NETWORK: READY":"NETWORK: UNAVAILABLE");

    intptr_t managed_result=dnd_aot_entry(&heap);
    dnd_console_write_line(managed_result==0?"MANAGED MAIN: OK":"MANAGED MAIN: FAILED");

    for (;;) {
        dnd_input_poll();
        const DndGamePad *pad=dnd_input_gamepad(0);
        if(pad) {
            rotation += ((float)pad->stick_x/128.0f)*3.0f;
            if(pad->down & PAD_BUTTON_A) dnd_console_write_line("PAD A PRESSED");
            if(pad->down & PAD_BUTTON_Y) dnd_console_set_visible(!dnd_console_visible());
            if(pad->down & PAD_BUTTON_START) exit(0);
        }
        rotation += 0.35f;
        dnd_graphics_begin_frame(0.025f,0.035f,0.06f,1.0f);
        dnd_graphics_draw_demo(rotation);
        dnd_graphics_begin_overlay();
        dnd_console_render();
        dnd_graphics_end_frame();
    }
}
