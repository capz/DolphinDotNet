#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include "dnd_managed.h"

intptr_t dnd_aot_entry(DndManagedHeap *heap);

int main(void)
{
    unsigned char storage[4096];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, storage, sizeof(storage));
    intptr_t result=dnd_aot_entry(&heap);
    if(result!=57)
    {
        fprintf(stderr,"control-flow result: %ld (expected 57)\n",(long)result);
        return 1;
    }
    return 0;
}
