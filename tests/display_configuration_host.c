#include "dnd_draw.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static int resolution=2,scan=1;
static int saw_wide,saw_half,saw_fit,configurations;
int dnd_graphics_configure_display(int r,int s){configurations++;if(r==1&&s==1)return -1;if(r==3&&s==1)return -2;if(r)resolution=r;if(s)scan=s;if(r==1)scan=2;return 0;}
int dnd_graphics_width(void){return resolution==1?320:640;}
int dnd_graphics_height(void){return resolution==1?240:480;}
int dnd_graphics_display_stat(int kind){return kind==0?resolution:kind==1?scan:kind==2||kind==4?dnd_graphics_width():kind==3?resolution==3?576:dnd_graphics_height():kind==5?dnd_graphics_height():kind==6?resolution==3?50000:59940:-1;}
void dnd_draw_backend_2d_projection(float w,float h){if(fabsf(w-480*16.f/9)<.002f&&h==480)saw_wide++;if(fabsf(w-240*16.f/9)<.002f&&h==240)saw_half++;}
void dnd_draw_backend_viewport(int x,int y,int w,int h){assert(x>=0&&y>=0&&x+w<=dnd_graphics_width()&&y+h<=dnd_graphics_height());if(x==80&&y==0&&w==480&&h==480)saw_fit++;}
void dnd_draw_backend_scissor(int x,int y,int w,int h){assert(x>=0&&y>=0&&w>0&&h>0&&x+w<=dnd_graphics_width()&&y+h<=dnd_graphics_height());}
int main(void){
 static unsigned char storage[1024*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,storage,sizeof storage);dnd_gc_set_stress(true);
 assert(dnd_raw_enter(1)==0);assert(dnd_device_configure_display(0,0,1)<0&&!configurations);assert(dnd_raw_leave(1)==0);
 intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"display result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}
 assert(saw_wide&&saw_half&&saw_fit);assert(!dnd_device_active()&&!dnd_draw_live()&&!dnd_draw_total_bytes());
 int before=configurations;assert(dnd_device_claim_external()==0);assert(dnd_device_configure_display(0,0,1)<0&&configurations==before);
 puts("Display AOT/GC: widescreen-only dimensions, automatic/explicit cameras, project/unproject, fractional logical canvases, retained geometry, fit/stretch, invalid modes and frame guards passed.");
}
