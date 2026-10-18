#define _POSIX_C_SOURCE 200809L
#include "dnd_draw.h"
#include <stdio.h>
#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/stat.h>

#include "png_texture_test.h"
#include "effect_package_test.h"
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
extern int graphics_draws,graphics_vertices,graphics_raw_commands,graphics_effect_draws;
static void fixture(const char *root,const char *name,const void *data,size_t length){char path[256];snprintf(path,sizeof(path),"%s/sd/%s",root,name);FILE *file=fopen(path,"wb");assert(file);assert(fwrite(data,1,length,file)==length);assert(fclose(file)==0);}
int main(void){char root[]="/tmp/dnd-graphics-XXXXXX";assert(mkdtemp(root));assert(setenv("DND_STORAGE_ROOT",root,1)==0);char path[256];snprintf(path,sizeof(path),"%s/sd",root);assert(mkdir(path,0700)==0);unsigned char tx[96]={'D','N','T','X',1};tx[8]=6;tx[12]=tx[16]=4;tx[20]=64;fixture(root,"graphics.dndtex",tx,sizeof(tx));unsigned char font[128]={'D','N','F','T',1};font[8]=font[12]=1;font[16]=32;font[20]=16;font[24]=96;memcpy(font+32,tx,96);fixture(root,"graphics.dndfont",font,sizeof(font));static unsigned char memory[512*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(true);intptr_t result=dnd_aot_entry(&heap);if(result||dnd_exception_pending()){fprintf(stderr,"graphics result=%ld error=%s\n",(long)result,dnd_exception_message());return 1;}assert(!dnd_device_active()&&!dnd_draw_live()&&!dnd_draw_total_bytes());assert(graphics_effect_draws>=2);assert(graphics_draws>3&&graphics_vertices>21&&graphics_raw_commands==8);assert(dnd_device_begin(0,0,0,1)==0);assert(dnd_device_raw_begin()==0);assert(dnd_raw_enter(2)==0);assert(dnd_raw_enter(1)==0);assert(dnd_raw_enter(1)<0);assert(dnd_device_raw_end()<0);assert(dnd_raw_leave(2)<0);assert(dnd_raw_leave(1)==0);assert(dnd_raw_leave(2)==0);assert(dnd_device_raw_end()==0);assert(dnd_device_end()==0);
int token=dnd_buffer_create(32,32);assert(token>0);assert((dnd_buffer_address(token,0)&31)==0);assert(dnd_device_begin(0,0,0,1)==0);assert(dnd_draw_release(token)==0);assert(dnd_draw_live()==1);assert(dnd_draw_release(token)<0);assert(dnd_device_end()==0);assert(dnd_draw_live()==0);assert(dnd_buffer_create(1,3)==0);assert(dnd_canvas_create(5)==0);
snprintf(path,sizeof(path),"%s/sd/graphics.dndtex",root);assert(unlink(path)==0);snprintf(path,sizeof(path),"%s/sd/graphics.dndfont",root);assert(unlink(path)==0);snprintf(path,sizeof(path),"%s/sd",root);assert(rmdir(path)==0);assert(rmdir(root)==0);
png_texture_tests();effect_package_tests();
puts("Graphics AOT: shared 3D/raw/2D frame, ordered batching, deferred disposal and stress GC passed.");return 0;}
