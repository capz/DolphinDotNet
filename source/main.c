#include "dnd_platform.h"
#include "dnd_managed.h"
#include "dnd_input.h"
#include "dnd_audio.h"
#include "dnd_graphics.h"
#include "dnd_console.h"
#include "dnd_network.h"
#include "dnd_storage.h"
#include "dnd_dol.h"
#include <gccore.h>
#include <stdint.h>
#include <stdlib.h>

#ifdef DND_GDB_ENABLED
extern void dnd_gdb_init(void);
#endif

static uint8_t managed_heap_memory[256 * 1024];
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);

int main(void) {
    DndManagedHeap heap;
    dnd_platform_init();
#ifdef DND_GDB_ENABLED
    // Debugger rendezvous occurs before managed code and normal BBA init.
    dnd_gdb_init();
#endif
    dnd_managed_heap_init(&heap,managed_heap_memory,sizeof(managed_heap_memory));
    dnd_console_write_line("DOLPHINDOTNET");
    dnd_console_write_line("MANAGED C# AOT / GX / PAD");
#ifndef DND_NO_AUTONETWORK
    int network_status=dnd_network_init();
    network_status=network_status<0?-1:dnd_net_status();
    dnd_console_write_line(network_status==1?"NETWORK: READY":network_status==0?"NETWORK: CONFIGURING":"NETWORK: UNAVAILABLE");
#endif

    intptr_t managed_result=dnd_aot_entry(&heap);
    dnd_audio_shutdown();
    if (dnd_dol_pending() && managed_result==0 && !dnd_exception_pending()) {
        dnd_input_shutdown();
        dnd_net_stop();
        dnd_storage_shutdown();
    }
    dnd_dol_finish(managed_result==0 && !dnd_exception_pending());
    dnd_console_write_line(managed_result==0?"MANAGED MAIN: OK":"MANAGED MAIN: FAILED");
#ifdef DND_SAMPLE_CONTROLS
    dnd_console_write_line("Z: STATISTICS   START: EXIT");
    dnd_platform_wait_forever();
#endif
    return managed_result==0?0:1;
}
