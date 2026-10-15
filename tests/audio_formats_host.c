#define _POSIX_C_SOURCE 200809L
#include "dnd_async.h"
#include "dnd_audio.h"
#include "dnd_audio_codec.h"
#include "dnd_storage.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
static DndAudioDecoder decoders[32];
static int active[32], plays;
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
  return 0;
}
int dnd_audio_backend_play_encoded(unsigned slot, const DndAudioSource *s,
                                   bool loop, unsigned start, unsigned end,
                                   float volume, float pitch) {
  (void)volume;
  (void)pitch;
  assert(slot < 32);
  active[slot] = 1;
  plays++;
  dnd_audio_decoder_init(&decoders[slot], s, loop);
  decoders[slot].loop_start = start;
  decoders[slot].loop_end = end;
  return 1;
}
int dnd_audio_backend_playing(unsigned slot) {
  if (!active[slot])
    return 0;
  uint8_t output[16];
  unsigned n = dnd_audio_decode(&decoders[slot], output, 4);
  if (!n)
    active[slot] = 0;
  return active[slot];
}
void dnd_audio_backend_stop(unsigned slot) { active[slot] = 0; }
void dnd_audio_backend_control(unsigned slot, int kind, float value,
                               unsigned rate) {
  (void)slot;
  (void)kind;
  (void)value;
  (void)rate;
}
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
int main(int argc, char **argv) {
  assert(argc >= 2);
  setenv("DND_STORAGE_ROOT", argv[1], 1);
  static unsigned char memory[256 * 1024];
  DndManagedHeap heap;
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  dnd_gc_set_stress(argc > 2);
  intptr_t result = dnd_value_aot_entry(&heap);
  if (result || dnd_exception_pending() || dnd_async_running() ||
      dnd_fs_open_handles() || plays < 4) {
    fprintf(stderr, "audio async result=%ld exception=%d files=%d plays=%d\n",
            (long)result, dnd_exception_kind(), dnd_fs_open_handles(), plays);
    fprintf(stderr, "Native message: %s\n", dnd_exception_message());
    DndString *detail =
        dnd_exception_get_message((DndException *)dnd_exception_object());
    if (detail) {
      for (uint32_t i = 0; i < detail->length; i++)
        fputc(detail->chars[i] & 127, stderr);
      fputc(10, stderr);
    }
    return 1;
  }
  dnd_audio_shutdown();
  printf(
      "Audio async/AOT/GC playback and cancellation passed; collections=%zu\n",
      heap.collections);
  return 0;
}
