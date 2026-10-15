#include "dnd_iso_file.h"
#include <limits.h>
#include <string.h>

/* FST reads always use absolute byte positions. Keep the backing ISO open. */
static bool iso_read(void *context, uint64_t offset, void *output, size_t count) {
  DndIsoFile *image = context;
  if (!image || !image->file || offset > LONG_MAX || count > LONG_MAX ||
      count > (uint64_t)LONG_MAX - offset || fseek(image->file, (long)offset, SEEK_SET))
    return false;
  return fread(output, 1, count, image->file) == count;
}

bool dnd_iso_file_open(DndIsoFile *image, const char *path) {
  if (!image || !path || !*path || image->file || image->fst.table)
    return false;
  FILE *file = fopen(path, "rb");
  if (!file)
    return false;
  if (fseek(file, 0, SEEK_END)) { fclose(file); return false; }
  long capacity = ftell(file);
  if (capacity < 0 || capacity < 0x440) { fclose(file); return false; }
  image->file = file;
  if (!dnd_fst_mount(&image->fst, iso_read, image, (uint64_t)capacity)) {
    fclose(file);
    image->file = NULL;
    return false;
  }
  return true;
}

void dnd_iso_file_close(DndIsoFile *image) {
  if (!image) return;
  dnd_fst_unmount(&image->fst);
  if (image->file) fclose(image->file);
  image->file = NULL;
}
int dnd_iso_file_entry(const DndIsoFile *image, const char *path) {
  return image ? dnd_fst_find(&image->fst, path) : -1;
}
int dnd_iso_file_read(const DndIsoFile *image, unsigned index, uint32_t offset,
                      void *data, size_t count) {
  if (!image) return -1;
  return dnd_fst_read(&image->fst, index, offset, data, count);
}
