#include "dnd_managed.h"
#include "dnd_async.h"
#include "dnd_input.h"
#include <assert.h>
#include <stdio.h>

static unsigned probes,polls;
static const int presence[8][2]={{1,0},{2,1},{3,1},{0,2},{1,0},{4,3},{99,0},{0,1}};
int dnd_hardware_probe(int kind,int port,int mode) {
    assert(kind==2 && mode==1 && probes<16 && port==4+(int)(probes%2));
    unsigned index=probes++;
    return presence[index/2][index%2];
}
void dnd_input_backend_init(void){}
void dnd_input_backend_shutdown(void){}
bool dnd_input_backend_rumble(int port,uint32_t us){(void)port;(void)us;return false;}
void dnd_input_backend_stop_rumble(int port){(void)port;}
void dnd_input_backend_sample(DndInputSample pads[4]) {
    polls++;
    for(unsigned p=0;p<4;p++)
        pads[p]=(DndInputSample){.connected=!(polls>=2 && polls%2==0 && p==(polls-2)/2),.supports_rumble=true,.held=0x100};
}
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void) {
    static unsigned char storage[1024*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,storage,sizeof(storage));dnd_gc_set_stress(true);
    intptr_t result=dnd_aot_entry(&heap);
    if(result || dnd_exception_pending()) {fprintf(stderr,"connection result=%ld exception=%s\n",(long)result,dnd_exception_message());return 1;}
    assert(probes==16 && polls==9 && !dnd_async_running());
    puts("Managed card A/B and four-port controller connection transitions passed.");
}
