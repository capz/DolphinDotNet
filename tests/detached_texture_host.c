#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void){static unsigned char storage[2*1024*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,storage,sizeof(storage));dnd_gc_set_stress(true);intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"detached result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}assert(!dnd_device_active()&&!dnd_draw_live()&&!dnd_draw_total_bytes());puts("Detached upload AOT/GC: range/CRC/cancellation/drain/source disposal/fence retirement passed.");return 0;}
