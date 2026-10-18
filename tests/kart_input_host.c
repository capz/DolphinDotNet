#include "dnd_managed.h"
#include "dnd_input.h"
#include <stdio.h>
#include <string.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static unsigned polls;
void dnd_input_backend_init(void){}
void dnd_input_backend_shutdown(void){}
void dnd_input_backend_sample(DndInputSample pads[4]) {
    memset(pads,0,4*sizeof(*pads));pads[0].connected=true;
    /* Neutral connection, A through countdown, B to stop, then A + right stick. */
    unsigned frame=polls++;
    pads[0].held=frame<2?0:frame<240?0x0100u:frame<270?0x0200u:0x0100u;
    if(frame>=270)pads[0].stick_x=80;
}
bool dnd_input_backend_rumble(int port,uint32_t us){(void)port;(void)us;return false;}
void dnd_input_backend_stop_rumble(int port){(void)port;}
#ifndef GEKKO
int main(void){static unsigned char memory[512*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(true);intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"Kart input result=%ld exception=%s\n",(long)result,dnd_exception_message());return 1;}puts("Kart paired countdown, routed throttle, braking and steering passed.");return 0;}

#endif
