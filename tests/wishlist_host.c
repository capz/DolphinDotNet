#include "dnd_managed.h"
#include <stdio.h>
void dnd_console_write_line(const char *text){puts(text);}
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void){static unsigned char memory[2*1024*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(true);intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"Wishlist result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}printf("Wishlist AOT passed: collections=%zu heap=%zu\n",heap.collections,heap.used);return 0;}
