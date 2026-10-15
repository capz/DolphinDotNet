#ifndef DND_FST_H
#define DND_FST_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
typedef bool (*DndDiscRead)(void *context, uint64_t offset, void *data,
                            size_t length);
typedef struct {
  uint8_t *table;
  uint32_t count, size;
  DndDiscRead read;
  void *context;
  uint64_t capacity;
  /* Original boot.bin header captured during successful FST validation. */
  uint8_t header[0x440];
} DndFst;
bool dnd_fst_mount(DndFst *, DndDiscRead, void *, uint64_t capacity);
void dnd_fst_unmount(DndFst *);
int dnd_fst_find(const DndFst *, const char *path);
bool dnd_fst_directory(const DndFst *, uint32_t entry);
const char *dnd_fst_name(const DndFst *, uint32_t entry);
uint32_t dnd_fst_length(const DndFst *, uint32_t entry);
uint32_t dnd_fst_next(const DndFst *, uint32_t entry);
int dnd_fst_read(const DndFst *, uint32_t entry, uint32_t offset, void *data,
                 size_t length);
#ifdef DND_GAMECUBE_STORAGE
bool dnd_gc_fst_mount(void);
bool dnd_gc_fst_mount_image(const char *path);
bool dnd_gc_fst_copy_header(uint8_t output[0x440]);
void dnd_gc_fst_unmount(void);
#endif
#endif
