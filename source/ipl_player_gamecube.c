#include "dnd_ipl_player.h"
#include "dnd_audio.h"
#include <aesndlib.h>
#include <gccore.h>
#include <string.h>
static AESNDPB *voice;
static void *owner;
static DndIplPull consume;
static _Alignas(32) uint8_t output[1280];
static void callback(AESNDPB *pb,u32 state) {
  if(state!=VOICE_STATE_STREAM||!consume)return;
  consume(owner,output);DCFlushRange(output,sizeof(output));
  AESND_SetVoiceBuffer(pb,output,sizeof(output));
}
bool dnd_ipl_backend_start(void *context,DndIplPull pull) {
  if(voice||!context||!pull)return false;
  dnd_audio_init();voice=AESND_AllocateVoice(callback);if(!voice)return false;
  owner=context;consume=pull;
  AESND_SetVoiceFormat(voice,VOICE_STEREO16);AESND_SetVoiceFrequencyRatio(voice,32028.5f/DSP_DEFAULT_FREQ);
  AESND_SetVoiceVolume(voice,256,256);pull(context,output);DCFlushRange(output,sizeof(output));
  AESND_SetVoiceBuffer(voice,output,sizeof(output));AESND_SetVoiceStream(voice,true);AESND_SetVoiceStop(voice,false);return true;
}
void dnd_ipl_backend_stop(void *context) {
  if(!voice||owner!=context)return;
  /* Interrupt exclusion prevents a racing callback from using the native
   * session after teardown. No managed object is ever a callback context. */
  u32 level=IRQ_Disable();AESND_SetVoiceStop(voice,true);
  AESND_RegisterVoiceCallback(voice,NULL);AESND_FreeVoice(voice);
  voice=NULL;owner=NULL;consume=NULL;memset(output,0,sizeof(output));IRQ_Restore(level);
}
