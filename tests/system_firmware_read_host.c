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
static uint32_t fw_registers[5];
static unsigned fw_page,fw_cursor,fw_locked,fw_selected,fw_valid;
static uint64_t fw_clock;
/* Exercise the production EXI driver, including the physical ROM's 1 KiB
 * burst window, rather than replacing the entire firmware ABI with memcpy. */
int dnd_fw_lock(void)
{
    unsigned read=calls++;
    if(read>=1024)return 0; /* Then exercise inaccessible-source fallback. */
    assert(!fw_locked);fw_locked=1;fw_valid=read<512;return 1;
}
int dnd_fw_select(void){assert(fw_locked&&!fw_selected);fw_selected=1;return 1;}
int dnd_fw_deselect(void){assert(fw_selected);fw_selected=0;return 1;}
void dnd_fw_unlock(void){assert(fw_locked&&!fw_selected);fw_locked=0;}
uint64_t dnd_fw_now(void){return ++fw_clock;}
uint32_t dnd_fw_read(unsigned reg){return fw_registers[reg];}
void dnd_fw_write(unsigned reg,uint32_t value)
{
    assert(fw_locked&&fw_selected);fw_registers[reg]=value;
    if(reg!=3||!(value&1))return;
    if(value&4){unsigned address=fw_registers[4]>>6;fw_page=address&~1023u;fw_cursor=address&1023u;}
    else {
        unsigned bytes=((value>>4)&3)+1;fw_registers[4]=0;
        for(unsigned i=0;i<bytes;i++){
            unsigned address=fw_page+(fw_cursor++&1023u);assert(address<sizeof(rom));
            fw_registers[4]|=(uint32_t)(fw_valid?rom[address]:0)<<(24-i*8);
        }
    }
    fw_registers[3]=0;
}
void dnd_console_write_line(const char *text){puts(text);}
static int active_audio[32];
void dnd_audio_backend_init(void){}
void dnd_audio_backend_shutdown(void){}
int dnd_audio_backend_play(unsigned slot,const void *data,unsigned size,unsigned format,unsigned rate,bool loop,float volume,float pitch)
{assert(slot<32&&data&&size==6560*4&&format==3&&rate==32000&&!loop&&volume==1&&pitch==1);active_audio[slot]=1;return 1;}
int dnd_audio_backend_play_encoded(unsigned slot,const DndAudioSource *source,bool loop,unsigned start,unsigned end,float volume,float pitch)
{
    assert(slot<32&&source->rate==32000);
    assert(volume==1&&pitch==1);
    DndAudioDecoder decoder;dnd_audio_decoder_init(&decoder,source,loop);
    uint8_t pcm[4];assert(dnd_audio_decode(&decoder,pcm,1)==1);
    if(source->channels==1){assert(source->samples==16&&pcm[0]==0&&pcm[1]==0);}
    else {assert(source->channels==2&&source->samples==174067);assert(pcm[0]==0&&pcm[1]==1&&pcm[2]==255&&pcm[3]==254);}
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
    for(int clip=1;clip<=32;clip++)assert(dnd_audio_stat(clip,1)==-1);
    dnd_audio_shutdown();
    printf("Firmware console/file AOT passed; reads=%u collections=%zu heap=%zu\n",calls,heap.collections,heap.used);
    return 0;
}
