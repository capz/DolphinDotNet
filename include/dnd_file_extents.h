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
/* Relative / separated UTF-8 path. Returns -ENOTSUP on unrecognized
 * media/path encodings; malformed allocation chains return -EIO. */
int dnd_fat_count_extents(const DndSectorReader *reader,
                          const char *relative_path, unsigned *extents);
/* Resolve a nonempty file only when its complete allocation is contiguous.
 * absolute_lba is measured in 512-byte sectors from the start of the device
 * (including partition_lba exactly once); length is the exact file byte size.
 * Fragmented files return -ENOTSUP, empty files -EINVAL, and malformed or
 * incompletely initialized allocations -EIO. Outputs are unchanged on failure.
 * The caller must keep the filesystem unchanged while inspecting/using a span. */
int dnd_fat_contiguous_span(const DndSectorReader *reader,
                            const char *relative_path,
                            uint64_t *absolute_lba, uint64_t *length);
#endif
