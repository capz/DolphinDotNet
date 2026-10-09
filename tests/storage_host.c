#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
void dnd_console_write_line(const char *s) { (void)s; }
void dnd_console_write_managed_line(DndString *s) { (void)s; }
uint16_t dnd_input_buttons_down(unsigned p) {
  (void)p;
  return 0;
}
void dnd_graphics_present_demo_frame(int p) { (void)p; }
extern intptr_t dnd_value_aot_entry(DndManagedHeap *);
int main(int argc, char **argv) {
  (void)argv;
  char root[] = "/tmp/dnd-storage-XXXXXX";
  if (!mkdtemp(root))
    return 90;
  setenv("DND_STORAGE_ROOT", root, 1);
  const char *names[] = {"sd", "dvd", "mca"};
  char path[1024];
  for (int i = 0; i < 3; i++) {
    snprintf(path, sizeof(path), "%s/%s", root, names[i]);
    if (mkdir(path, 0700))
      return 91;
  }
  snprintf(path, sizeof(path), "%s/dvd/asset.bin", root);
  FILE *f = fopen(path, "wb");
  if (!f)
    return 92;
  fputc(42, f);
  fclose(f);
  static unsigned char memory[128 * 1024];
  DndManagedHeap heap;
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  dnd_gc_set_stress(argc > 1);
  intptr_t result = dnd_value_aot_entry(&heap);
  int handles = dnd_fs_open_handles();
  if (result || dnd_exception_pending() || handles) {
    fprintf(
        stderr, "storage smoke result=%ld exception=%d handles=%d message=%s\n",
        (long)result, dnd_exception_kind(), handles, dnd_exception_message());
    return 1;
  }
  unlink(path);
  for (int i = 0; i < 3; i++) {
    snprintf(path, sizeof(path), "%s/%s", root, names[i]);
    rmdir(path);
  }
  rmdir(root);
  printf(
      "managed storage integration passed (%zu collections, %zu heap bytes)\n",
      heap.collections, heap.used);
  return 0;
}
