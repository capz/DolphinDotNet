#define _POSIX_C_SOURCE 200809L
#include "dnd_async.h"
#include "dnd_model.h"
#include "dnd_storage.h"
#include "dnd_ufbx.h"
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

static pthread_t application;
static const char *root;
static atomic_int phase, released;
static int imports;
/* Link with --wrap=ufbx_load_memory: hold actual FBX parsing outside the gate
 * until the managed application renders, allocates/collects and cancels a load.
 * This fails deterministically if parsing holds the managed gate or runs on the
 * application thread. No instrumentation is added to production code. */
ufbx_scene *__real_ufbx_load_memory(const void *, size_t,
                                    const ufbx_load_opts *, ufbx_error *);
ufbx_scene *__wrap_ufbx_load_memory(const void *data, size_t size,
                                    const ufbx_load_opts *opts,
                                    ufbx_error *error) {
  if (pthread_equal(pthread_self(), application))
    abort();
  int current = ++imports;
  if (current <= 2) {
    char path[2048];
    snprintf(path, sizeof(path), "%s/sd/native-import-active", root);
    atomic_store(&phase, current);
    FILE *marker = fopen(path, "wb");
    if (!marker || fclose(marker))
      abort();
    struct timespec pause = {0, 1000000};
    int waited = 0;
    while (atomic_load(&released) < current && waited++ < 5000)
      nanosleep(&pause, NULL);
    if (atomic_load(&released) < current) {
      fputs("Application could not render/collect while native parsing was "
            "active.\n",
            stderr);
      abort();
    }
  }
  return __real_ufbx_load_memory(data, size, opts, error);
}
void dnd_console_write_line(const char *text) {
  if (strcmp(text, "release-native-import") ||
      !pthread_equal(pthread_self(), application))
    abort();
  atomic_store(&released, atomic_load(&phase));
}
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
int main(int argc, char **argv) {
  if (argc < 2)
    return 90;
  root = argv[1];
  application = pthread_self();
  setenv("DND_STORAGE_ROOT", root, 1);
  char path[2048];
  snprintf(path, sizeof(path), "%s/sd/native-import-active", root);
  unlink(path);
  snprintf(path, sizeof(path), "%s/sd/missing-sidecar.obj", root);
  FILE *file = fopen(path, "wb");
  if (!file)
    return 91;
  fputs("mtllib nonexistent.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n", file);
  fclose(file);
  static unsigned char storage[256 * 1024];
  DndManagedHeap heap;
  dnd_managed_heap_init(&heap, storage, sizeof(storage));
  dnd_gc_set_stress(argc > 2);
  intptr_t result = dnd_value_aot_entry(&heap);
  if (result || dnd_exception_pending() || dnd_async_running() ||
      dnd_model_live_handles() || dnd_fs_open_handles() ||
      atomic_load(&released) != 2) {
    fprintf(stderr,
            "model async: result=%ld error=%d models=%d files=%d barriers=%d\n",
            (long)result, dnd_exception_kind(), dnd_model_live_handles(),
            dnd_fs_open_handles(), atomic_load(&released));
    DndException *error = (DndException *)dnd_exception_object();
    if (error && error->message) {
      for (uint32_t i = 0; i < error->message->length; i++)
        fputc(error->message->chars[i] & 127, stderr);
      fputc('\n', stderr);
    }
    return 1;
  }
  printf("Background model await/render/cancellation passed; heap=%zu "
         "collections=%zu\n",
         heap.used, heap.collections);
  return 0;
}
