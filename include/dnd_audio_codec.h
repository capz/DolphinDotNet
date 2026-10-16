#ifndef DND_AUDIO_CODEC_H
#define DND_AUDIO_CODEC_H
#include <stdbool.h>
#include <stdint.h>
/* Immutable native data. No managed references, file handles or callback I/O.
 */
typedef struct {
  unsigned first, samples, offset, interleave, stride, channel[2];
  int history[2][2];
  unsigned reset_history;
} DndAudioSpan;
typedef struct {
  const uint8_t *data;
  unsigned size, codec, channels, rate, samples, loop_start, loop_end, has_loop;
  unsigned span_count;
  DndAudioSpan *span;
  int coefficients[2][16], history[2][2];
  unsigned adx_version, adx_type;
} DndAudioSource;
typedef struct {
  unsigned position, span, cursor, cached;
  int history[2][2];
  int16_t frame[2][32];
} DndAudioDecodeState;
typedef struct {
  const DndAudioSource *source;
  DndAudioDecodeState state, loop_state;
  unsigned loop, loop_start, loop_end, saved, failed;
} DndAudioDecoder;
void dnd_audio_decoder_init(DndAudioDecoder *decoder,
                            const DndAudioSource *source, bool loop);
/* Returns frames produced, with interleaved PCM16 in big-endian byte order. */
unsigned dnd_audio_decode(DndAudioDecoder *decoder, uint8_t *output,
                          unsigned frames);
int dnd_audio_backend_play_encoded(unsigned slot, const DndAudioSource *source,
                                   bool loop, unsigned loop_start,
                                   unsigned loop_end, float volume,
                                   float pitch);
#endif
