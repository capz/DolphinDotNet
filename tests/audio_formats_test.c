/* Original fixture playback through the public native API and a deterministic
 * DSP fake. */
#include "audio_test_eh.inc"
#include "dnd_audio.h"
#include "dnd_audio_codec.h"
#include "dnd_storage.h"
#include <assert.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static struct {
  uint8_t *data;
  unsigned size, cursor;
  int open;
} files[2];
static int handles, throw_aux, read_failure;
static unsigned read_quantum = 7;
static DndAudioDecoder decoder[32];
static int active[32];
int dnd_fs_open(DndString *path, int mode, int access, int share) {
  assert(mode == 3 && access == 1 && share == 1);
  unsigned i = (unsigned)(uintptr_t)path - 1;
  assert(i < 2);
  if (i == 1 && throw_aux) {
    audio_test_exception = true;
    longjmp(audio_test_eh->environment, 1);
  }
  if (!files[i].data)
    return 0;
  assert(!files[i].open);
  files[i].open = 1;
  files[i].cursor = 0;
  handles++;
  return 11 + (int)i;
}
int64_t dnd_fs_length_native(int handle) {
  assert(handle == 11 || handle == 12);
  return files[handle - 11].size;
}
int dnd_fs_read_native(int handle, void *out, int count) {
  assert(handle == 11 || handle == 12);
  unsigned i = (unsigned)handle - 11;
  assert(files[i].open);
  if (read_failure)
    return -1;
  unsigned n = (unsigned)count;
  if (n > files[i].size - files[i].cursor)
    n = files[i].size - files[i].cursor;
  if (n > read_quantum)
    n = read_quantum;
  memcpy(out, files[i].data + files[i].cursor, n);
  files[i].cursor += n;
  return (int)n;
}
int64_t dnd_fs_seek(int handle, int64_t at, int origin) {
  unsigned i = (unsigned)handle - 11;
  assert(i < 2 && origin == 0);
  if (at < 0 || (uint64_t)at > files[i].size)
    return -1;
  files[i].cursor = (unsigned)at;
  return at;
}
void dnd_fs_close(int handle) {
  assert(handle == 11 || handle == 12);
  assert(files[handle - 11].open);
  files[handle - 11].open = 0;
  handles--;
}
uint32_t dnd_array_length(DndArray *a) { return a->length; }
uint64_t dnd_array_load_scalar(DndArray *a, uint32_t i, uint32_t size,
                               bool sign) {
  (void)sign;
  assert(size == 1);
  return a->data[i];
}
void dnd_audio_backend_init(void) {}
void dnd_audio_backend_shutdown(void) {}
int dnd_audio_backend_play(unsigned slot, const void *data, unsigned size,
                           unsigned format, unsigned rate, bool loop,
                           float volume, float pitch) {
  (void)slot;
  (void)data;
  (void)size;
  (void)format;
  (void)rate;
  (void)loop;
  (void)volume;
  (void)pitch;
  assert(0);
  return 0;
}
int dnd_audio_backend_play_encoded(unsigned slot, const DndAudioSource *s,
                                   bool loop, unsigned start, unsigned end,
                                   float volume, float pitch) {
  assert(slot < 32 && volume == 1 && pitch == 1);
  active[slot] = 1;
  dnd_audio_decoder_init(&decoder[slot], s, loop);
  decoder[slot].loop_start = start;
  decoder[slot].loop_end = end;
  return 1;
}
int dnd_audio_backend_playing(unsigned slot) { return active[slot]; }
void dnd_audio_backend_stop(unsigned slot) {
  active[slot] = 0;
  memset(&decoder[slot], 0, sizeof(decoder[slot]));
}
void dnd_audio_backend_control(unsigned slot, int kind, float value,
                               unsigned rate) {
  (void)slot;
  (void)kind;
  (void)value;
  (void)rate;
}
static void input(const char *root, const char *name, const char *body) {
  const char *names[2] = {name, body};
  for (unsigned i = 0; i < 2; i++) {
    assert(!files[i].open);
    free(files[i].data);
    memset(&files[i], 0, sizeof(files[i]));
    if (!names[i])
      continue;
    char path[1024];
    snprintf(path, sizeof(path), "%s/%s", root, names[i]);
    FILE *f = fopen(path, "rb");
    assert(f);
    assert(!fseek(f, 0, SEEK_END));
    long n = ftell(f);
    assert(n > 0);
    rewind(f);
    files[i].size = (unsigned)n;
    files[i].data = malloc((size_t)n);
    assert(files[i].data);
    assert(fread(files[i].data, 1, (size_t)n, f) == (size_t)n);
    assert(!fclose(f));
  }
}
static int start(int format, int entry, int budget) {
  return dnd_audio_begin_gamecube((DndString *)1,
                                  files[1].data ? (DndString *)2 : NULL, format,
                                  entry, budget);
}
static int complete(int token) {
  int status;
  unsigned steps = 0;
  while ((status = dnd_audio_step(token)) == 0)
    assert(++steps < 100000);
  return status;
}
static int sample(const uint8_t *p, unsigned n) {
  return (int16_t)((unsigned)p[n * 2] << 8 | p[n * 2 + 1]);
}
static void check(const char *root, const char *name, int format,
                  const char *body, int entry, int expected) {
  input(root, name, body);
  int token = start(format, entry, 1024 * 1024);
  if (!token)
    fprintf(stderr, "Audio open failed: %s\n", name);
  assert(token);
  int status = complete(token);
  if (status != 1)
    fprintf(stderr, "Audio decode failed: %s\n", name);
  assert(status == 1 && !handles);
  unsigned channels = (unsigned)dnd_audio_stat(token, 3),
           frames = (unsigned)dnd_audio_stat(token, 4);
  assert(channels >= 1 && channels <= 2 && frames <= 128);
  assert(dnd_audio_stat(token, 1) == (int)(frames * channels * 2));
  int voice = dnd_audio_play(token, 0, 1, 1);
  assert(voice);
  uint8_t full[512];
  assert(dnd_audio_decode(&decoder[0], full, frames) == frames);
  assert(!decoder[0].failed);
  assert(sample(full, 0) == expected);
  uint8_t one[4];
  assert(!dnd_audio_decode(&decoder[0], one, 1));
  assert(dnd_audio_voice_stop(voice) == 0);
  voice = dnd_audio_play(token, 1, 1, 1);
  assert(voice);
  int second = dnd_audio_play(token, 1, 1, 1);
  assert(second);
  unsigned begin = (unsigned)dnd_audio_stat(token, 6),
           end = (unsigned)dnd_audio_stat(token, 7);
  assert(begin < end && end <= frames);
  for (unsigned i = 0; i < end + 3 * (end - begin) + 1; i++) {
    assert(dnd_audio_decode(&decoder[0], one, 1) == 1);
    unsigned at = i < end ? i : begin + (i - end) % (end - begin);
    assert(!memcmp(one, full + at * channels * 2, channels * 2));
  }
  assert(dnd_audio_decode(&decoder[1], one, 1) == 1 &&
         !memcmp(one, full, channels * 2));
  assert(dnd_audio_dispose(token) == 0 && !active[0] && !active[1] && !handles);
}
/* Compare every repeated sample to a complete one-shot decode. Histories and
 * stereo data differ across frames, so misaligned seeks or stale predictors
 * cannot accidentally produce the expected sequence. */
