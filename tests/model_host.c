#define _POSIX_C_SOURCE 200809L
#include "dnd_aram.h"
#include "dnd_draw.h"
#include "dnd_model.h"
#include "dnd_storage.h"
#include <stdio.h>
#include <stdlib.h>
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
int main(int argc, char **argv) {
  if (argc < 2)
    return 90;
  setenv("DND_STORAGE_ROOT", argv[1], 1);
  static unsigned char memory[128 * 1024];
  DndManagedHeap heap;
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  dnd_gc_set_stress(argc > 2);
  intptr_t result = dnd_value_aot_entry(&heap);
  if (result || dnd_exception_pending() || dnd_model_live_handles() ||
      dnd_fs_open_handles() || dnd_aram_stat(4) || dnd_draw_live()) {
    fprintf(stderr,
            "model smoke result=%ld error=%d message=%s models=%d files=%d "
            "ARAM=%d\n",
            (long)result, dnd_exception_kind(), dnd_exception_message(),
            dnd_model_live_handles(), dnd_fs_open_handles(), dnd_aram_stat(4));
    DndString *detail = dnd_exception_pending()
                           ? dnd_exception_get_message((DndException *)dnd_exception_object())
                           : NULL;
    if (detail) {
      fputs("Managed assertion: ", stderr);
      for (uint32_t i = 0; i < detail->length; i++)
        fputc(detail->chars[i] < 128 ? (char)detail->chars[i] : '?', stderr);
      fputc('\n', stderr);
    }
    return 1;
  }
  printf("Model AOT integration passed; heap high water=%zu collections=%zu\n",
         heap.used, heap.collections);
  return 0;
}
