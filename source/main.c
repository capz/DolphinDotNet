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
    DndManagedHeap heap; int network_status;
    dnd_platform_init();
    dnd_managed_heap_init(&heap,managed_heap_memory,sizeof(managed_heap_memory));
    dnd_console_write_line("DOLPHINDOTNET");
    dnd_console_write_line("MANAGED C# AOT / OPENGX / PAD / NETWORK");
    network_status=dnd_network_init();
    dnd_console_write_line(network_status>=0?"NETWORK: READY":"NETWORK: UNAVAILABLE");

    intptr_t managed_result=dnd_aot_entry(&heap);
    dnd_console_write_line(managed_result==0?"MANAGED MAIN: OK":"MANAGED MAIN: FAILED");
    return managed_result==0?0:1;
}
