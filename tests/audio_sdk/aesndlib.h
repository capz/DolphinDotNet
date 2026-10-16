#ifndef DND_TEST_AESND_H
#define DND_TEST_AESND_H
#include <stdbool.h>
#include <stdint.h>
typedef uint32_t u32;
typedef uint16_t u16;
typedef struct AESNDPB AESNDPB;
typedef void (*AESNDVoiceCallback)(AESNDPB *, u32);
#define VOICE_STATE_STOPPED 0
#define VOICE_STATE_RUNNING 1
#define VOICE_STATE_STREAM 2
#define VOICE_MONO16 2
#define VOICE_STEREO16 3
struct AESNDPB {
  bool allocated, stopped, stream;
  AESNDVoiceCallback callback;
  void *user;
  const void *buffer;
  unsigned length, format, rate, volume;
};
void AESND_Init(void);
void AESND_Reset(void);
void AESND_Pause(bool);
AESNDPB *AESND_AllocateVoice(AESNDVoiceCallback);
void AESND_FreeVoice(AESNDPB *);
void AESND_SetVoiceUserData(AESNDPB *, void *);
void *AESND_GetVoiceUserData(AESNDPB *);
void AESND_SetVoiceStop(AESNDPB *, bool);
void AESND_RegisterVoiceCallback(AESNDPB *, AESNDVoiceCallback);
void AESND_PlayVoice(AESNDPB *, u32, const void *, u32, u32, u32, bool);
void AESND_SetVoiceFormat(AESNDPB *, u32);
void AESND_SetVoiceFrequency(AESNDPB *, u32);
void AESND_SetVoiceBuffer(AESNDPB *, const void *, u32);
void AESND_SetVoiceVolume(AESNDPB *, u16, u16);
void AESND_SetVoiceStream(AESNDPB *, bool);
#endif
