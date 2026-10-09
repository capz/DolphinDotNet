#include <stdint.h>
#include <stdio.h>
#include "dnd_managed.h"

extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);

int main(int argc, char **argv)
{
    static unsigned char storage[64 * 1024];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, storage, sizeof(storage));
    (void)argv;
    dnd_gc_set_stress(argc > 1);
    intptr_t result = dnd_value_aot_entry(&heap);
    if (dnd_exception_pending() || result != 0) {
        fprintf(stderr, "compiled managed smoke failed: result=%ld exception=%d message=%s\n",
            (long)result, (int)dnd_exception_kind(), dnd_exception_message());
        return 1;
    }
    printf("managed heap: capacity=%zu high_water=%zu collections=%zu\n",heap.capacity,heap.used,heap.collections);
    puts("compiled managed generic/lifecycle/exception integration passed");
    return 0;
}
