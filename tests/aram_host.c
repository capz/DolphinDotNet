#include <stdint.h>
#include <stdio.h>
#include <errno.h>
#include "dnd_managed.h"
#include "dnd_aram.h"

extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);

/* This managed-array fixture has no native graphics buffers. Mach-O linkers
 * require explicit hooks even though the production backend declares them weak. */
int dnd_buffer_hold(int token) { (void)token; return -EINVAL; }
int dnd_buffer_unhold(int token) { (void)token; return -EINVAL; }
uint64_t dnd_buffer_held_address(int token,int offset,int count)
{ (void)token; (void)offset; (void)count; return 0; }

int main(int argc, char **argv)
{
    static unsigned char storage[128 * 1024];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, storage, sizeof(storage));
    (void)argv;
    dnd_gc_set_stress(argc > 1);
    intptr_t result = dnd_value_aot_entry(&heap);
    if (dnd_exception_pending() || result != 0 || dnd_aram_stat(4) != 0) {
        fprintf(stderr, "compiled managed smoke failed: result=%ld exception=%d message=%s\n",
            (long)result, (int)dnd_exception_kind(), dnd_exception_message());
        return 1;
    }
    printf("managed heap: capacity=%zu high_water=%zu collections=%zu\n",heap.capacity,heap.used,heap.collections);
    puts("compiled managed ARAM/cache integration passed");
    return 0;
}
