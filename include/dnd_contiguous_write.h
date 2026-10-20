#ifndef DND_CONTIGUOUS_WRITE_H
#define DND_CONTIGUOUS_WRITE_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define DND_CW_PATH_CAP 1024
enum {
  DND_CW_AFTER_RESERVE=1,
  DND_CW_BEFORE_PUBLISH=2,
  DND_CW_AFTER_PUBLISH=3
};
typedef struct {
  void *context;
  /* Count physical, not logical, cluster runs. Unsupported filesystems
   * must return -ENOTSUP; never claim success without inspecting allocation. */
  int (*count_extents)(void *context,const char *path,unsigned *extents);
  /* Optional fault injection used by native tests. */
  int (*checkpoint)(void *context,int point);
} DndContiguousOps;
typedef struct {
  DndContiguousOps ops;
  char root[DND_CW_PATH_CAP], target[DND_CW_PATH_CAP], staged[DND_CW_PATH_CAP];
  int fd;
  uint64_t expected, written, checksum;
  bool active;
} DndContiguousWriter;

/* Create-new only. The published path is never replaced/overwritten.
 * The reserved temporary file is invisible at its final path until Complete.
 * Size is known in advance, with the GameCube 32-bit off_t limit enforced. */
int dnd_contiguous_begin(DndContiguousWriter *writer,const char *root,
                         const char *target,uint64_t length,DndContiguousOps ops);
int dnd_contiguous_write(DndContiguousWriter *writer,const void *data,size_t length);
int dnd_contiguous_complete(DndContiguousWriter *writer);
int dnd_contiguous_abort(DndContiguousWriter *writer);
/* Recovers interrupted staging/publish on mount. Never deletes a published
 * destination; invalid journals fail closed without modifying any files. */
int dnd_contiguous_recover(const char *root);
#endif