static void check_adx_ranges(const char *root, const char *name, int format,
                             int channels, int has_loop) {
  input(root, name, NULL);
  int token = start(format, 0, 1024 * 1024);
  assert(token && complete(token) == 1 && !handles);
  assert(dnd_audio_stat(token, 3) == channels &&
         dnd_audio_stat(token, 4) == 117 &&
         dnd_audio_stat(token, 5) == has_loop);
  unsigned original_start = has_loop ? 35 : 0,
           original_end = has_loop ? 101 : 117;
  assert(dnd_audio_stat(token, 6) == (int)original_start &&
         dnd_audio_stat(token, 7) == (int)original_end);
  assert(dnd_audio_stat(token, 9) == 1);
  int once = dnd_audio_play(token, 0, 1, 1);
  assert(once);
  uint8_t full[117 * 4], output[1152];
  assert(dnd_audio_decode(&decoder[0], full, 117) == 117);
  assert(!decoder[0].failed);
  assert(memcmp(full, full + 32 * channels * 2, (size_t)channels * 2));
  if (channels == 2)
    assert(memcmp(full, full + 2, 2));
  assert(!dnd_audio_voice_stop(once));
  const unsigned ranges[][2] = {{0, 1},     {1, 2},   {3, 31},   {31, 32},
                                {31, 33},   {32, 64}, {35, 101}, {63, 117},
                                {116, 117}, {0, 117}};
  for (unsigned r = 0; r < sizeof(ranges) / sizeof(ranges[0]); r++) {
    unsigned start_sample = ranges[r][0], end_sample = ranges[r][1];
    int voice =
        dnd_audio_play_range(token, (int)start_sample, (int)end_sample, 1, 1);
    assert(voice);
    int second = dnd_audio_play_range(token, 5, 73, 1, 1);
    assert(second && second != voice);
    const int invalid[][2] = {
        {-1, 50},   {0, -1},           {5, 5}, {6, 5}, {0, 118},
        {117, 118}, {INT_MAX, INT_MAX}};
    for (unsigned bad = 0; bad < sizeof(invalid) / sizeof(invalid[0]); bad++)
      assert(
          !dnd_audio_play_range(token, invalid[bad][0], invalid[bad][1], 1, 1));
    assert(!decoder[0].state.position && !decoder[1].state.position);
    unsigned cursor = 0;
    const unsigned requests[] = {1, 17, 2, 288, 33};
    for (unsigned chunk = 0; chunk < 20; chunk++) {
      unsigned count = requests[chunk % 5];
      assert(dnd_audio_decode(&decoder[0], output, count) == count);
      assert(!decoder[0].failed);
      for (unsigned i = 0; i < count; i++) {
        unsigned at = cursor + i;
        if (at >= end_sample)
          at = start_sample + (at - end_sample) % (end_sample - start_sample);
        assert(!memcmp(output + i * channels * 2, full + at * channels * 2,
                       (size_t)channels * 2));
      }
      cursor += count;
    }
    assert(dnd_audio_decode(&decoder[1], output, 288) == 288);
    for (unsigned i = 0; i < 288; i++) {
      unsigned at = i < 73 ? i : 5 + (i - 73) % 68;
      assert(!memcmp(output + i * channels * 2, full + at * channels * 2,
                     (size_t)channels * 2));
    }
    assert(dnd_audio_voice_stat(voice) && dnd_audio_voice_stat(second));
    assert(!dnd_audio_voice_stop(voice) && !dnd_audio_voice_stop(second));
    assert(dnd_audio_stat(token, 5) == has_loop &&
           dnd_audio_stat(token, 6) == (int)original_start &&
           dnd_audio_stat(token, 7) == (int)original_end);
  }
  /* Embedded looping still uses the file interval after range overrides. */
  assert(dnd_audio_play(token, 1, 1, 1));
  assert(dnd_audio_decode(&decoder[0], output, 288) == 288);
  for (unsigned i = 0; i < 288; i++) {
    unsigned at = i < original_end
                      ? i
                      : original_start + (i - original_end) %
                                             (original_end - original_start);
    assert(!memcmp(output + i * channels * 2, full + at * channels * 2,
                   (size_t)channels * 2));
  }
  assert(!dnd_audio_dispose(token) && !active[0] && !active[1]);
  assert(!dnd_audio_play_range(token, 0, 1, 1, 1));
}
int main(int argc, char **argv) {
  assert(argc == 2);
  const char *root = argv[1];
  for (int channels = 1; channels <= 2; channels++)
    for (int version = 0x300; version <= 0x500; version += 0x100) {
      char name[64];
      snprintf(name, sizeof(name), "loop-%d-%d.adx", channels, version);
      check_adx_ranges(root, name, 2, channels, version != 0x500);
    }
  check_adx_ranges(root, "loop.afs", 9, 2, 1);
  check_adx_ranges(root, "no-loop.adx", 2, 1, 0);
  check(root, "tone.awd", 0, NULL, 0, 1);
  check(root, "tone.dsp", 1, NULL, 0, 1);
  check(root, "history.dsp", 1, NULL, 0, 6);
  check(root, "tone.adx", 0, NULL, 0, 1);
  check(root, "zero-cutoff.adx", 2, NULL, 0, 1);
  check(root, "tone.afs", 0, NULL, 1, 1);
  check(root, "tone.afc", 3, NULL, 0, 1);
  check(root, "tone.hps", 0, NULL, 0, 1);
  check(root, "history.hps", 4, NULL, 0, 6);
  check(root, "tone.ssm", 5, NULL, 0, 1);
  check(root, "tone.ast", 0, NULL, 0, 1);
  check(root, "pcm.ast", 6, NULL, 0, 1);
  check(root, "tone.dtk", 11, NULL, 0, 1);
  check(root, "tone.rws", 8, NULL, 0, 1);
  check(root, "old.rws", 0, NULL, 0, 1);
  check(root, "tone.sbh", 10, "tone.sbb", 0, 1);
  for (int codec = 2; codec <= 4; codec++)
    for (int version = 0x300; version <= 0x500; version += 0x100) {
      char name[64];
      snprintf(name, sizeof(name), "type-%d-%d.adx", codec, version);
      check(root, name, 2, NULL, 0, codec == 4 ? 4096 : 1);
    }
  for (int codec = 0; codec < 4; codec++) {
    char head[64], body[64];
    snprintf(body, sizeof(body), "wave-%d.aw", codec);
    snprintf(head, sizeof(head), "wave-%d.wsys", codec);
    check(root, head, 7, body, 0, codec == 1 ? 0 : codec == 2 ? 256 : 1);
    snprintf(head, sizeof(head), "wave-%d.aaf", codec);
    check(root, head, 7, body, 0, codec == 1 ? 0 : codec == 2 ? 256 : 1);
  }
  const char *bad[] = {"bad-predictor.dsp", "bad-loop.dsp",
                       "bad-block.hps",     "bad-codec.rws",
                       "encrypted.adx",     "cycle.aaf",
                       "empty-loop.adx",    "reversed-loop.adx",
                       "bad-loop.adx",      "negative-loop.adx"};
  int formats[] = {1, 1, 4, 8, 2, 7, 2, 2, 2, 2};
  for (unsigned i = 0; i < sizeof(bad) / sizeof(bad[0]); i++) {
    input(root, bad[i], i == 5 ? "wave-0.aw" : NULL);
    int t = start(formats[i], 0, 1024 * 1024);
    assert(t && complete(t) < 0);
    assert(!dnd_audio_dispose(t) && !handles);
  }
  input(root, "tone.sbh", "tone.sbb");
  throw_aux = 1;
  assert(!start(10, 0, 1024 * 1024) && !handles && !dnd_exception_pending());
  throw_aux = 0;
  input(root, "tone.dsp", NULL);
  int t = start(1, 0, 1024 * 1024);
  assert(t);
  read_failure = 1;
  assert(dnd_audio_step(t) < 0);
  assert(!dnd_audio_dispose(t) && !handles);
  read_failure = 0;
  for (unsigned n = 0; n < files[0].size; n++) {
    unsigned original = files[0].size;
    files[0].size = n;
    t = start(1, 0, 1024 * 1024);
    if (t) {
      assert(complete(t) < 0);
      assert(!dnd_audio_dispose(t));
    }
    assert(!handles);
    files[0].size = original;
  }
  input(root, "tone.ssm", NULL);
  t = start(5, 1, 1024 * 1024);
  assert(t && complete(t) < 0);
  assert(!dnd_audio_dispose(t));
  input(root, "large.dsp", NULL);
  assert(!start(1, 0, 1024) && !handles);
  /* Deterministic mutations exercise pointer arithmetic and cleanup under
   * sanitizers. */
  const char *mutate[] = {"tone.dsp", "tone.adx",   "tone.afc", "tone.hps",
                          "tone.ssm", "tone.ast",   "tone.rws", "old.rws",
                          "tone.awd", "wave-0.aaf", "tone.sbh"};
  int mf[] = {1, 2, 3, 4, 5, 6, 8, 8, 12, 7, 10};
  unsigned random = 0x5a71;
  for (unsigned kind = 0; kind < 11; kind++) {
    input(root, mutate[kind],
          kind == 9    ? "wave-0.aw"
          : kind == 10 ? "tone.sbb"
                       : NULL);
    unsigned size = files[0].size;
    uint8_t *original = malloc(size);
    assert(original);
    memcpy(original, files[0].data, size);
    for (unsigned attempt = 0; attempt < 200; attempt++) {
      memcpy(files[0].data, original, size);
      random = random * 1664525u + 1013904223u;
      unsigned at = random % size;
      random = random * 1664525u + 1013904223u;
      files[0].data[at] ^= (uint8_t)(1u << (random & 7));
      t = start(mf[kind], 0, 1024 * 1024);
      if (t) {
        complete(t);
        assert(!dnd_audio_dispose(t));
      }
      assert(!handles);
    }
    free(original);
  }
  input(root, "music.dsp", NULL);
  read_quantum = 4096;
  t = start(1, 0, 8 * 1024 * 1024);
  assert(t && complete(t) == 1);
  assert(dnd_audio_stat(t, 1) > 16 * 1024 * 1024 &&
         dnd_audio_stat(t, 8) < 8 * 1024 * 1024);
  assert(dnd_audio_play(t, 1, 1, 1));
  assert(!dnd_audio_dispose(t) && !handles);
  dnd_audio_shutdown();
  for (unsigned i = 0; i < 2; i++)
    free(files[i].data);
  puts("GameCube audio containers, sample vectors, loops, independent voices, "
       "errors and ownership passed");
  return 0;
}
