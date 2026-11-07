#ifndef DND_DEFRAG_PLANNER_H
#define DND_DEFRAG_PLANNER_H
#include <stdint.h>
#include "dnd_file_extents.h"

enum { DND_DEFRAG_FAT32 = 1, DND_DEFRAG_EXFAT = 2 };

/* All fields are cluster counts except byte/cluster and the physical cluster
 * index (which is the FAT/exFAT cluster number, beginning at 2).
 * This is a read-only snapshot, not a reserved allocation. */
typedef struct {
    uint64_t total_clusters;
    uint64_t free_clusters;
    uint64_t largest_free_run_clusters;
    uint64_t largest_free_run_start_cluster;
    uint32_t bytes_per_cluster;
    int filesystem;
} DndDefragFreeSpace;

/* Inspect free allocation in a mounted 512-byte-sector FAT32 or exFAT
 * partition. The reader is the same cached physical-sector interface already
 * used for file extent inspection. exFAT uses its allocation bitmap, not FAT
 * entries to infer free clusters. No sector writes occur.
 * Returns 0 or negative errno; unsupported media return -ENOTSUP. */
int dnd_defrag_read_free_space(const DndSectorReader *reader,
                               DndDefragFreeSpace *result);

/* Dry-run direct-reservation feasibility. Returns 1 when a single free run
 * can fit file_bytes, 0 when not, and -errno on invalid/unsupported inputs.
 * The returned start is a hint: re-scan and verify on actual reservation.
 * GameCube native off_t limits are enforced here (<= INT32_MAX). */
int dnd_defrag_preflight_direct(const DndDefragFreeSpace *stats,
                                uint64_t file_bytes, uint64_t *start_cluster);

#endif
