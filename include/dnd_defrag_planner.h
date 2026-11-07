#ifndef DND_DEFRAG_PLANNER_H
#define DND_DEFRAG_PLANNER_H
#include <stdint.h>
#include <stddef.h>
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

/* Native, read-only planning contracts. Arrays belong to the caller; a scan
 * exceeding either capacity fails with -ENOSPC and valid remains zero. No
 * full-volume bitmap, heap allocation, recursive traversal or file data reads.
 * Names are exact UTF-16 components; parent indices reconstruct paths without
 * guessing OEM codepages. Entries with undecodable FAT short names are pinned.
 */
#define DND_DEFRAG_FREE UINT32_MAX
#define DND_DEFRAG_UNKNOWN (UINT32_MAX-1u)
#define DND_DEFRAG_SYSTEM (UINT32_MAX-2u)
enum {
    DND_DEFRAG_DIRECTORY=1, DND_DEFRAG_PINNED=2,
    DND_DEFRAG_NO_FAT_CHAIN=4
};
typedef struct {
    uint64_t entry_lba, bytes;
    uint32_t parent, first_cluster, flags;
    uint16_t entry_offset, attributes, name[256];
} DndDefragFile;
typedef struct {
    uint64_t start, count;
    uint32_t owner; /* file index, FREE, UNKNOWN or SYSTEM */
} DndDefragRun;
typedef struct {
    /* Supplied by the mounted-device layer, never inferred from mount names
     * or FAT serials. All partitions on one card share physical_id. Unknown
     * (all zero) identity is rejected. Increment generation on remount/swap. */
    uint64_t physical_id[2], generation;
    int writable;
} DndDefragDevice;
typedef struct {
    DndDefragFile *files;
    DndDefragRun *runs;
    size_t file_capacity, run_capacity, file_count, run_count;
    DndDefragDevice device;
    DndDefragFreeSpace space;
    uint64_t partition_lba, volume_serial, fingerprint;
    int valid;
} DndDefragSnapshot;
typedef struct {
    void *context;
    /* Additional mounted/open-file/system policy. Return 1 to pin, 0 to
     * retain default policy, or -errno. The callback must never write media. */
    int (*pin_file)(void *context,const DndDefragFile *file);
} DndDefragScanOps;
int dnd_defrag_scan(const DndSectorReader *reader,DndDefragDevice device,
                     DndDefragScanOps ops,DndDefragSnapshot *snapshot);
/* Compare with a fresh scan under the same per-volume lock. -ESTALE includes
 * remount, device/partition swap, directory, policy or allocation changes.
 * Fingerprints detect accidental changes; they are not authentication. */
int dnd_defrag_snapshot_matches(const DndDefragSnapshot *before,
                                 const DndDefragSnapshot *fresh);

enum {
    DND_DEFRAG_NO_PLAN=0, DND_DEFRAG_DIRECT=1,
    DND_DEFRAG_EVACUATE_ISO=2, DND_DEFRAG_EVACUATE_BLOCKERS=3,
    DND_DEFRAG_REBUILD=4
};
enum {
    DND_DEFRAG_REASON_NONE=0, DND_DEFRAG_REASON_PINNED=1,
    DND_DEFRAG_REASON_CAPACITY=2, DND_DEFRAG_REASON_SCRATCH=3,
    DND_DEFRAG_REASON_GUARD=4
};
typedef struct {
    uint32_t file;
    uint64_t bytes, scratch_clusters;
} DndDefragMove;
typedef struct {
    int strategy, reason;
    uint32_t source;
    uint64_t start, clusters, evacuation_bytes, scratch_peak_clusters;
    uint64_t target_peak_clusters, bytes_read, bytes_written;
    uint64_t target_fingerprint, scratch_fingerprint;
    size_t move_count;
} DndDefragPlan;
/* Exact candidate search over extent boundaries. Whole files are charged once,
 * even when multiple extents intersect. Order: minimum evacuation bytes, then
 * moved files, then predicted I/O, then lowest cluster. Source evacuation is
 * considered with/without blockers. Scratch capacity uses SCRATCH geometry.
 * Caller reserves extra scratch clusters for filesystem/journal growth.
 * Moves are ordered by stable file index (source first when evacuated).
 * mounted_iso/live_source must be supplied by the mounted-handle layer.
 * This is an estimate, never permission/reservation to execute or rebuild. */
int dnd_defrag_plan(const DndDefragSnapshot *target,uint32_t source,
                     const DndDefragSnapshot *scratch,
                     uint64_t scratch_reserve_clusters,
                     int mounted_iso,int live_source,
                     DndDefragMove *moves,size_t move_capacity,
                     DndDefragPlan *plan);

#endif
