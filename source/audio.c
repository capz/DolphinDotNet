#include "dnd_audio.h"
#include "dnd_audio_codec.h"
#include "dnd_storage.h"
#include <limits.h>
#include <malloc.h>
#include <math.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

enum { CLIPS = 16, VOICES = 32, MAX_PCM_BYTES = 16 * 1024 * 1024 };
typedef struct {
  int token, file, ready;
  unsigned length, position, format, rate;
  uint8_t *pcm;
  int encoded, auxiliary_file, input_index, parsed;
  unsigned input_size[2], input_position[2], requested_format, entry, budget;
  uint8_t *input[2];
  DndAudioSource source;
  DndAudioDecoder validation;
} Clip;
typedef struct {
  int token, clip;
  unsigned rate;
} Voice;
static Clip clips[CLIPS];
static Voice voices[VOICES];
static int next_clip = 1, next_voice = 1, initialized;

static uint16_t le16(const uint8_t *p) {
  return (uint16_t)(p[0] | ((uint16_t)p[1] << 8));
}
static uint32_t le32(const uint8_t *p) {
  return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) |
         ((uint32_t)p[3] << 24);
}
static Clip *find_clip(int token) {
  for (int i = 0; i < CLIPS; i++)
    if (token && clips[i].token == token)
      return &clips[i];
  return NULL;
}
static Voice *find_voice(int token) {
  for (int i = 0; i < VOICES; i++)
    if (token && voices[i].token == token)
      return &voices[i];
  return NULL;
}
static bool read_exact(int file, uint8_t *out, unsigned count) {
  unsigned done = 0;
  while (done < count) {
    int got = dnd_fs_read_native(file, out + done, (int)(count - done));
    if (got <= 0)
      return false;
    done += (unsigned)got;
  }
  return true;
}
static bool wav_header(int file, int64_t length, unsigned *offset,
                       unsigned *size, unsigned *format, unsigned *rate) {
  uint8_t header[12];
  if (length < 44 || !read_exact(file, header, 12) ||
      memcmp(header, "RIFF", 4) || memcmp(header + 8, "WAVE", 4) ||
      (uint64_t)le32(header + 4) + 8u > (uint64_t)length)
    return false;
  int64_t end = (int64_t)le32(header + 4) + 8;
  int has_fmt = 0;
  for (int64_t pos = 12; pos + 8 <= end;) {
    uint8_t chunk[8];
    if (dnd_fs_seek(file, pos, 0) != pos || !read_exact(file, chunk, 8))
      return false;
    uint32_t n = le32(chunk + 4);
    int64_t start = pos + 8, next = start + (int64_t)n + (n & 1u);
    if (next > end || next < start)
      return false;
    if (!memcmp(chunk, "fmt ", 4)) {
      uint8_t fmt[16];
      if (n < 16 || !read_exact(file, fmt, 16))
        return false;
      unsigned channels = le16(fmt + 2), bits = le16(fmt + 14),
               hz = le32(fmt + 4);
      unsigned block = channels * (bits / 8);
      if (le16(fmt) != 1 || (channels != 1 && channels != 2) ||
          (bits != 8 && bits != 16) || hz < 8000 || hz > 48000 ||
          le16(fmt + 12) != block || le32(fmt + 8) != hz * block)
        return false;
      *format = (channels == 1 ? (bits == 8 ? 4u : 2u) : (bits == 8 ? 5u : 3u));
      *rate = hz;
      has_fmt = 1;
    } else if (!memcmp(chunk, "data", 4) && has_fmt) {
      unsigned bytes_per_frame =
          (*format & 1u ? 2u : 1u) * ((*format & 2u) ? 2u : 1u);
      /* AESND expects even/whole frames and 32-byte-padded DMA memory. */
      if (n == 0 || n > MAX_PCM_BYTES || n % bytes_per_frame)
        return false;
      *offset = (unsigned)start;
      *size = n;
      return true;
    }
    pos = next;
  }
  return false;
}
#include "audio_formats.inc"

