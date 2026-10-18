#include "dnd_managed.h"
#include <stdio.h>
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
int main(void) {
    static unsigned char storage[256*1024];
    DndManagedHeap heap;dnd_managed_heap_init(&heap,storage,sizeof(storage));
    dnd_gc_set_stress(true);
    intptr_t result=dnd_value_aot_entry(&heap);
    if(result || dnd_exception_pending()) {
        fprintf(stderr,"Kart items: result=%ld error=%s\n",(long)result,dnd_exception_message());
        return 1;
    }
    puts("Kart AOT pickups, item physics, hit acknowledgements and replay protection passed.");
    return 0;
}
