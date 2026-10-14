#include "dnd_managed.h"
#include <stdio.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void) {
    static unsigned char memory[512*1024];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));
    dnd_gc_set_stress(true);
    intptr_t result=dnd_aot_entry(&heap);
    if (result || dnd_exception_pending()) {
        fprintf(stderr,"input contexts result=%ld exception=%s\n",(long)result,dnd_exception_message());
        return 1;
    }
    printf("Input contexts, consumption, suppression, capture and lifecycle passed (%zu collections).\n",heap.collections);
    return 0;
}
