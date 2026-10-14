#include "dnd_managed.h"
#include "dnd_async.h"
#include <assert.h>
#include <stdio.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
extern intptr_t dnd_value_Dolphin_Timing_TimingRuntime_get_Pending(void);
int main(void) {
    static unsigned char memory[256*1024]; DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory)); dnd_gc_set_stress(true);
    intptr_t result=dnd_aot_entry(&heap);
    if (result || dnd_exception_pending()) { fprintf(stderr,"timing result=%ld exception=%s\n",(long)result,dnd_exception_message()); return 1; }
    assert(!dnd_async_running() && dnd_value_Dolphin_Timing_TimingRuntime_get_Pending()==0);
    printf("Timing clocks, delays, captured callbacks and shutdown passed (%zu collections).\n",heap.collections);
}
