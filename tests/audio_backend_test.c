#include "dnd_audio.h"
#include "dnd_audio_codec.h"
#include <aesndlib.h>
#include <assert.h>
#include <stdio.h>
#include <string.h>
static AESNDPB voices[32];
static unsigned created, freed, flushes;
void AESND_Init(void) {}
void AESND_Reset(void) {}
void AESND_Pause(bool value) { (void)value; }
AESNDPB *AESND_AllocateVoice(AESNDVoiceCallback callback) {
  for (unsigned i = 0; i < 32; i++)
    if (!voices[i].allocated) {
      memset(&voices[i], 0, sizeof(voices[i]));
      voices[i].allocated = true;
      voices[i].stopped = true;
      voices[i].callback = callback;
      created++;
      return &voices[i];
    }
  return NULL;
}
void AESND_FreeVoice(AESNDPB *v) {
  assert(v->allocated && v->stopped && !v->callback);
  v->allocated = false;
  freed++;
}
void AESND_SetVoiceUserData(AESNDPB *v, void *user) { v->user = user; }
void *AESND_GetVoiceUserData(AESNDPB *v) { return v->user; }
void AESND_SetVoiceStop(AESNDPB *v, bool stop) { v->stopped = stop; }
void AESND_RegisterVoiceCallback(AESNDPB *v, AESNDVoiceCallback cb) {
  v->callback = cb;
}
void AESND_PlayVoice(AESNDPB *v, u32 format, const void *data, u32 size,
                     u32 rate, u32 delay, bool loop) {
  (void)delay;
  (void)loop;
  v->format = format;
  v->buffer = data;
  v->length = size;
  v->rate = rate;
  v->stopped = false;
}
void AESND_SetVoiceFormat(AESNDPB *v, u32 format) { v->format = format; }
void AESND_SetVoiceFrequency(AESNDPB *v, u32 rate) { v->rate = rate; }
void AESND_SetVoiceBuffer(AESNDPB *v, const void *data, u32 size) {
  assert(size && size <= 1152);
  v->buffer = data;
  v->length = size;
}
void AESND_SetVoiceVolume(AESNDPB *v, u16 a, u16 b) {
  assert(a == b);
  v->volume = a;
}
void AESND_SetVoiceStream(AESNDPB *v, bool stream) { v->stream = stream; }
void DCFlushRange(void *p, u32 n) {
  assert(p && ((uintptr_t)p % 32) == 0 && n && n % 32 == 0);
  flushes++;
}
/* The codec's sample accuracy is tested separately; this fake isolates
 * DMA-buffer lifecycle. */
void dnd_audio_decoder_init(DndAudioDecoder *d, const DndAudioSource *s,
                            bool loop) {
  memset(d, 0, sizeof(*d));
  d->source = s;
  d->loop = loop;
  d->loop_start = s->loop_start;
  d->loop_end = s->loop_end;
}
unsigned dnd_audio_decode(DndAudioDecoder *d, uint8_t *out, unsigned count) {
  unsigned got = 0;
  while (got < count) {
    if (d->loop && d->state.position == d->loop_end)
      d->state.position = d->loop_start;
    if (d->state.position == d->source->samples)
      break;
    for (unsigned c = 0; c < d->source->channels; c++) {
      *out++ = 0;
      *out++ = (uint8_t)d->state.position;
    }
    got++;
    d->state.position++;
  }
  return got;
}
static void request(unsigned slot) {
  AESNDPB *v = &voices[slot];
  assert(v->allocated && !v->stopped && v->stream && v->callback);
  v->callback(v, VOICE_STATE_STREAM);
}
int main(void) {
  dnd_audio_backend_init();
  DndAudioSource s = {
      .channels = 2, .rate = 22050, .samples = 700, .loop_end = 700};
  assert(!dnd_audio_backend_play_encoded(0, &s, true, 20, 20, 1, 1));
  assert(!dnd_audio_backend_play_encoded(0, &s, true, 0, 701, 1, 1));
  assert(!created);
  assert(dnd_audio_backend_play_encoded(0, &s, false, 0, 700, .5f, 1));
  assert(voices[0].length == 1152 && voices[0].format == 3 &&
         voices[0].rate == 22050 && voices[0].volume == 128);
  const uint8_t *p = voices[0].buffer;
  assert(p[1] == 0);
  request(0);
  assert(p[1] == (uint8_t)288 && voices[0].length == 1152);
  request(0);
  assert(p[1] == (uint8_t)576 && voices[0].length == (700 - 576) * 4);
  request(0);
  assert(voices[0].length == 1152 && !voices[0].stopped && p[1] == 0);
  request(0);
  assert(!voices[0].stopped);
  request(0);
  assert(voices[0].stopped && !dnd_audio_backend_playing(0));
  dnd_audio_backend_stop(0);
  assert(freed == 1);
  assert(dnd_audio_backend_play_encoded(0, &s, true, 3, 35, 1, 1.5f));
  assert(dnd_audio_backend_play_encoded(1, &s, true, 17, 500, 1, 1));
  p = voices[0].buffer;
  for (unsigned i = 0; i < 288; i++)
    assert(p[i * 4 + 1] == (uint8_t)(i < 35 ? i : 3 + (i - 35) % 32));
  request(0);
  for (unsigned i = 0; i < 288; i++)
    assert(p[i * 4 + 1] == (uint8_t)(3 + (288 + i - 35) % 32));
  assert(((const uint8_t *)voices[1].buffer)[1] == 0);
  for (unsigned i = 0; i < 20; i++)
    request(0);
  assert(!voices[0].stopped);
  dnd_audio_backend_control(0, 1, 2, 22050);
  assert(voices[0].rate == 44100);
  dnd_audio_backend_shutdown();
  assert(created == freed && flushes > 20);
  puts("AESND bounded callback buffers, tail draining, independent voices and "
       "cleanup passed");
  return 0;
}
