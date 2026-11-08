#ifndef DND_RAW_MOVE_H
#define DND_RAW_MOVE_H
#include "dnd_defrag_planner.h"
#include "dnd_verified_copy.h"

/* EXPERIMENTAL, image-only. No normal storage/Defrag API calls this module.
 * Physical device adapters are refused until independent review and actual
 * hardware acceptance exist. The trusted owner keeps the image exclusively
 * offline and supplies an independently verified backup and trusted request.
 */
#define DND_RAW_SECTOR 512u
#define DND_RAW_REQUEST_BYTES 512u
#define DND_RAW_PATCH_BYTES 2048u
#define DND_RAW_CHECKPOINT_BYTES 512u
#define DND_RAW_IMAGE 0x494d4731u
#define DND_RAW_DEVICE 0x52415731u

enum {
    DND_RAW_PREPARED=1,DND_RAW_DESTINATION_VERIFIED=2,DND_RAW_METADATA_INTENT=3,
    DND_RAW_METADATA_APPLIED=4,DND_RAW_COMMITTED=5,DND_RAW_CLEANUP_INTENT=6,
    DND_RAW_WINDOW_COMPLETE=7,DND_RAW_ROLLED_BACK=8
};
enum {DND_RAW_NONE=0,DND_RAW_PENDING=1,DND_RAW_NEEDS_PARTNER=2};
typedef struct {
    uint64_t operation[2],image_id[2],partition_lba,sector_count,generation;
    uint64_t entry_lba,file_bytes,logical_first,cluster_count;
    uint32_t destination,original_first,file_flags,filesystem,bytes_per_cluster;
    uint16_t entry_offset,attributes;
    unsigned char metadata_sha256[32],file_sha256[32],window_sha256[32];
    unsigned char boot_sha256[32],backup_sha256[32];
} DndRawMove;
typedef struct {
    uint64_t lba;
    _Alignas(32) unsigned char before[512],applied[512],cleaned[512];
} DndRawPatch;
typedef struct {
    uint64_t sequence,patch_count;
    uint32_t state;
    unsigned char request_sha256[32],patches_sha256[32];
} DndRawCheckpoint;
typedef struct {
    uint64_t data_bytes_read,data_bytes_written,metadata_sectors_written;
    uint64_t journal_bytes_written;
    uint32_t state,before_extents,after_extents;
    int recovered,rolled_back;
} DndRawResult;
typedef struct {
    void *context;
    uint32_t medium_kind;
    /* All exact callbacks return zero or -errno. Sector buffers align to 32.
     * guard establishes exclusive offline ownership, image identity/size,
     * immutable backup binding and no mounted/foreign consumers, on every I/O.
     * invalidate discards every cache before readback and on recovery entry. */
    int (*guard)(void *,const DndRawMove *);
    int (*read_sector)(void *,uint64_t,void *);
    int (*write_sector)(void *,uint64_t,const void *);
    int (*sync_image)(void *);
    int (*invalidate)(void *);
    /* Records are canonical byte strings, never native structure layouts.
     * kind: 0=request(index 0), 1=patch(index), 2=checkpoint(slot 0/1).
     * -ENOENT is absent; -ENODEV means replica unavailable. A torn/corrupt
     * existing record is not absence. Immutable request/patch records may only
     * be created or confirmed byte-identical, never overwritten in recovery. */
    int (*read_record)(void *,unsigned replica,unsigned kind,uint64_t index,void *,size_t);
    int (*write_record)(void *,unsigned replica,unsigned kind,uint64_t index,const void *,size_t);
    int (*sync_journal)(void *,unsigned replica);
    /* Preparation/validation scratch: bounded RAM, spill to external storage.
     * reset clears ONLY this operation's temporary builder, never either
     * authoritative replica. get: -ENOENT if LBA not in the builder.
     * put coalesces a repeated LBA and preserves first-insertion order.
     * count/read enumerate that deterministic order. */
    int (*build_reset)(void *);
    int (*build_get)(void *,uint64_t,DndRawPatch *);
    int (*build_put)(void *,const DndRawPatch *);
    int (*build_count)(void *,uint64_t *);
    int (*build_read)(void *,uint64_t,DndRawPatch *);
    /* Optional acceleration only: return an ordinal, verified by the engine.
     * Must never turn a missing/corrupt replica into trusted patch data. */
    int (*find_patch)(void *,unsigned replica,uint64_t lba,uint64_t *index);
    int (*cancelled)(void *);
} DndRawOps;

/* These codecs reject noncanonical padding, invalid ranges and corruption. */
int dnd_raw_request_encode(const DndRawMove *,unsigned char out[DND_RAW_REQUEST_BYTES]);
int dnd_raw_request_decode(const unsigned char in[DND_RAW_REQUEST_BYTES],DndRawMove *);
int dnd_raw_patch_encode(const DndRawPatch *,uint64_t index,const unsigned char request_hash[32],
                           unsigned char out[DND_RAW_PATCH_BYTES]);
int dnd_raw_patch_decode(const unsigned char in[DND_RAW_PATCH_BYTES],uint64_t index,
                           const unsigned char request_hash[32],DndRawPatch *);
int dnd_raw_checkpoint_encode(const DndRawCheckpoint *,unsigned char out[512]);
int dnd_raw_checkpoint_decode(const unsigned char in[512],DndRawCheckpoint *);

/* Pure read-only preflight. workspace arrays have explicit caller capacities;
 * no volume-sized bitmap/file buffer. reader observes the same offline image
 * as ops; source logical length is limited to INT32_MAX, LBAs remain uint64.
 * Only a wholly free destination interval is permitted for a move window.
 * The caller sets operation and backup digest after plan and before execute. */
int dnd_raw_move_plan(DndRawOps,const DndSectorReader *,DndDefragDevice,
                        uint64_t sector_count,DndDefragSnapshot *workspace,
                        uint32_t file,uint64_t logical_first,uint64_t count,
                        uint32_t destination,DndRawMove *move,
                        void *transfer,size_t capacity);
int dnd_raw_move_inspect(DndRawOps,const DndRawMove *,DndRawCheckpoint *);
/* Both copies are sealed/read back before any image write. Existing operations
 * require explicit recovery; execute never silently overwrites their journal. */
int dnd_raw_move_execute(DndRawOps,const DndRawMove *,DndDefragSnapshot *,
                           void *transfer,size_t capacity,DndRawResult *);
/* Before commit: verified undo. Any valid committed marker: reconcile then
 * verify/finish redo. Missing/corrupt/foreign records fail without image writes.
 * expected must be independently trusted, not blindly decoded from a journal. */
int dnd_raw_move_recover(DndRawOps,const DndRawMove *,DndDefragSnapshot *,
                           void *transfer,size_t capacity,DndRawResult *);
/* No ordinary mount is permitted with a missing, corrupt or pending journal. */
int dnd_raw_move_mount_allowed(DndRawOps,const DndRawMove *);
#endif
