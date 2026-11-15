#ifndef IPL_TEST_AESND_H
#define IPL_TEST_AESND_H
#include <stdbool.h>
#include <stdint.h>
typedef uint32_t u32;
typedef struct {int unused;void *user;} AESNDPB;
typedef void (*AESNDVoiceCallback)(AESNDPB *,u32);
#define VOICE_STATE_STREAM 2
#define VOICE_STEREO16 3
#define DSP_DEFAULT_FREQ (54000000.0f/1124.0f)
AESNDPB *AESND_AllocateVoice(AESNDVoiceCallback);
AESNDVoiceCallback AESND_RegisterVoiceCallback(AESNDPB *,AESNDVoiceCallback);
void AESND_SetVoiceBuffer(AESNDPB *,const void *,u32);
void AESND_SetVoiceFormat(AESNDPB *,u32);
void AESND_SetVoiceUserData(AESNDPB *,void *);
void *AESND_GetVoiceUserData(AESNDPB *);
void AESND_SetVoiceFrequencyRatio(AESNDPB *,float);
void AESND_SetVoiceVolume(AESNDPB *,unsigned,unsigned);
void AESND_SetVoiceStream(AESNDPB *,bool);
void AESND_SetVoiceStop(AESNDPB *,bool);
void AESND_FreeVoice(AESNDPB *);
#endif
