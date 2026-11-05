#define _POSIX_C_SOURCE 200809L
#include "dnd_managed.h"
#include "dnd_storage.h"
#include "dnd_draw.h"
#include "dnd_system_firmware.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned calls;
static uint8_t rom[0x200000];
int dnd_firmware_read(DndArray *destination,int offset,int count)
{
    if(calls++>=512)return -1; /* Contention/unavailable path triggers file fallback. */
    if(!destination||count!=4096||offset<0||offset>0x200000-count||destination->length<(unsigned)count)return -3;
    memcpy(destination->data,rom+offset,(size_t)count);return count;
}
void dnd_console_write_line(const char *text){puts(text);}
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(int argc,char **argv)
{
    if(argc!=3)return 90;
    FILE *file=fopen(argv[2],"rb");if(!file)return 91;
    if(fread(rom,1,sizeof(rom),file)!=sizeof(rom))return 92;
    fclose(file);setenv("DND_STORAGE_ROOT",argv[1],1);
    static unsigned char memory[8*1024*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(true);
    intptr_t result=dnd_aot_entry(&heap);
    if(result||dnd_exception_pending()||dnd_fs_open_handles()||dnd_draw_live()){
        fprintf(stderr,"Firmware read stage=%ld error=%s handles=%d\n",(long)result,dnd_exception_message(),dnd_fs_open_handles());return 1;
    }
    printf("Firmware console/file AOT passed; reads=%u collections=%zu heap=%zu\n",calls,heap.collections,heap.used);
    return 0;
}
