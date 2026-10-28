#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static int configured,reset;
void dnd_draw_backend_options(const float *p,int scene){if(p){configured++;if(scene)assert(p[13]==3&&p[4]==1);else assert(p[0]>=0&&p[0]<=2);}else reset++;}
int main(void){static unsigned char storage[1024*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,storage,sizeof(storage));dnd_gc_set_stress(true);intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"phase two result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}assert(configured==0&&reset>=3);assert(!dnd_device_active()&&!dnd_draw_live()&&!dnd_draw_total_bytes());puts("Graphics phase two AOT/GC, camera math, bulk submissions, clips and state snapshots passed.");return 0;}
