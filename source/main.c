#include "dnd_platform.h"
#include "dnd_managed.h"
#include "dnd_input.h"
#include "dnd_graphics.h"
#include "dnd_console.h"
#include "dnd_network.h"
#include "dnd_worker.h"
#include <gccore.h>
#include <stdint.h>
#include <stdlib.h>

static uint8_t managed_heap_memory[256 * 1024];
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);

int main(void) {
    DndManagedHeap heap; int network_status;
    dnd_platform_init();
    /* Native-only I/O worker; managed continuations remain on main until the
     * GC and scheduler are explicitly made thread-aware. */
    bool worker_started = dnd_worker_start();
    dnd_managed_heap_init(&heap,managed_heap_memory,sizeof(managed_heap_memory));
    dnd_console_write_line("DOLPHINDOTNET");
    dnd_console_write_line("MANAGED C# AOT / OPENGX / PAD / NETWORK");
    network_status=dnd_network_init();
    network_status=network_status<0?-1:dnd_net_status();
    dnd_console_write_line(network_status==1?"NETWORK: READY":network_status==0?"NETWORK: CONFIGURING":"NETWORK: UNAVAILABLE");

    intptr_t managed_result=dnd_aot_entry(&heap);
    if (worker_started) dnd_worker_stop();
    dnd_console_write_line(managed_result==0?"MANAGED MAIN: OK":"MANAGED MAIN: FAILED");
    return managed_result==0?0:1;
}
