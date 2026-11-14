#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>
#include <unistd.h>
extern intptr_t dnd_value_aot_entry(DndManagedHeap *);
int main(int argc,char **argv) {
  char root[]="/tmp/dnd-gcloader-select-XXXXXX";assert(mkdtemp(root));
  assert(!setenv("DND_STORAGE_ROOT",root,1));
  char path[128];snprintf(path,sizeof(path),"%s/sd",root);assert(!mkdir(path,0700));
  (void)argv;static unsigned char memory[128*1024];DndManagedHeap heap;
  dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>1);
  intptr_t result=dnd_value_aot_entry(&heap);
  if(result||dnd_exception_pending()||dnd_fs_open_handles()) {
    fprintf(stderr,"GC Loader AOT smoke result=%ld exception=%d message=%s\n",
      (long)result,dnd_exception_kind(),dnd_exception_message());return 1;
  }
  assert(!rmdir(path)&&!rmdir(root));
  puts("GC Loader selection native binding, caught managed OOM/storage diagnostics and GC stress passed.");
  return 0;
}
