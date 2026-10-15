/* Standalone host test: deterministic storage and DSP fake. */
#include "dnd_audio.h"
#include "dnd_storage.h"
#include <assert.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static uint8_t file_data[256];
static unsigned file_size, cursor;
static int open_file, closed, initialized, shutdowns, stopped;
static int active[32], play_count;
static unsigned last_format, last_rate, last_size;
static int last_loop;
static float last_volume,last_pitch;
static uint8_t first_sample[4];
static void u16(unsigned off,unsigned value) {file_data[off]=(uint8_t)value;file_data[off+1]=(uint8_t)(value>>8);}
static void u32(unsigned off,unsigned value) {for(unsigned i=0;i<4;i++)file_data[off+i]=(uint8_t)(value>>(8*i));}
static void valid_wav(void) {
    memset(file_data,0,sizeof(file_data));
    memcpy(file_data,"RIFF",4);memcpy(file_data+8,"WAVEfmt ",8);
    u32(4,40);u32(16,16);u16(20,1);u16(22,2);u32(24,22050);
    u32(28,88200);u16(32,4);u16(34,16);
    memcpy(file_data+36,"data",4);u32(40,4);
    file_data[44]=0x34;file_data[45]=0x12;file_data[46]=0xcd;file_data[47]=0xab;
    file_size=48;open_file=0;cursor=0;closed=0;
}
int dnd_fs_open(DndString *path,int mode,int access,int share) {
    assert(path && mode==3 && access==1 && share==1);
    open_file++;cursor=0;return 11;
}
int64_t dnd_fs_length_native(int handle){assert(handle==11);return file_size;}
int dnd_fs_read_native(int handle,void *out,int size) {
    assert(handle==11);
    if(cursor==file_size)return 0;
    if(size>3)size=3; /* Exercise partial reads in header and payload. */
    if((unsigned)size>file_size-cursor)size=(int)(file_size-cursor);
    memcpy(out,file_data+cursor,(size_t)size);cursor+=(unsigned)size;return size;
}
int64_t dnd_fs_seek(int handle,int64_t offset,int origin) {
    assert(handle==11 && origin==0);
    if(offset<0 || (uint64_t)offset>file_size)return -1;
    cursor=(unsigned)offset;return offset;
}
void dnd_fs_close(int handle){assert(handle==11);closed++;}
uint32_t dnd_array_length(DndArray *array){return array->length;}
uint64_t dnd_array_load_scalar(DndArray *array,uint32_t index,uint32_t size,bool signed_value){
    (void)signed_value;
    assert(array && index<array->length && size==1 && array->element_size==1);
    return array->data[index];
}
void dnd_audio_backend_init(void){initialized++;}
void dnd_audio_backend_shutdown(void){shutdowns++;}
int dnd_audio_backend_play(unsigned slot,const void *data,unsigned size,
                           unsigned format,unsigned rate,bool loop,float volume,float pitch) {
    assert(slot<32 && !active[slot]);active[slot]=1;play_count++;
    last_format=format;last_rate=rate;last_size=size;last_loop=loop;last_volume=volume;last_pitch=pitch;
    memcpy(first_sample,data,4);return 1;
}
int dnd_audio_backend_playing(unsigned slot){return slot<32 && active[slot];}
void dnd_audio_backend_stop(unsigned slot){assert(slot<32);active[slot]=0;stopped++;}
void dnd_audio_backend_control(unsigned slot,int kind,float value,unsigned rate){
    assert(active[slot] && rate==22050);
    if(kind==0)last_volume=value;else last_pitch=value;
}
int main(void) {
    valid_wav();
    assert(dnd_audio_init()==1 && dnd_audio_init()==1 && initialized==1);
    int clip=dnd_audio_begin((DndString *)1);
    assert(clip>0 && dnd_audio_stat(clip,0)==0);
    assert(dnd_audio_step(clip)==0);
    while(dnd_audio_step(clip)==0){}
    assert(closed==1 && dnd_audio_stat(clip,0)==1 &&
           dnd_audio_stat(clip,1)==4 && dnd_audio_stat(clip,2)==22050);
    int one=dnd_audio_play(clip,0,0.5f,1);
    int two=dnd_audio_play(clip,1,1,1.5f);
    assert(one>0 && two>one && play_count==2 && last_loop==1 &&
           last_format==3 && last_rate==22050 && last_size==4);
    assert(first_sample[0]==0x12 && first_sample[1]==0x34 &&
           first_sample[2]==0xab && first_sample[3]==0xcd);
    assert(dnd_audio_voice_stat(one)==1 && dnd_audio_voice_control(one,0,0.3f)==0 &&
           fabsf(last_volume-0.3f)<0.001f);
    assert(dnd_audio_voice_control(two,1,2.f)==0 && last_pitch==2.f);
    assert(dnd_audio_voice_control(two,1,4.f)<0 &&
           dnd_audio_play(clip,0,NAN,1)==0);
    assert(dnd_audio_voice_stop(one)==0 && dnd_audio_voice_stat(one)==0);
    assert(dnd_audio_voice_stop(one)==0);
    assert(dnd_audio_dispose(clip)==0 && dnd_audio_voice_stat(two)==0);
    assert(dnd_audio_dispose(clip)<0 && closed==1 && stopped>=2);
    valid_wav();
    u16(20,3); /* IEEE float WAV is deliberately unsupported. */
    assert(dnd_audio_begin((DndString *)1)==0 && closed==1);
    valid_wav();
    u32(40,100); /* Data chunk extends beyond RIFF. */
    assert(dnd_audio_begin((DndString *)1)==0 && closed==1);
    valid_wav();
    clip=dnd_audio_begin((DndString *)1);
    assert(clip>0 && dnd_audio_dispose(clip)==0 && closed==1);
    /* New PCM8 factory must copy the managed array before returning. */
    DndArray *samples=(DndArray *)malloc(sizeof(DndArray)+64);
    assert(samples!=NULL);
    memset(samples,0,sizeof(DndArray)+64);
    samples->length=64;samples->element_size=1;
    for(unsigned i=0;i<64;i++)samples->data[i]=(uint8_t)i;
    int pcm=dnd_audio_create_pcm8(samples,22050);
    assert(pcm>0 && dnd_audio_stat(pcm,0)==1 &&
           dnd_audio_stat(pcm,1)==64 && dnd_audio_stat(pcm,2)==22050);
    int pcm_voice=dnd_audio_play(pcm,0,0.75f,1.f);
    assert(pcm_voice>0 && last_format==4 && last_rate==22050 &&
           last_size==64 && first_sample[0]==0 && first_sample[3]==3);
    samples->data[0]=255; /* Audio must retain its own immutable native copy. */
    assert(dnd_audio_voice_stop(pcm_voice)==0);
    assert(dnd_audio_play(pcm,0,1.f,1.f)>0 && first_sample[0]==0);
    assert(dnd_audio_dispose(pcm)==0);
    samples->element_size=2;
    assert(dnd_audio_create_pcm8(samples,22050)==0);
    samples->element_size=1;
    assert(dnd_audio_create_pcm8(samples,4000)==0);
    free(samples);
    dnd_audio_shutdown();
    assert(shutdowns==1);
    puts("audio native tests passed");
    return 0;
}
