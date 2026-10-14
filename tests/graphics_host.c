#include "dnd_draw.h"
#include <stdio.h>
#include <assert.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
extern int graphics_draws,graphics_vertices,graphics_raw_commands;
int main(void){static unsigned char memory[512*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(true);intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"graphics result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}assert(!dnd_device_active()&&!dnd_draw_live()&&!dnd_draw_total_bytes());assert(graphics_draws>3&&graphics_vertices>21&&graphics_raw_commands==5);assert(dnd_device_begin(0,0,0,1)==0);assert(dnd_device_raw_begin()==0);assert(dnd_raw_enter(2)==0);assert(dnd_raw_enter(1)==0);assert(dnd_raw_enter(1)<0);assert(dnd_device_raw_end()<0);assert(dnd_raw_leave(2)<0);assert(dnd_raw_leave(1)==0);assert(dnd_raw_leave(2)==0);assert(dnd_device_raw_end()==0);assert(dnd_device_end()==0);
int token=dnd_buffer_create(32,32);assert(token>0);assert((dnd_buffer_address(token,0)&31)==0);assert(dnd_device_begin(0,0,0,1)==0);assert(dnd_draw_release(token)==0);assert(dnd_draw_live()==1);assert(dnd_draw_release(token)<0);assert(dnd_device_end()==0);assert(dnd_draw_live()==0);assert(dnd_buffer_create(1,3)==0);assert(dnd_canvas_create(5)==0);
puts("Graphics AOT: shared 3D/raw/2D frame, ordered batching, deferred disposal and stress GC passed.");return 0;}
