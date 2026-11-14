#include "dnd_storage.h"
#include <assert.h>
#include <stdio.h>
extern intptr_t dnd_value_aot_entry(DndManagedHeap *);
int main(int argc,char **argv) {
  (void)argv;static unsigned char memory[128*1024];DndManagedHeap heap;
  dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>1);
  intptr_t result=dnd_value_aot_entry(&heap);
  if(result||dnd_exception_pending()||dnd_fs_open_handles()) {
    fprintf(stderr,"GC Loader AOT smoke result=%ld exception=%d message=%s\n",
      (long)result,dnd_exception_kind(),dnd_exception_message());return 1;
  }
  puts("GC Loader selection managed null validation, native binding and unsupported-host exception passed.");
  return 0;
}
