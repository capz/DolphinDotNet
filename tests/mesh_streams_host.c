#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static int blocks;
bool dnd_draw_backend_mesh_passes(const DndDrawVertex *v,uint32_t c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w,const unsigned char *d,int bytes,int count,const unsigned char *colors,const float *uv,int passes){
 assert(v&&c==0xffffffff&&t&&m->color==0x0b16212c&&w&&d[0]==0x90&&bytes==32&&count==3&&passes==2);
 assert(colors[0]==255&&colors[3]==128&&colors[5]==255&&colors[7]==64&&uv[4]==.25f&&uv[10]==.75f);blocks++;return true;
}
int main(void){static unsigned char storage[1024*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,storage,sizeof storage);dnd_gc_set_stress(true);intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"mesh streams result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}assert(blocks==1&&!dnd_device_active()&&!dnd_draw_live()&&!dnd_draw_total_bytes());puts("Mesh streams AOT/GC: managed bindings, color/UV array layout, shared lifetime, frame guards and disposal passed.");}
