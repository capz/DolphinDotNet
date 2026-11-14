/* Synthetic native results test AOT facade/control flow only. These patterns
 * are public fixture data, never a production entropy fallback. */
#include "dnd_managed.h"
#include "dnd_async.h"
#include "dnd_platform_entropy.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>

static DndObject *last_output;
static int calls,cleared,service_calls;
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
extern intptr_t dnd_value_Program_RequestCancellation(void);

static void check_cleared(void){
    if(!last_output)return;
    DndArray *output=(DndArray *)last_output;
    for(uint32_t i=0;i<output->length;i++)assert(output->data[i]==0);
    cleared++;
}

int dnd_platform_entropy_fill_array(DndArray *output,int32_t timeout_ms){
    assert(!dnd_async_running()||dnd_async_context()==1);
    if(dnd_async_running())service_calls++;
    check_cleared();
    assert(output&&output->element_size==1&&!output->elements_are_references);
    assert(output->length==(calls==0?48u:64u)&&timeout_ms==30000);
    last_output=(DndObject *)output;
    calls++;
    for(uint32_t i=0;i<output->length;i++)output->data[i]=(unsigned char)((i+calls*17)%251+1);
    if(calls==3)return DND_ENTROPY_SOURCE;
    if(calls==4)return DND_ENTROPY_MEMORY;
    if(calls==5)return DND_ENTROPY_TIMEOUT;
    if(calls==6)return DND_ENTROPY_BUSY;
    if(calls==7)return DND_ENTROPY_INVALID;
    if(calls==8){
        (void)dnd_value_Program_RequestCancellation();
        assert(!dnd_exception_pending());
    }
    assert(calls<=9);
    return DND_ENTROPY_OK;
}

void dnd_console_write_line(const char *text){puts(text);}

int main(int argc,char **argv){
    (void)argv;
    static unsigned char storage[256*1024];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap,storage,sizeof(storage));
    dnd_gc_set_stress(argc>1);
    DndObject **slots[]={&last_output};DndRootSet roots;
    DndGcRootRegistration registration={0};
    dnd_roots_init(&roots,slots,1);
    assert(dnd_root_add(&roots,&last_output));
    assert(dnd_gc_register_roots(&registration,&roots));
    intptr_t result=dnd_value_aot_entry(&heap);
    if(result||dnd_exception_pending()){
        fprintf(stderr,"Entropy AOT smoke: result=%ld calls=%d exception=%s\n",(long)result,calls,dnd_exception_message());
        DndException *error=(DndException *)dnd_exception_object();
        if(error&&error->message){
            for(uint32_t i=0;i<error->message->length;i++)fputc(error->message->chars[i],stderr);
            fputc('\n',stderr);
        }
        return 1;
    }
    check_cleared();
    assert(calls==9&&cleared==9);
    assert(service_calls==(getenv("DND_ASYNC_MANUAL")?0:9));
    dnd_gc_unregister_roots(&registration);
    printf("Entropy AOT mock calls=%d service calls=%d cleared buffers=%d GC collections=%zu\n",calls,service_calls,cleared,heap.collections);
    return 0;
}
