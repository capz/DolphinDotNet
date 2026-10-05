#include "dnd_platform.h"
#include "dnd_runtime.h"
#include "dnd_vm.h"
#include "dnd_input.h"
#include "dnd_graphics.h"
#include "dnd_console.h"
#include "dnd_network.h"
#include "../generated/generated_program.h"
#include <gccore.h>
#include <stdint.h>
#include <stdlib.h>

static uint8_t managed_heap[256 * 1024];

int main(void) {
    DndHeap heap; DndVm vm; float rotation=0.0f; int network_status;
    dnd_platform_init();
    dnd_heap_init(&heap,managed_heap,sizeof(managed_heap));
    dnd_console_write_line("DOLPHINDOTNET");
    dnd_console_write_line("OPENGL VIA OPENGX / PAD / NETWORK");
    network_status=dnd_network_init();
    dnd_console_write_line(network_status>=0?"NETWORK: READY":"NETWORK: UNAVAILABLE");

    dnd_vm_init(&vm,dnd_generated_code,dnd_generated_code_size,
                dnd_generated_strings,dnd_generated_string_count);
    if(!dnd_vm_run(&vm)) dnd_console_write_line("MANAGED RUNTIME FAULT");

    while(SYS_MainLoop()) {
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
    return vm.return_value;
}
