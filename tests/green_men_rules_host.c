#include "dnd_managed.h"
#include <stdio.h>
#include <stdint.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void) {
    static unsigned char memory[256*1024];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(true);
    intptr_t result=dnd_aot_entry(&heap);
    if(result || dnd_exception_pending()) {
        fprintf(stderr,"Green Men rules failed: result=%ld kind=%d %s\n",(long)result,dnd_exception_kind(),dnd_exception_message());
        return 1;
    }
    puts("Green Men AOT gameplay, collisions, waves, pause, damage, determinism and audio passed.");
    return 0;
}
