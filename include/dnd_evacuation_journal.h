#ifndef DND_EVACUATION_JOURNAL_H
#define DND_EVACUATION_JOURNAL_H
#include <stdint.h>
#include "dnd_defrag_planner.h"
#include "dnd_verified_copy.h"

/* P3 protocol foundation, deliberately not a filesystem executor. Platform
 * bindings, physical acceptance and managed Execute remain disabled. No API
 * here deletes, publishes, formats or performs raw filesystem metadata writes.
 * The caller locks both physical devices and revalidates the surviving copy
 * before EVERY authority transition. Journals are integrity checked, not
 * authenticated: a journal path is never sufficient authorization to delete.
 */
#define DND_EVAC_WIRE_BYTES 1536u
#define DND_EVAC_PATH 1024u
enum {
    DND_EVAC_PREPARED=1, DND_EVAC_COPYING=2, DND_EVAC_SCRATCH_VERIFIED=3,
    DND_EVAC_RELEASE_READY=4, DND_EVAC_SOURCE_RELEASED=5,
    DND_EVAC_RESTORING=6, DND_EVAC_TARGET_VERIFIED=7,
    DND_EVAC_PUBLISH_READY=8, DND_EVAC_PUBLISHED=9, DND_EVAC_DONE=10,
    DND_EVAC_ABORTED=11
};
enum {
    DND_EVAC_CLEAN=0, DND_EVAC_PENDING=1, DND_EVAC_RECONCILE=2,
    DND_EVAC_NEEDS_SCRATCH=3, DND_EVAC_NEEDS_TARGET=4
};
typedef struct {
    uint64_t operation[2],sequence;
    uint64_t target_id[2],scratch_id[2],target_partition,scratch_partition;
    uint64_t target_serial,scratch_serial,bytes,mtime,atime,durable_bytes;
    uint32_t state,strategy,attributes,target_cluster_bytes,scratch_cluster_bytes;
    unsigned char prefix_sha256[32],file_sha256[32];
    char relative_path[DND_EVAC_PATH];
} DndEvacRecord;
typedef struct {
    void *context;
    /* volume 0 is target, 1 scratch. Identity MUST come from the mounted
     * physical device layer and include the actual partition and serial. */
    int (*probe)(void *,unsigned volume,uint64_t id[2],uint64_t *partition,
                  uint64_t *serial);
    /* Two deterministic reserved slots per volume. -ENOENT means no slot;
     * every successful read returns EXACTLY WIRE_BYTES. -ENODEV means absent.
     * Never follow symlinks or overwrite an unrelated/colliding journal. */
    int (*read_slot)(void *,unsigned volume,unsigned slot,void *wire);
    int (*write_slot)(void *,unsigned volume,unsigned slot,const void *wire);
    int (*sync_volume)(void *,unsigned volume);
} DndEvacJournalOps;
int dnd_evac_encode(const DndEvacRecord *,unsigned char wire[DND_EVAC_WIRE_BYTES]);
int dnd_evac_decode(const unsigned char wire[DND_EVAC_WIRE_BYTES],DndEvacRecord *);
/* Read-only. Conflicting operations, corrupt slots without a valid older
 * record, skipped sequences and illegal transitions fail closed. On missing
 * media, returns NeedsScratch/Target without touching the present volume. */
int dnd_evac_inspect(DndEvacJournalOps,DndEvacRecord *latest);
/* Create-new PREPARED sequence 1 on both volumes. Alternating slots preserve
 * the previous checkpoint across torn writes. Writes are synced and reread. */
int dnd_evac_begin(DndEvacJournalOps,const DndEvacRecord *);
/* Repair only the latest validated adjacent checkpoint; never advance while
 * replicas disagree. This is idempotent and performs journal writes only. */
int dnd_evac_reconcile(DndEvacJournalOps,const DndEvacRecord *latest);
int dnd_evac_advance(DndEvacJournalOps,const DndEvacRecord *previous,
                      const DndEvacRecord *next);
/* Staging names depend solely on the operation ID, never on journal paths. */
int dnd_evac_stage_leaf(const DndEvacRecord *,char *out,unsigned capacity);
#endif