int dnd_audio_init(void) {
  if (!initialized) {
    dnd_audio_backend_init();
    initialized = 1;
  }
  return 1;
}
int dnd_audio_begin(DndString *path) {
  if (!path || next_clip == INT_MAX)
    return 0;
  dnd_audio_init();
  int slot = -1;
  for (int i = 0; i < CLIPS; i++)
    if (!clips[i].token) {
      slot = i;
      break;
    }
  if (slot < 0)
    return 0;
  int file = dnd_fs_open(path, 3, 1, 1);
  if (!file)
    return 0;
  int64_t total = dnd_fs_length_native(file);
  unsigned offset = 0, size = 0, format = 0, rate = 0;
  if (total < 44 || total > INT_MAX ||
      !wav_header(file, total, &offset, &size, &format, &rate) ||
      dnd_fs_seek(file, offset, 0) != (int64_t)offset) {
    dnd_fs_close(file);
    return 0;
  }
  size_t aligned = ((size_t)size + 31u) & ~(size_t)31u;
  uint8_t *pcm = memalign(32, aligned);
  if (!pcm) {
    dnd_fs_close(file);
    return 0;
  }
  memset(pcm, 0, aligned);
  int token = next_clip++;
  clips[slot] = (Clip){.token = token,
                       .file = file,
                       .length = size,
                       .format = format,
                       .rate = rate,
                       .pcm = pcm};
  return token;
}
int dnd_audio_begin_gamecube(DndString *path, DndString *body, int format,
                             int entry, int budget) {
  if (!path || format < 0 || format > 12 || entry < 0 || entry > 65535 ||
      budget < 1024 || budget > MAX_PCM_BYTES || next_clip == INT_MAX)
    return 0;
  dnd_audio_init();
  Clip *c = NULL;
  for (int i = 0; i < CLIPS; i++)
    if (!clips[i].token) {
      c = &clips[i];
      break;
    }
  if (!c)
    return 0;
  memset(c, 0, sizeof(*c));
  c->token = next_clip++;
  c->encoded = 1;
  c->requested_format = (unsigned)format;
  c->entry = (unsigned)entry;
  c->budget = (unsigned)budget;
  DndEhFrame frame;
  dnd_eh_push(&frame);
  int success = 0;
  if (setjmp(frame.environment) == 0) {
    c->file = dnd_fs_open(path, 3, 1, 1);
    if (c->file) {
      int64_t n = dnd_fs_length_native(c->file), extra = 0;
      if (body) {
        c->auxiliary_file = dnd_fs_open(body, 3, 1, 1);
        if (c->auxiliary_file)
          extra = dnd_fs_length_native(c->auxiliary_file);
      }
      if (n > 0 && n <= budget && (!body || (c->auxiliary_file && extra > 0)) &&
          extra <= budget - n &&
          n + extra + sizeof(DndAudioDecoder) + 64 <= (unsigned)budget) {
        c->input_size[0] = (unsigned)n;
        c->input_size[1] = (unsigned)extra;
        c->input[0] = malloc((size_t)n);
        if (extra)
          c->input[1] = malloc((size_t)extra);
        if (c->input[0] && (!extra || c->input[1]))
          success = 1;
      }
    }
  }
  dnd_eh_pop(&frame);
  if (dnd_exception_pending()) {
    dnd_exception_clear();
    success = 0;
  }
  if (!success) {
    dnd_audio_dispose(c->token);
    return 0;
  }
  return c->token;
}
static int audio_encoded_step(Clip *c) {
  if (c->input_index < 2) {
    unsigned i = (unsigned)c->input_index;
    if (c->input_position[i] < c->input_size[i]) {
      unsigned n = c->input_size[i] - c->input_position[i];
      if (n > 4096)
        n = 4096;
      int got = dnd_fs_read_native(i ? c->auxiliary_file : c->file,
                                   c->input[i] + c->input_position[i], (int)n);
      if (got <= 0 || (unsigned)got > n)
        return -1;
      c->input_position[i] += (unsigned)got;
      if (c->input_position[i] < c->input_size[i])
        return 0;
    }
    int *file = i ? &c->auxiliary_file : &c->file;
    if (*file) {
      dnd_fs_close(*file);
      *file = 0;
    }
    c->input_index++;
    return 0;
  }
  if (!c->parsed) {
    AudioView h = {c->input[0], c->input_size[0], 0},
              body = {c->input[1], c->input_size[1], 0};
    unsigned remaining = c->budget - c->input_size[0] - c->input_size[1] -
                         (unsigned)sizeof(DndAudioDecoder);
    if (!ac_parse(&c->source, &h, &body, c->requested_format, c->entry,
                  remaining))
      return -1;
    c->length = c->source.samples * c->source.channels * 2;
    c->format = c->source.channels == 2 ? 3 : 2;
    c->rate = c->source.rate;
    dnd_audio_decoder_init(&c->validation, &c->source, false);
    c->parsed = 1;
  }
  /* Bound every background validation slice; retain compressed input for
   * playback. */
  uint8_t scratch[1024];
  unsigned frames = sizeof(scratch) / (c->source.channels * 2);
  unsigned got = dnd_audio_decode(&c->validation, scratch, frames);
  if (c->validation.failed ||
      (!got && c->validation.state.position < c->source.samples))
    return -1;
  if (c->validation.state.position < c->source.samples)
    return 0;
  c->ready = 1;
  return 1;
}
int dnd_audio_create_pcm8(DndArray *samples, int rate) {
  if (!samples || samples->elements_are_references ||
      samples->element_size != 1 || rate < 8000 || rate > 48000 ||
      next_clip == INT_MAX)
    return 0;
  unsigned size = dnd_array_length(samples);
  if (size == 0 || size > MAX_PCM_BYTES)
    return 0;
  dnd_audio_init();
  int slot = -1;
  for (int i = 0; i < CLIPS; i++)
    if (!clips[i].token) {
      slot = i;
      break;
    }
  if (slot < 0)
    return 0;
  size_t aligned = ((size_t)size + 31u) & ~(size_t)31u;
  uint8_t *pcm = memalign(32, aligned);
  if (!pcm)
    return 0;
  /* AESND reads native memory asynchronously: never retain a managed pointer.
   */
  memset(pcm, 128, aligned);
  for (unsigned i = 0; i < size; i++)
    pcm[i] = (uint8_t)dnd_array_load_scalar(samples, i, 1, false);
  int token = next_clip++;
  clips[slot] = (Clip){.token = token,
                       .file = 0,
                       .ready = 1,
                       .length = size,
                       .format = 4,
                       .rate = (unsigned)rate,
                       .pcm = pcm};
  return token;
}
int dnd_audio_step(int token) {
  Clip *c = find_clip(token);
  if (!c)
    return -1;
  if (c->ready)
    return 1;
  if (c->encoded)
    return audio_encoded_step(c);
  unsigned remain = c->length - c->position;
  unsigned count = remain > 4096 ? 4096 : remain;
  int got = dnd_fs_read_native(c->file, c->pcm + c->position, (int)count);
  if (got <= 0)
    return -1;
  c->position += (unsigned)got;
  if (c->position < c->length)
    return 0;
  dnd_fs_close(c->file);
  c->file = 0;
  /* WAV is little-endian; GameCube PCM16 samples are big-endian. */
  if (c->format == 2 || c->format == 3)
    for (unsigned i = 0; i < c->length; i += 2) {
      uint8_t b = c->pcm[i];
      c->pcm[i] = c->pcm[i + 1];
      c->pcm[i + 1] = b;
    }
  c->ready = 1;
  return 1;
}
int dnd_audio_stat(int token, int kind) {
  Clip *c = find_clip(token);
  if (!c)
    return -1;
  return kind == 0   ? c->ready
         : kind == 1 ? (int)c->length
         : kind == 2 ? (int)c->rate
         : kind == 3 ? (int)(c->format & 1u ? 2 : 1)
         : kind == 4 ? (int)(c->length / ((c->format & 1u ? 2 : 1) *
                                          ((c->format & 2u) ? 2 : 1)))
         : kind == 5 ? (int)c->source.has_loop
         : kind == 6 ? (int)c->source.loop_start
         : kind == 7
             ? (c->encoded ? (int)c->source.loop_end
                           : (int)(c->length / ((c->format & 1u ? 2 : 1) *
                                                ((c->format & 2u) ? 2 : 1))))
         : kind == 8 ? (int)(c->encoded ? c->input_size[0] + c->input_size[1] +
                                              c->source.span_count *
                                                  sizeof(DndAudioSpan) +
                                              sizeof(DndAudioDecoder)
                                        : ((c->length + 31u) & ~31u))
         : kind == 9 ? c->encoded
                     : -1;
}
static void stop_slot(int i) {
  if (!voices[i].token)
    return;
  dnd_audio_backend_stop((unsigned)i);
  memset(&voices[i], 0, sizeof(voices[i]));
}
int dnd_audio_dispose(int token) {
  Clip *c = find_clip(token);
  if (!c)
    return -1;
  for (int i = 0; i < VOICES; i++)
    if (voices[i].clip == token)
      stop_slot(i);
  if (c->file)
    dnd_fs_close(c->file);
  if (c->auxiliary_file)
    dnd_fs_close(c->auxiliary_file);
  free(c->input[0]);
  free(c->input[1]);
  free(c->source.span);
  free(c->pcm);
  memset(c, 0, sizeof(*c));
  return 0;
}
static int play_clip(Clip *c, int loop, unsigned start, unsigned end,
                     float volume, float pitch) {
  if (!c || !c->ready || next_voice == INT_MAX || !isfinite(volume) ||
      volume < 0 || volume > 1 || !isfinite(pitch) || pitch < 0.25f ||
      pitch > 3.0f || c->rate * pitch > 144000)
    return 0;
  int slot = -1;
  for (int i = 0; i < VOICES; i++) {
    if (voices[i].token && !dnd_audio_backend_playing((unsigned)i))
      stop_slot(i);
    if (slot < 0 && !voices[i].token)
      slot = i;
  }
  if (slot < 0)
    return 0;
  int started =
      c->encoded
          ? dnd_audio_backend_play_encoded((unsigned)slot, &c->source,
                                           loop != 0, start, end, volume, pitch)
          : dnd_audio_backend_play((unsigned)slot, c->pcm, c->length, c->format,
                                   c->rate, loop != 0, volume, pitch);
  if (!started)
    return 0;
  int voice = next_voice++;
  voices[slot] = (Voice){.token = voice, .clip = c->token, .rate = c->rate};
  return voice;
}
int dnd_audio_play(int token, int loop, float volume, float pitch) {
  Clip *c = find_clip(token);
  return c ? play_clip(c, loop, c->source.loop_start, c->source.loop_end,
                       volume, pitch)
           : 0;
}
int dnd_audio_play_range(int token, int start, int end, float volume,
                         float pitch) {
  Clip *c = find_clip(token);
  if (!c || !c->ready || !c->encoded || start < 0 || end <= start ||
      (unsigned)end > c->source.samples)
    return 0;
  return play_clip(c, 1, (unsigned)start, (unsigned)end, volume, pitch);
}
int dnd_audio_voice_stat(int token) {
  Voice *v = find_voice(token);
  if (!v)
    return 0;
  return dnd_audio_backend_playing((unsigned)(v - voices)) ? 1 : 0;
}
int dnd_audio_voice_control(int token, int kind, float value) {
  Voice *v = find_voice(token);
  if (!v || !dnd_audio_backend_playing((unsigned)(v - voices)) ||
      !isfinite(value) || (kind == 0 && (value < 0 || value > 1)) ||
      (kind == 1 && (value < 0.25f || value > 3 || value * v->rate > 144000)) ||
      (kind != 0 && kind != 1))
    return -1;
  dnd_audio_backend_control((unsigned)(v - voices), kind, value, v->rate);
  return 0;
}
int dnd_audio_voice_stop(int token) {
  Voice *v = find_voice(token);
  if (!v)
    return 0; /* A finished voice is already stopped. */
  stop_slot((int)(v - voices));
  return 0;
}
void dnd_audio_shutdown(void) {
  if (!initialized)
    return;
  for (int i = 0; i < VOICES; i++)
    stop_slot(i);
  for (int i = 0; i < CLIPS; i++)
    if (clips[i].token)
      dnd_audio_dispose(clips[i].token);
  dnd_audio_backend_shutdown();
  initialized = 0;
}
