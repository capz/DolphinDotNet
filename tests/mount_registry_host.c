#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
static char volume[1024],removed[1024];
void dnd_console_write_line(const char *message){
  if(!strcmp(message,"REMOVE_SD"))assert(!rename(volume,removed));
  if(!strcmp(message,"RESTORE_SD"))assert(!rename(removed,volume));
}
void dnd_console_write_managed_line(DndString *message) {
  char text[100];unsigned n=message->length<99?message->length:99;
  for(unsigned i=0;i<n;i++)text[i]=(char)message->chars[i];
  text[n]=0;dnd_console_write_line(text);
}
extern intptr_t dnd_value_aot_entry(DndManagedHeap *);
int main(int argc,char **argv) {
  (void)argv;char root[]="/tmp/dnd-mount-managed-XXXXXX";assert(mkdtemp(root));
  snprintf(volume,sizeof(volume),"%s/sd",root);snprintf(removed,sizeof(removed),"%s/removed",root);
  assert(!mkdir(volume,0700)&&!setenv("DND_STORAGE_ROOT",root,1));
  static unsigned char memory[512*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,sizeof(memory));
  dnd_gc_set_stress(argc>1);intptr_t result=dnd_value_aot_entry(&heap);
  if(result||dnd_exception_pending()||dnd_fs_open_handles()) {
    fprintf(stderr,"mount smoke result=%ld exception=%d handles=%d message=%s\n",(long)result,
      dnd_exception_kind(),dnd_fs_open_handles(),dnd_exception_message());return 1;
  }
  assert(!rmdir(volume)&&!rmdir(root));puts("Managed mount discovery, remount, borrowed access and events passed.");
}
