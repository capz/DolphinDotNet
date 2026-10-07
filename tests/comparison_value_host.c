#include <stdint.h>
#include <stdio.h>
#include "dnd_managed.h"

extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);

int main(void)
{
    unsigned char storage[4096];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap,storage,sizeof(storage));
    intptr_t result=dnd_value_aot_entry(&heap);
    if(dnd_exception_kind()!=DND_EXCEPTION_NONE){
        fprintf(stderr,"comparison smoke exception=%d\n",(int)dnd_exception_kind());
        return 2;
    }
    if(result!=0){
        fprintf(stderr,"comparison smoke result=%ld\n",(long)result);
        return 1;
    }
    return 0;
}
