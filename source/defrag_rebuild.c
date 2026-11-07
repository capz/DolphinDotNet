#include "dnd_defrag_rebuild.h"
#include <errno.h>
#include <limits.h>
#include <string.h>

static uint64_t clusters(uint64_t bytes,uint32_t size) {
    return bytes/size+(bytes%size!=0);
}
static int internal_name(const uint16_t *name) {
    return name[0]=='.' && name[1]=='_' && name[2]=='_';
}
static int before(DndDefragRebuildEntry a,DndDefragRebuildEntry b,uint32_t iso) {
    if(a.file==iso || b.file==iso)return a.file==iso;
    if(a.target_clusters!=b.target_clusters)return a.target_clusters>b.target_clusters;
    return a.file<b.file;
}
int dnd_defrag_rebuild_plan(const DndDefragSnapshot *target,uint32_t iso,
                             const DndDefragSnapshot *scratch,uint64_t reserve,
                             int mounted_iso,int live_handles,
                             DndDefragRebuildEntry *entries,size_t capacity,
                             DndDefragRebuildPlan *out) {
    if(!out || !target || !target->valid || !target->files || !target->runs ||
       !target->run_count || target->file_count>=DND_DEFRAG_SYSTEM ||
       !(target->device.physical_id[0]|target->device.physical_id[1]) ||
       !target->space.total_clusters || target->space.total_clusters>UINT32_MAX-2u ||
       target->space.bytes_per_cluster<512 || target->space.bytes_per_cluster>16777216 ||
       (target->space.bytes_per_cluster&(target->space.bytes_per_cluster-1)) ||
       iso>=target->file_count || !target->files[iso].bytes ||
       (target->files[iso].flags&DND_DEFRAG_DIRECTORY) ||
       (!entries && capacity) || (scratch && (!scratch->valid ||
       scratch->space.bytes_per_cluster<512 || scratch->space.bytes_per_cluster>16777216 ||
       (scratch->space.bytes_per_cluster&(scratch->space.bytes_per_cluster-1)) ||
       scratch->space.free_clusters>scratch->space.total_clusters)))return -EINVAL;
    DndDefragRebuildPlan p={0};p.target_fingerprint=target->fingerprint;
    p.reason=DND_REBUILD_NEEDS_OFFLINE_ACCEPTANCE;
    if(!target->device.writable || mounted_iso || live_handles) {
        p.reason=DND_REBUILD_DEVICE_GUARD;*out=p;return 0;
    }
    /* Unknown/lost chains are data too. A file-only backup cannot justify
     * erasing them, even if an attractive ISO window could be recreated. */
    uint64_t end=2,free_count=0;
    for(size_t i=0;i<target->run_count;i++) {
        const DndDefragRun *r=target->runs+i;
        if(!r->count || r->start!=end || end>target->space.total_clusters+2 ||
           r->count>target->space.total_clusters+2-end)return -EINVAL;
        if(r->owner==DND_DEFRAG_UNKNOWN) {
            p.reason=DND_REBUILD_UNKNOWN_ALLOCATION;*out=p;return 0;
        }
        if(r->owner>=target->file_count && r->owner!=DND_DEFRAG_FREE &&
           r->owner!=DND_DEFRAG_SYSTEM)return -EINVAL;
        end+=r->count;
        if(r->owner==DND_DEFRAG_FREE)free_count+=r->count;
    }
    if(end!=target->space.total_clusters+2 || free_count!=target->space.free_clusters)
        return -EINVAL;
    for(size_t i=0;i<target->file_count;i++) {
        const DndDefragFile *file=target->files+i;
        if(file->flags&DND_DEFRAG_NAME_UNSUPPORTED) {
            p.reason=DND_REBUILD_UNSUPPORTED_NAME;*out=p;return 0;
        }
        if(internal_name(file->name)) {
            p.reason=DND_REBUILD_ACTIVE_TRANSACTION;*out=p;return 0;
        }
        if(file->flags&DND_DEFRAG_DIRECTORY) {p.directory_count++;continue;}
        if(file->bytes>INT32_MAX) {
            p.reason=DND_REBUILD_FILE_TOO_LARGE;*out=p;return 0;
        }
        uint64_t allocated=0;
        for(size_t j=0;j<target->run_count;j++)
            if(target->runs[j].owner==i)allocated+=target->runs[j].count;
        if(allocated!=clusters(file->bytes,target->space.bytes_per_cluster))return -EINVAL;
        p.file_count++;p.backup_bytes+=file->bytes;
        p.target_restore_clusters+=clusters(file->bytes,target->space.bytes_per_cluster);
        if(scratch)p.scratch_peak_clusters+=clusters(file->bytes,scratch->space.bytes_per_cluster);
    }
    if(!scratch || !scratch->device.writable ||
       !(scratch->device.physical_id[0]|scratch->device.physical_id[1]) ||
       (scratch->device.physical_id[0]==target->device.physical_id[0] &&
        scratch->device.physical_id[1]==target->device.physical_id[1])) {
        p.reason=DND_REBUILD_NEEDS_SCRATCH;*out=p;return 0;
    }
    p.scratch_fingerprint=scratch->fingerprint;
    if(reserve>scratch->space.free_clusters ||
       p.scratch_peak_clusters>scratch->space.free_clusters-reserve) {
        p.reason=DND_REBUILD_SCRATCH_CAPACITY;*out=p;return 0;
    }
    p.scratch_peak_clusters+=reserve;
    /* Backup and restoration each write once and perform source + reopened
     * destination verification reads. Additional platform validation is extra. */
    p.bytes_written=2*p.backup_bytes;p.bytes_read=4*p.backup_bytes;
    if(p.file_count>capacity)return -ENOSPC;
    size_t n=0;
    for(size_t i=0;i<target->file_count;i++) {
        const DndDefragFile *f=target->files+i;
        if(f->flags&DND_DEFRAG_DIRECTORY)continue;
        DndDefragRebuildEntry e={(uint32_t)i,
            (f->flags&DND_DEFRAG_PINNED)?DND_REBUILD_METADATA_REVIEW:0,
            f->bytes,clusters(f->bytes,scratch->space.bytes_per_cluster),
            clusters(f->bytes,target->space.bytes_per_cluster)};
        size_t at=n;
        while(at && before(e,entries[at-1],iso)) {entries[at]=entries[at-1];at--;}
        entries[at]=e;n++;
    }
    p.can_back_up=1;*out=p;return 0;
}
