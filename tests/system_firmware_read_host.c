#define _POSIX_C_SOURCE 200809L
#include "dnd_managed.h"
#include "dnd_storage.h"
#include "dnd_draw.h"
#include "dnd_audio.h"
#include "dnd_audio_codec.h"
#include "dnd_model.h"
#include <assert.h>
#include "dnd_system_firmware.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned calls;
static uint8_t rom[0x200000];
int dnd_firmware_read(DndArray *destination,int offset,int count)
{
    unsigned read=calls++;
    if(read>=1024)return -1; /* Then exercise inaccessible-source fallback. */
    if(!destination||count!=4096||offset<0||offset>0x200000-count||destination->length<(unsigned)count)return -3;
    if(read<512)memcpy(destination->data,rom+offset,(size_t)count);
    else memset(destination->data,0,(size_t)count); /* Invalid console firmware -> file fallback. */
    return count;
}
void dnd_console_write_line(const char *text){puts(text);}
static int active_audio[32];
void dnd_audio_backend_init(void){}
void dnd_audio_backend_shutdown(void){}
int dnd_audio_backend_play(unsigned slot,const void *data,unsigned size,unsigned format,unsigned rate,bool loop,float volume,float pitch)
{(void)slot;(void)data;(void)size;(void)format;(void)rate;(void)loop;(void)volume;(void)pitch;assert(0);return 0;}
int dnd_audio_backend_play_encoded(unsigned slot,const DndAudioSource *source,bool loop,unsigned start,unsigned end,float volume,float pitch)
{
    assert(slot<32&&source->channels==2&&source->rate==32000&&source->samples==174067);
    assert(volume==1&&pitch==1);
    DndAudioDecoder decoder;dnd_audio_decoder_init(&decoder,source,loop);
    uint8_t pcm[4];assert(dnd_audio_decode(&decoder,pcm,1)==1);
    assert(pcm[0]==0&&pcm[1]==1&&pcm[2]==255&&pcm[3]==254);
    assert(start<end&&end<=source->samples);active_audio[slot]=1;return 1;
}
int dnd_audio_backend_playing(unsigned slot){assert(slot<32);return active_audio[slot];}
void dnd_audio_backend_control(unsigned slot,int kind,float value,unsigned rate){(void)slot;(void)kind;(void)value;(void)rate;}
void dnd_audio_backend_stop(unsigned slot){assert(slot<32);active_audio[slot]=0;}
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
    if(result||dnd_exception_pending()||dnd_fs_open_handles()||dnd_draw_live()||dnd_model_live_handles()){
        fprintf(stderr,"Firmware read stage=%ld error=%s handles=%d\n",(long)result,dnd_exception_message(),dnd_fs_open_handles());
        if(dnd_exception_pending()){
            DndObject *error=dnd_exception_object();
            if(error&&dnd_type_is_assignable_from(&DND_TYPE_EXCEPTION,error->type)){
                DndString *message=dnd_exception_get_message((DndException *)error);
                if(message){fputs("Managed detail: ",stderr);for(uint32_t i=0;i<message->length;i++)fputc(message->chars[i]<128?message->chars[i]:'?',stderr);fputc('\n',stderr);}
            }
        }
        return 1;
    }
    for(unsigned slot=0;slot<32;slot++)assert(!active_audio[slot]);
    for(int clip=1;clip<=3;clip++)assert(dnd_audio_stat(clip,1)==-1);
    dnd_audio_shutdown();
    printf("Firmware console/file AOT passed; reads=%u collections=%zu heap=%zu\n",calls,heap.collections,heap.used);
    return 0;
}
