#include "dnd_managed.h"
#include "dnd_async.h"
#include <assert.h>
#include <stdio.h>
static int next,active,remaining,aborts,steps,published;
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
void dnd_console_write_line(const char *text){puts(text);}
int dnd_fs_contiguous_begin(DndString *path,int64_t length){
  (void)path;assert(!active);active=++next;remaining=length==99?-1:3;return active;
}
int dnd_fs_contiguous_complete_step(int token){
  assert(token==active && dnd_async_context()==0);steps++;
  if(remaining<0){dnd_exception_throw(DND_EXCEPTION_IO,"Injected verification failure");return -1;}
  if(remaining-->0)return 0;
  active=0;published++;return 1;
}
void dnd_fs_contiguous_abort(int token){assert(token==active);active=0;aborts++;}
void dnd_fs_contiguous_write(int token,DndArray *data,int offset,int count){
  (void)token;(void)data;(void)offset;(void)count;assert(!"Write must reject pending/disposed writers");
}
void dnd_fs_contiguous_complete(int token){(void)token;assert(!"Complete must reject pending writers");}
int main(int argc,char **argv){
  (void)argv;static unsigned char storage[256*1024];DndManagedHeap heap;
  dnd_managed_heap_init(&heap,storage,sizeof(storage));dnd_gc_set_stress(argc>1);
  intptr_t result=dnd_value_aot_entry(&heap);
  if(result||dnd_exception_pending()){
    fprintf(stderr,"Contiguous async: result=%ld exception=%s\n",(long)result,dnd_exception_message());
    DndException *e=(DndException*)dnd_exception_object();
    if(e&&e->message){for(uint32_t i=0;i<e->message->length;i++)fputc(e->message->chars[i],stderr);fputc('\n',stderr);}return 1;
  }
  assert(next==5 && active==0 && aborts==4 && published==1 && steps==7);
  printf("Contiguous async native calls verified; GC collections=%zu\n",heap.collections);return 0;
}
