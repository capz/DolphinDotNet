#ifndef DND_EVACUATION_H
#define DND_EVACUATION_H
#include "dnd_evacuation_journal.h"

enum {DND_EVAC_ORIGINAL=0,DND_EVAC_SCRATCH=1,DND_EVAC_TARGET_STAGE=2};
typedef struct {
    void *context;
    /* Hold both physical-device mutation locks for the WHOLE invocation.
     * Check writable firmware, DVD conflicts, active handles and media IDs.
     * Source absence after release is allowed; foreign handles never are. */
    int (*guard)(void *,const DndEvacRecord *);
    /* Called before initial PREPARED journals. Reject staging collisions on
     * BOTH volumes. After PREPARED, only this operation owns those names. */
    int (*stages_absent)(void *,const DndEvacRecord *);
    /* direction 0: original -> scratch stage; 1: scratch -> target stage.
     * Use filesystem-managed create/reservation. Reuse only this journal's
     * own stage on resume. Restore metadata, validate lengths/cookies, and
     * expose exact I/O callbacks; checkpoint is supplied by the engine. */
    int (*open_copy)(void *,const DndEvacRecord *,unsigned direction,DndCopyOps *);
    int (*close_copy)(void *);
    /* Reopen and verify full SHA-256/length. Original/target-stage checks
     * additionally require physical extent count == 1 after restoration.
     * Before source release, original need only match its original bytes.
     * No symlinks, special files, unverified overwrite or journal path trust.
     * Return -ENOENT only if the exact expected file is absent. */
    int (*verify)(void *,const DndEvacRecord *,unsigned location);
    int (*release_source)(void *,const DndEvacRecord *);
    /* Create-new publish at original path; never overwrite an existing file. */
    int (*publish)(void *,const DndEvacRecord *);
    /* Only the deterministic internal scratch stage is eligible for deletion. */
    int (*remove_scratch)(void *,const DndEvacRecord *);
    int (*cancelled)(void *);
} DndEvacFileOps;
typedef struct {
    int recovery_state;
    DndEvacRecord record;
    DndCopyResult io;
} DndEvacResult;
/* Isolated native protocol engine; no GameCube backend or managed bridge is
 * enabled. expected is the trusted request (PREPARED, sequence 1), independently
 * bound to canonical source and device IDs, not taken blindly from a journal.
 * The engine always re-verifies copies before destructive transitions.
 * keep_evacuated pauses safely at SOURCE_RELEASED for P4 orchestration.
 * NeedsScratch/Target are positive outcomes, errors negative errno. Cancellation
 * preserves recovery state and every remaining verified copy. Repeated calls
 * resume idempotently; no callback may bypass its specified filesystem guards.
 */
int dnd_evac_run(DndEvacJournalOps journal,DndEvacFileOps files,
                   const DndEvacRecord *expected,int keep_evacuated,
                   void *scratch,size_t capacity,DndEvacResult *result);
#endif
