/* Emulator acceptance: success is published only after async Main and the
 * service-thread shutdown return successfully. No networking/video required. */
#include "dnd_managed.h"
#include "dnd_async.h"
#include <stdint.h>
#include <unistd.h>
volatile uint32_t dnd_async_test_result;
static unsigned char storage[256 * 1024];
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void) {
    DndManagedHeap heap;
    dnd_async_test_result = 1;
    dnd_managed_heap_init(&heap, storage, sizeof(storage));
    dnd_gc_set_stress(true);
    intptr_t result = dnd_aot_entry(&heap);
    dnd_async_test_result = result == 0 && !dnd_exception_pending() && !dnd_async_running()
        ? 0x4153594e : 0xdead0000u | (uint32_t)result;
    for (;;) usleep(1000);
}
