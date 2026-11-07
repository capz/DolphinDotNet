#ifndef DND_DEFRAG_BATCH_H
#define DND_DEFRAG_BATCH_H
#include "dnd_evacuation.h"

typedef struct {
    uint64_t operation[2];
    DndDefragPlan plan;
    DndEvacRecord iso;
    const DndEvacRecord *blockers;
    size_t blocker_count;
    int evacuate_iso;
} DndDefragBatch;
typedef struct {
    void *context;
    /* Lock both physical volumes throughout the invocation. Revalidate the
     * trusted manifest, source hashes, writable firmware, capacity, planned
     * allocation changes and handle/DVD guards. Ignore only this operation's
     * own journal/stage mutations; unrelated changes invalidate the plan. */
    int (*guard)(void *,const DndDefragBatch *);
    /* Immutable reserved manifest, indexed 0=header, 1=ISO, 2+=blockers.
     * Read EXACTLY WIRE_BYTES or -ENOENT. Writes are create-new only; partial
     * or conflicting records must never be silently overwritten. */
    int (*read_manifest)(void *,unsigned volume,size_t index,void *wire);
    int (*write_manifest)(void *,unsigned volume,size_t index,const void *wire);
    int (*sync_volume)(void *,unsigned volume);
    DndEvacJournalOps (*journal)(void *,size_t file); /* 0=ISO, 1+=blockers */
    DndEvacFileOps (*files)(void *,size_t file);
    /* A fresh physical allocation check under the held lock, not an estimate. */
    int (*window_free)(void *,uint64_t start,uint64_t clusters);
    /* Verify ISO hash, length, metadata and ONE physical extent. -EAGAIN
     * means the intact original still needs relocation, -ENOENT evacuated. */
    int (*verify_iso)(void *,const DndEvacRecord *);
    int (*direct_iso)(void *,const DndEvacRecord *,DndCopyResult *);
    int (*blocker_extents)(void *,size_t blocker,unsigned *extents);
    int (*cancelled)(void *);
} DndDefragBatchOps;
typedef struct {
    size_t evacuated,restored,fragmented_restores;
    int iso_verified,recovery_state;
    uint64_t bytes_read,bytes_written;
} DndDefragBatchResult;
/* Native P4 coordinator, unbound to GameCube. All records must have authoritative
 * full SHA-256 manifests from execution preflight; Plan remains read-only.
 * Persist and reread the SAME immutable manifest on both physical devices
 * before offloading any file. Unique per-file P3 journals survive interruption
 * independently; repeat invocation resumes them, never recreates an operation.
 * Restore blockers only after a physically contiguous ISO is verified.
 * Fragmented blocker restores are explicitly reported, never ISO success.
 */
int dnd_defrag_batch_run(DndDefragBatchOps,const DndDefragBatch *,
                           void *scratch,size_t capacity,DndDefragBatchResult *);
#endif
