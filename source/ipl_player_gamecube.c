#include "dnd_ipl_player.h"
#include "dnd_audio.h"
#include <aesndlib.h>
#include <gccore.h>
#include <string.h>
static AESNDPB *voice;
static uintptr_t next_ticket=1,ticket;
static void *owner;
static DndIplPull consume;
static _Alignas(32) uint8_t output[2304];
static void callback(AESNDPB *pb,u32 state) {
  if(state!=VOICE_STATE_STREAM||!consume||(uintptr_t)AESND_GetVoiceUserData(pb)!=ticket)return;
  consume(owner,output,288);DCFlushRange(output,1152);
  AESND_SetVoiceBuffer(pb,output,1152);
}
bool dnd_ipl_backend_start(void *context,DndIplPull pull) {
  if(voice||!context||!pull||!next_ticket)return false;
  dnd_audio_init();voice=AESND_AllocateVoice(callback);if(!voice)return false;
  ticket=next_ticket++;AESND_SetVoiceUserData(voice,(void *)ticket);owner=context;consume=pull;
  AESND_SetVoiceFormat(voice,VOICE_STEREO16);AESND_SetVoiceFrequencyRatio(voice,32028.5f/DSP_DEFAULT_FREQ);
  AESND_SetVoiceVolume(voice,256,256);pull(context,output,288);pull(context,output+1152,288);DCFlushRange(output,sizeof(output));
  AESND_SetVoiceBuffer(voice,output,sizeof(output));AESND_SetVoiceStream(voice,true);AESND_SetVoiceStop(voice,false);return true;
}
void dnd_ipl_backend_stop(void *context) {
  if(!voice||owner!=context)return;
  /* Interrupt exclusion prevents a racing callback from using the native
   * session after teardown. No managed object is ever a callback context. */
  u32 level=IRQ_Disable();AESND_SetVoiceStop(voice,true);
  AESND_RegisterVoiceCallback(voice,NULL);AESND_FreeVoice(voice);
  voice=NULL;owner=NULL;consume=NULL;ticket=0;memset(output,0,sizeof(output));IRQ_Restore(level);
}
