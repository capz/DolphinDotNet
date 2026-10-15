#include "dnd_audio.h"
#include <aesndlib.h>
#include <gccore.h>
#include <string.h>

typedef struct {
    AESNDPB *pb;
    volatile int stopped;
    unsigned rate;
} BackendVoice;
static BackendVoice slots[32];
static int ready;
/* AESND invokes this from the DSP callback: set a flag only, no I/O or free. */
static void voice_callback(AESNDPB *pb,u32 state) {
    BackendVoice *v=(BackendVoice *)AESND_GetVoiceUserData(pb);
    if(v && state==VOICE_STATE_STOPPED) v->stopped=1;
}
void dnd_audio_backend_init(void) {
    if(ready)return;
    AESND_Init();
    AESND_Pause(false);
    ready=1;
}
void dnd_audio_backend_stop(unsigned slot) {
    if(slot>=32)return;
    BackendVoice *v=&slots[slot];
    if(v->pb) {
        AESND_SetVoiceStop(v->pb,true);
        AESND_RegisterVoiceCallback(v->pb,NULL);
        AESND_FreeVoice(v->pb);
    }
    memset(v,0,sizeof(*v));
}
int dnd_audio_backend_play(unsigned slot,const void *data,unsigned size,
                           unsigned format,unsigned rate,bool loop,
                           float volume,float pitch) {
    if(slot>=32 || !ready || !data || !size || slots[slot].pb)return 0;
    AESNDPB *pb=AESND_AllocateVoice(voice_callback);
    if(!pb)return 0;
    BackendVoice *v=&slots[slot];v->pb=pb;v->stopped=0;v->rate=rate;
    AESND_SetVoiceUserData(pb,v);
    DCFlushRange((void *)data,(size+31u)&~31u);
    AESND_PlayVoice(pb,format,data,size,(unsigned)(rate*pitch),0,loop);
    unsigned level=(unsigned)(volume*256.0f+0.5f);
    AESND_SetVoiceVolume(pb,level,level);
    return 1;
}
int dnd_audio_backend_playing(unsigned slot) {
    return slot<32 && slots[slot].pb && !slots[slot].stopped;
}
void dnd_audio_backend_control(unsigned slot,int kind,float value,
                               unsigned rate) {
    if(slot>=32 || !slots[slot].pb)return;
    if(kind==0) {
        unsigned level=(unsigned)(value*256.0f+0.5f);
        AESND_SetVoiceVolume(slots[slot].pb,level,level);
    } else if(kind==1) AESND_SetVoiceFrequency(slots[slot].pb,(unsigned)(rate*value));
}
void dnd_audio_backend_shutdown(void) {
    if(!ready)return;
    for(unsigned i=0;i<32;i++)dnd_audio_backend_stop(i);
    AESND_Reset();
    ready=0;
}
