#ifndef DND_FILE_EXTENTS_H
#define DND_FILE_EXTENTS_H
#include <stdint.h>
/* Read one 512-byte sector into a 32-byte-aligned caller-owned buffer.
 * This parser is read-only and never writes filesystem metadata. */
typedef struct {
    void *context;
    int (*read_sector)(void *context, uint64_t lba, void *aligned_buffer);
    uint64_t partition_lba;
} DndSectorReader;
/* Relative / separated path, ASCII only. Returns -ENOTSUP on unrecognized
 * media/path encodings; malformed allocation chains return -EIO. */
int dnd_fat_count_extents(const DndSectorReader *reader,
                          const char *relative_path, unsigned *extents);
#endif
