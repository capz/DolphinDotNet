#include <stdint.h>
#include <stdio.h>
#include "dnd_managed.h"
void dnd_console_write_line(const char *text){puts(text);}
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
int main(int argc,char **argv){static unsigned char storage[256*1024];DndManagedHeap heap;(void)argv;dnd_managed_heap_init(&heap,storage,sizeof(storage));dnd_gc_set_stress(argc>1);intptr_t result=dnd_value_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"network smoke: result=%ld exception=%d type=%s\n",(long)result,(int)dnd_exception_kind(),dnd_exception_message());DndException *e=(DndException*)dnd_exception_object();if(e&&e->message){fputs("message: ",stderr);for(uint32_t i=0;i<e->message->length&&i<256;i++)fputc(e->message->chars[i]<128?e->message->chars[i]:'?',stderr);fputc('\n',stderr);}return 1;}printf("network smoke passed: heap=%zu collections=%zu\n",heap.used,heap.collections);return 0;}
