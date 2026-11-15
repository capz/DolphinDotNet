#include "dnd_ipl_player.h"
#include <aesndlib.h>
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
static AESNDPB voice;
void AESND_SetVoiceUserData(AESNDPB *v,void *p){v->user=p;}
void *AESND_GetVoiceUserData(AESNDPB *v){return v->user;}
static AESNDVoiceCallback callback;
static unsigned supplied,consumed,buffers,muted,enabled,locked;
int dnd_audio_init(void){return 1;}
AESNDPB *AESND_AllocateVoice(AESNDVoiceCallback cb){assert(!callback);callback=cb;return &voice;}
AESNDVoiceCallback AESND_RegisterVoiceCallback(AESNDPB *v,AESNDVoiceCallback cb){assert(v==&voice);AESNDVoiceCallback old=callback;callback=cb;return old;}
void AESND_SetVoiceFormat(AESNDPB *v,u32 f){assert(v==&voice&&f==VOICE_STEREO16);}
void AESND_SetVoiceFrequencyRatio(AESNDPB *v,float f){assert(v==&voice&&f>.66f&&f<.67f);}
void AESND_SetVoiceVolume(AESNDPB *v,unsigned l,unsigned r){assert(v==&voice&&l==256&&r==256);}
void AESND_SetVoiceStream(AESNDPB *v,bool s){assert(v==&voice&&s);enabled++;}
void AESND_SetVoiceStop(AESNDPB *v,bool s){assert(v==&voice);if(s){assert(locked);muted++;}}
void AESND_FreeVoice(AESNDPB *v){assert(v==&voice&&locked&&!callback);}
void DCFlushRange(void *p,u32 bytes){assert(((uintptr_t)p&31)==0&&bytes%32==0);}
u32 IRQ_Disable(void){assert(!locked);locked=1;return 123;}
void IRQ_Restore(u32 before){assert(locked&&before==123);locked=0;}
void AESND_SetVoiceBuffer(AESNDPB *v,const void *p,u32 bytes) {
  assert(v==&voice&&p);assert(bytes==1152u||bytes==2304u);
  const uint8_t *data=p;
  for(unsigned i=0;i<bytes;i++,consumed++)assert(data[i]==(uint8_t)consumed);
  buffers++;
}
static void pull(void *context,uint8_t *out,unsigned frames) {
  assert(context==&supplied&&frames==288);
  for(unsigned i=0;i<frames*4;i++,supplied++)out[i]=(uint8_t)supplied;
}
int main(void) {
  assert(dnd_ipl_backend_start(&supplied,pull)&&enabled==1&&buffers==1&&supplied==2304);
  assert(!dnd_ipl_backend_start(&supplied,pull));
  for(unsigned i=0;i<1000;i++)callback(&voice,VOICE_STATE_STREAM);
  assert(supplied==consumed&&buffers==1001);
  AESNDPB retired=voice;AESNDVoiceCallback stale=callback;dnd_ipl_backend_stop(&supplied);
  unsigned before=supplied;stale(&voice,VOICE_STATE_STREAM);assert(supplied==before&&!callback&&muted==1);
  assert(dnd_ipl_backend_start(&supplied,pull));before=supplied;
  stale(&retired,VOICE_STATE_STREAM);assert(supplied==before);
  callback(&voice,VOICE_STATE_STREAM);assert(supplied==before+1152);
  dnd_ipl_backend_stop(&supplied);puts("IPL AESND backend: double-buffer priming, 288-frame pulls, continuous PCM and callback teardown passed");
}
