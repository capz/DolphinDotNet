#include <stdint.h>
#include <stdio.h>
#include "dnd_managed.h"

extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);

int main(void)
{
    static unsigned char storage[1024 * 1024];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, storage, sizeof(storage));
    intptr_t result = dnd_value_aot_entry(&heap);
    if (dnd_exception_pending() || result != 0) {
        fprintf(stderr, "compiled managed smoke failed: result=%ld exception=%d message=%s\n",
            (long)result, (int)dnd_exception_kind(), dnd_exception_message());
        return 1;
    }
    puts("compiled managed generic/lifecycle/exception integration passed");
    return 0;
}
