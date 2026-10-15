#ifndef DND_ISO_FILE_H
#define DND_ISO_FILE_H
#include "dnd_fst.h"
#include <stdio.h>
typedef struct {
  FILE *file;
  DndFst fst;
} DndIsoFile;
/* Read-only GameCube disc image backed by any mounted filesystem's stdio. */
bool dnd_iso_file_open(DndIsoFile *image, const char *path);
void dnd_iso_file_close(DndIsoFile *image);
int dnd_iso_file_entry(const DndIsoFile *image, const char *path);
int dnd_iso_file_read(const DndIsoFile *image, unsigned index, uint32_t offset,
                      void *data, size_t count);
#endif
