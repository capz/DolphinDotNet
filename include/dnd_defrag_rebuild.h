#ifndef DND_DEFRAG_REBUILD_H
#define DND_DEFRAG_REBUILD_H
#include "dnd_defrag_planner.h"

enum {
    DND_REBUILD_NEEDS_OFFLINE_ACCEPTANCE=1,
    DND_REBUILD_NEEDS_SCRATCH=2,
    DND_REBUILD_SCRATCH_CAPACITY=3,
    DND_REBUILD_UNKNOWN_ALLOCATION=4,
    DND_REBUILD_ACTIVE_TRANSACTION=5,
    DND_REBUILD_FILE_TOO_LARGE=6,
    DND_REBUILD_DEVICE_GUARD=7,
    DND_REBUILD_UNSUPPORTED_NAME=8
};
enum {DND_REBUILD_METADATA_REVIEW=1};
typedef struct {
    uint32_t file,flags;
    uint64_t bytes,scratch_clusters,target_clusters;
} DndDefragRebuildEntry;
typedef struct {
    int can_back_up,can_execute,reason;
    size_t file_count,directory_count;
    uint64_t backup_bytes,scratch_peak_clusters,target_restore_clusters;
    uint64_t bytes_read,bytes_written,target_fingerprint,scratch_fingerprint;
} DndDefragRebuildPlan;
/* Read-only whole-card backup inventory and rebuild order. All regular files,
 * including read-only files and GCI saves, must be backed up; directories are
 * retained in the snapshot as exact UTF-16 paths/parents. ISO restores first,
 * then largest allocations first, then stable file index. No format/delete API
 * is exposed. can_execute is always zero until offline format/metadata/backend
 * acceptance and a published consumer exist. This is explicitly not a hidden
 * fallback in ordinary Execute. Caller budgets journal/manifest overhead via
 * reserve; backup requires distinct writable physical scratch media.
 */
int dnd_defrag_rebuild_plan(const DndDefragSnapshot *target,uint32_t iso,
                             const DndDefragSnapshot *scratch,uint64_t reserve,
                             int mounted_iso,int live_handles,
                             DndDefragRebuildEntry *entries,size_t capacity,
                             DndDefragRebuildPlan *plan);
#endif
