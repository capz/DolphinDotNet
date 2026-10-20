#ifndef DND_FILE_OPTIMIZE_H
#define DND_FILE_OPTIMIZE_H
#include <stdint.h>
#include <sys/stat.h>

/* Status values are stable across the native/managed bridge. */
enum {
    DND_OPT_ALREADY_CONTIGUOUS = 0,
    DND_OPT_REPAIRED = 1,
    DND_OPT_NO_CONTIGUOUS_SPACE = 2,
    DND_OPT_UNSUPPORTED = 3
};

/* The platform probe MUST return the number of physical allocation extents.
 * A reported single extent must mean actual consecutive disk clusters.
 * Return -ENOTSUP if the filesystem cannot be inspected safely. */
typedef struct {
    void *context;
    int (*count_extents)(void *context, const char *path, unsigned *extents);
    /* Optional deterministic fault injection for host tests. */
    int (*checkpoint)(void *context, int point);
    /* Return 0 only when all available attributes and timestamps have been
     * copied, or -ENOTSUP to leave the original untouched. */
    int (*preserve_metadata)(void *context,const char *original,
                             const char *stage,const struct stat *metadata);
} DndOptimizeOps;
enum {
    DND_OPT_AFTER_PREPARE = 4,
    DND_OPT_DURING_COPY = 5,
    DND_OPT_BEFORE_BACKUP = 1,
    DND_OPT_AFTER_BACKUP = 2,
    DND_OPT_AFTER_PUBLISH = 3
};

/* root is a mounted volume root and path is a canonical file on that volume.
 * Only one optimization is permitted per volume at a time; callers serialize.
 * Return a status above or a negative errno. */
int dnd_optimize_file(const char *root, const char *path,
                      const DndOptimizeOps *ops);
/* Call before exposing the newly mounted volume. Never discards a backup
 * unless the published file has been completely verified. */
int dnd_optimize_recover(const char *root);

#endif
