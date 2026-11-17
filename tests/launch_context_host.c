#include "dnd_managed.h"
#include "dnd_launch.h"
#include <stdio.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(int argc,char **argv) {
    (void)argv;
    const char *incoming[]={"cardb:/apps/example/default.dol","caption=\xc3\xbc"};
    dnd_launch_capture(argc>1?2:0,incoming);
    static unsigned char memory[256*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>2);
    intptr_t result=dnd_aot_entry(&heap);
    if(result||dnd_exception_pending()){fprintf(stderr,"Launch context result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}
    puts("Launch context AOT: incoming UTF-8, argument array, bounds and absent argv passed");return 0;
}
