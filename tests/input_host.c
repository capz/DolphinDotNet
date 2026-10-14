#include "dnd_input.h"
#include "dnd_managed.h"
#include "dnd_async.h"
#include <assert.h>
#include <stdio.h>
static unsigned polls, motors;
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
void dnd_input_backend_init(void) { }
void dnd_input_backend_shutdown(void) { }
void dnd_input_backend_sample(DndInputSample pads[4]) {
    polls++;
    pads[0]=(DndInputSample){.connected=polls!=3,.supports_rumble=polls!=4,
        .held=polls==1?0x100:polls==2?0x240:polls==4?0x400:0x900,
        .stick_x=polls==1?80:polls==5?40:5,.cstick_y=polls==1?65:0,.trigger_l=polls==1?180:polls==5?100:105};
    pads[1].connected=true;
}
bool dnd_input_backend_rumble(int port,uint32_t us) { (void)us; motors|=1u<<port; return true; }
void dnd_input_backend_stop_rumble(int port) { motors&=~(1u<<port); }
int main(void) {
    static unsigned char memory[256*1024]; DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory)); dnd_gc_set_stress(true);
    intptr_t result=dnd_aot_entry(&heap);
    if (result || dnd_exception_pending()) { fprintf(stderr,"input result=%ld exception=%s\n",(long)result,dnd_exception_message()); return 1; }
    assert(polls==5 && motors==0 && !dnd_async_running());
    printf("Managed input snapshots, bindings and executor guard passed (%zu collections).\n",heap.collections);
}
