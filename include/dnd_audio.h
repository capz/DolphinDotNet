#ifndef DND_AUDIO_H
#define DND_AUDIO_H
#include "dnd_managed.h"
#include <stdbool.h>
#include <stdint.h>

/* Clips are kept in native RAM, never in movable managed arrays. Loading may
 * run on the background executor; playback requires the application executor.
 */
int dnd_audio_init(void);
int dnd_audio_begin(DndString *path);
int dnd_audio_begin_source(DndString *path, DndString *data_path, int format,
                             int entry, int budget);
/* Copy mono 8-bit unsigned PCM out of movable managed memory into aligned DSP
 * RAM. */
int dnd_audio_create_pcm8(DndArray *samples, int sample_rate);
int dnd_audio_step(int clip);
int dnd_audio_stat(int clip, int kind);
int dnd_audio_dispose(int clip);
int dnd_audio_play(int clip, int loop, float volume, float pitch);
/* GameCube source override: play the intro, then repeat [start, end) in sample
 * frames per channel. The shared source metadata is unchanged. */
int dnd_audio_play_range(int clip, int start, int end, float volume,
                         float pitch);
int dnd_audio_voice_stat(int voice);
int dnd_audio_voice_control(int voice, int kind, float value);
int dnd_audio_voice_stop(int voice);
void dnd_audio_shutdown(void);

/* The backend must not perform I/O from its DSP/interrupt callback. */
void dnd_audio_backend_init(void);
void dnd_audio_backend_shutdown(void);
int dnd_audio_backend_play(unsigned slot, const void *data, unsigned size,
                           unsigned format, unsigned rate, bool loop,
                           float volume, float pitch);
int dnd_audio_backend_playing(unsigned slot);
void dnd_audio_backend_control(unsigned slot, int kind, float value,
                               unsigned rate);
void dnd_audio_backend_stop(unsigned slot);
#endif
