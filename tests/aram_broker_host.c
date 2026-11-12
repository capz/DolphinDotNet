#include "dnd_aram.h"
#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(int argc,char **argv){
 static unsigned char memory[1024*1024];DndManagedHeap heap;
 dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>1);(void)argv;
 intptr_t result=dnd_aot_entry(&heap);
 if(result||dnd_exception_pending()){
  fprintf(stderr,"ARAM broker AOT result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;
 }
 assert(!dnd_aram_stat(4)&&!dnd_aram_broker_stat(0)&&!dnd_draw_live()&&!dnd_draw_total_bytes());
 puts("ARAM broker AOT roundtrip, ordering, priorities, cancellation and disposed-owner lifetime passed.");return 0;
}
