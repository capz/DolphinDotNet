#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "effect_package_test.h"
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
extern int graphics_draws,graphics_effect_draws;
int main(void){
 static unsigned char memory[512*1024];DndManagedHeap heap;
 dnd_managed_heap_init(&heap,memory,sizeof memory);dnd_gc_set_stress(true);
 intptr_t result=dnd_aot_entry(&heap);
 if(result||dnd_exception_pending()){
  fprintf(stderr,"TEV alpha AOT result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;
 }
 assert(graphics_draws==5&&graphics_effect_draws==4);
 assert(!dnd_device_active()&&!dnd_draw_live()&&!dnd_draw_total_bytes());
 effect_package_tests();
 puts("TEV alpha AOT: dual alpha, konst, full stage budget, native validation and zero leaked resources under GC stress passed.");return 0;
}
