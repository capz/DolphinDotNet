#include "dnd_audio.h"
#include "dnd_audio_codec.h"
#include <aesndlib.h>
#include <gccore.h>
#include <malloc.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
  AESNDPB *pb;
  volatile int stopped;
  unsigned rate;
  DndAudioDecoder decoder;
  uint8_t *decoded;
  unsigned channels, draining;
} BackendVoice;
static BackendVoice slots[32];
static int ready;
/* Callback work is bounded native decoding only: no I/O, allocation or managed
 * access. */
static void voice_callback(AESNDPB *pb, u32 state) {
  BackendVoice *v = (BackendVoice *)AESND_GetVoiceUserData(pb);
  if (!v)
    return;
  if (state == VOICE_STATE_STOPPED)
    v->stopped = 1;
  if (state == VOICE_STATE_STREAM && v->decoded) {
    unsigned frames =
        dnd_audio_decode(&v->decoder, v->decoded, 576 / v->channels);
    if (!frames) {
      /* Let AESND's two input buffers drain before ending the voice. */
      if (++v->draining > 2) {
        AESND_SetVoiceStop(pb, true);
        v->stopped = 1;
        return;
      }
      memset(v->decoded, 0, 1152);
    }
    unsigned bytes = frames ? frames * v->channels * 2 : 1152;
    DCFlushRange(v->decoded, (bytes + 31u) & ~31u);
    AESND_SetVoiceBuffer(pb, v->decoded, bytes);
  }
}
void dnd_audio_backend_init(void) {
  if (ready)
    return;
  AESND_Init();
  AESND_Pause(false);
  ready = 1;
}
void dnd_audio_backend_stop(unsigned slot) {
  if (slot >= 32)
    return;
  BackendVoice *v = &slots[slot];
  if (v->pb) {
    AESND_SetVoiceStop(v->pb, true);
    AESND_RegisterVoiceCallback(v->pb, NULL);
    AESND_FreeVoice(v->pb);
  }
  free(v->decoded);
  memset(v, 0, sizeof(*v));
}
int dnd_audio_backend_play(unsigned slot, const void *data, unsigned size,
                           unsigned format, unsigned rate, bool loop,
                           float volume, float pitch) {
  if (slot >= 32 || !ready || !data || !size || slots[slot].pb)
    return 0;
  AESNDPB *pb = AESND_AllocateVoice(voice_callback);
  if (!pb)
    return 0;
  BackendVoice *v = &slots[slot];
  v->pb = pb;
  v->stopped = 0;
  v->rate = rate;
  AESND_SetVoiceUserData(pb, v);
  DCFlushRange((void *)data, (size + 31u) & ~31u);
  AESND_PlayVoice(pb, format, data, size, (unsigned)(rate * pitch), 0, loop);
  unsigned level = (unsigned)(volume * 256.0f + 0.5f);
  AESND_SetVoiceVolume(pb, level, level);
  return 1;
}
int dnd_audio_backend_play_encoded(unsigned slot, const DndAudioSource *source,
                                   bool loop, unsigned loop_start,
                                   unsigned loop_end, float volume,
                                   float pitch) {
  if (slot >= 32 || !ready || !source || slots[slot].pb ||
      (loop && (loop_start >= loop_end || loop_end > source->samples)))
    return 0;
  uint8_t *decoded = memalign(32, 1152);
  if (!decoded)
    return 0;
  AESNDPB *pb = AESND_AllocateVoice(voice_callback);
  if (!pb) {
    free(decoded);
    return 0;
  }
  BackendVoice *v = &slots[slot];
  v->pb = pb;
  v->decoded = decoded;
  v->rate = source->rate;
  v->channels = source->channels;
  dnd_audio_decoder_init(&v->decoder, source, loop);
  v->decoder.loop_start = loop_start;
  v->decoder.loop_end = loop_end;
  unsigned frames = dnd_audio_decode(&v->decoder, decoded, 576 / v->channels);
  if (!frames) {
    dnd_audio_backend_stop(slot);
    return 0;
  }
  AESND_SetVoiceUserData(pb, v);
  AESND_SetVoiceFormat(pb,
                       source->channels == 2 ? VOICE_STEREO16 : VOICE_MONO16);
  AESND_SetVoiceFrequency(pb, (unsigned)(source->rate * pitch));
  unsigned bytes = frames * source->channels * 2;
  DCFlushRange(decoded, (bytes + 31u) & ~31u);
  AESND_SetVoiceBuffer(pb, decoded, bytes);
  unsigned level = (unsigned)(volume * 256.f + .5f);
  AESND_SetVoiceVolume(pb, level, level);
  AESND_SetVoiceStream(pb, true);
  AESND_SetVoiceStop(pb, false);
  return 1;
}
int dnd_audio_backend_playing(unsigned slot) {
  return slot < 32 && slots[slot].pb && !slots[slot].stopped;
}
void dnd_audio_backend_control(unsigned slot, int kind, float value,
                               unsigned rate) {
  if (slot >= 32 || !slots[slot].pb)
    return;
  if (kind == 0) {
    unsigned level = (unsigned)(value * 256.0f + 0.5f);
    AESND_SetVoiceVolume(slots[slot].pb, level, level);
  } else if (kind == 1)
    AESND_SetVoiceFrequency(slots[slot].pb, (unsigned)(rate * value));
}
void dnd_audio_backend_shutdown(void) {
  if (!ready)
    return;
  for (unsigned i = 0; i < 32; i++)
    dnd_audio_backend_stop(i);
  AESND_Reset();
  ready = 0;
}
