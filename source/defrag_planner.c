#include "dnd_defrag_planner.h"
#include <errno.h>
#include <limits.h>
#include <string.h>

static uint64_t clusters(uint64_t bytes,uint32_t size) {
    return bytes/size+(bytes%size!=0);
}
static int overlap(const DndDefragRun *r,uint64_t start,uint64_t count) {
    return r->start<start+count && start<r->start+r->count;
}
static int touches(const DndDefragSnapshot *s,uint32_t file,
                     uint64_t start,uint64_t count) {
    for(size_t i=0;i<s->run_count;i++)
        if(s->runs[i].owner==file && overlap(&s->runs[i],start,count))return 1;
    return 0;
}
static int independent(const DndDefragSnapshot *a,const DndDefragSnapshot *b) {
    return b && b->valid && b->device.writable &&
           (b->device.physical_id[0]|b->device.physical_id[1]) &&
           (a->device.physical_id[0]!=b->device.physical_id[0] ||
            a->device.physical_id[1]!=b->device.physical_id[1]);
}
/* Validate the immutable map too: callers can construct synthetic snapshots,
 * but a missing/free/overlapping interval must never imply a feasible plan. */
static int valid_map(const DndDefragSnapshot *s) {
    if(!s || !s->valid || !s->files || !s->runs || !s->file_count ||
       !s->run_count || !s->space.bytes_per_cluster ||
       s->space.total_clusters>UINT32_MAX-2u ||
       !(s->device.physical_id[0]|s->device.physical_id[1]))return 0;
    uint64_t end=2,free_count=0;
    for(size_t i=0;i<s->run_count;i++) {
        const DndDefragRun *r=&s->runs[i];
        if(r->start!=end || !r->count ||
           r->count>s->space.total_clusters+2-end ||
           (r->owner<s->file_count?0:r->owner<DND_DEFRAG_SYSTEM))return 0;
        if(r->owner==DND_DEFRAG_FREE)free_count+=r->count;
        end+=r->count;
    }
    if(end!=s->space.total_clusters+2 || free_count!=s->space.free_clusters)
        return 0;
    for(size_t f=0;f<s->file_count;f++) {
        if(s->files[f].flags&DND_DEFRAG_DIRECTORY)continue;
        uint64_t count=0;
        for(size_t i=0;i<s->run_count;i++)
            if(s->runs[i].owner==f)count+=s->runs[i].count;
        if(count!=clusters(s->files[f].bytes,s->space.bytes_per_cluster))return 0;
    }
    return 1;
}
typedef struct {
    const DndDefragSnapshot *target,*scratch;
    uint64_t required,reserve;
    uint32_t source;
    int scratch_ok,found;
    DndDefragPlan best;
} Search;
static int better(const DndDefragPlan *a,const DndDefragPlan *b) {
    if(a->evacuation_bytes!=b->evacuation_bytes)
        return a->evacuation_bytes<b->evacuation_bytes;
    if(a->move_count!=b->move_count)return a->move_count<b->move_count;
    if(a->bytes_written!=b->bytes_written)return a->bytes_written<b->bytes_written;
    if(a->start!=b->start)return a->start<b->start;
    return a->strategy<b->strategy;
}
static void candidate(Search *q,uint64_t start,int evacuate_source) {
    const DndDefragSnapshot *s=q->target;
    if(start<2 || start>s->space.total_clusters+2-q->required)return;
    DndDefragPlan p={0};
    p.start=start;p.clusters=q->required;p.source=q->source;
    uint64_t moved_clusters=0;
    for(size_t i=0;i<s->run_count;i++) {
        const DndDefragRun *r=&s->runs[i];
        if(!overlap(r,start,q->required) || r->owner==DND_DEFRAG_FREE)continue;
        if(r->owner>=s->file_count ||
           (s->files[r->owner].flags&(DND_DEFRAG_PINNED|DND_DEFRAG_DIRECTORY)) ||
           (r->owner==q->source && !evacuate_source))return;
    }
    for(size_t i=0;i<s->file_count;i++) {
        if(!((i==q->source && evacuate_source) ||
             touches(s,(uint32_t)i,start,q->required)))continue;
        if(s->files[i].flags&(DND_DEFRAG_PINNED|DND_DEFRAG_DIRECTORY))return;
        if(s->files[i].bytes>INT32_MAX)return;
        p.evacuation_bytes+=s->files[i].bytes;
        moved_clusters+=clusters(s->files[i].bytes,s->space.bytes_per_cluster);
        p.move_count++;
        if(q->scratch_ok)
            p.scratch_peak_clusters+=clusters(s->files[i].bytes,
                                               q->scratch->space.bytes_per_cluster);
    }
    if(p.move_count) {
        if(!q->scratch_ok || q->reserve>q->scratch->space.free_clusters ||
           p.scratch_peak_clusters>q->scratch->space.free_clusters-q->reserve)return;
        p.scratch_peak_clusters+=q->reserve;
    }
    if(s->space.free_clusters+moved_clusters<q->required)return;
    p.strategy=!p.move_count?DND_DEFRAG_DIRECT:
        evacuate_source && p.move_count==1?DND_DEFRAG_EVACUATE_ISO:
        DND_DEFRAG_EVACUATE_BLOCKERS;
    p.target_peak_clusters=q->required>moved_clusters?q->required-moved_clusters:0;
    /* ISO evacuation replaces the normal source->stage copy. A blocker is
     * copied out AND back; each verified copy writes once and reads twice
     * (source stream and reopened destination verification). */
    uint64_t transfer=s->files[q->source].bytes+2*p.evacuation_bytes;
    if(evacuate_source)transfer-=s->files[q->source].bytes;
    p.bytes_written=transfer;p.bytes_read=2*transfer;
    p.target_fingerprint=s->fingerprint;
    p.scratch_fingerprint=p.move_count?q->scratch->fingerprint:0;
    if(!q->found || better(&p,&q->best)) {q->best=p;q->found=1;}
}
static void at(Search *s,uint64_t start) {
    candidate(s,start,0);
    if(s->scratch_ok)candidate(s,start,1);
}
int dnd_defrag_plan(const DndDefragSnapshot *target,uint32_t source,
                     const DndDefragSnapshot *scratch,uint64_t reserve,
                     int mounted_iso,int live_source,
                     DndDefragMove *moves,size_t capacity,DndDefragPlan *out) {
    if(!out || !valid_map(target) || source>=target->file_count ||
       (scratch && !valid_map(scratch)) || (!moves && capacity))return -EINVAL;
    DndDefragPlan none={0};none.source=source;
    const DndDefragFile *iso=&target->files[source];
    if(iso->bytes>INT32_MAX)return -EFBIG;
    if(!iso->bytes || iso->flags&DND_DEFRAG_DIRECTORY)return -EINVAL;
    if(!target->device.writable || mounted_iso || live_source ||
       iso->flags&DND_DEFRAG_PINNED) {
        none.reason=DND_DEFRAG_REASON_GUARD;*out=none;return 0;
    }
    Search search={0};
    search.target=target;search.scratch=scratch;search.source=source;
    search.required=clusters(iso->bytes,target->space.bytes_per_cluster);
    search.reserve=reserve;search.scratch_ok=independent(target,scratch);
    if(search.required>target->space.total_clusters) {
        none.reason=DND_DEFRAG_REASON_CAPACITY;*out=none;return 0;
    }
    at(&search,2);
    /* Occupying-file sets change only at r.end and r.start-N+1. Testing
     * these exact integer boundaries is equivalent to testing every cluster,
     * including windows whose ends are within an existing extent. */
    for(size_t i=0;i<target->run_count;i++) {
        const DndDefragRun *r=&target->runs[i];
        at(&search,r->start+r->count);
        if(r->start>=search.required+1)at(&search,r->start-search.required+1);
    }
    if(!search.found) {
        none.reason=search.scratch_ok?DND_DEFRAG_REASON_PINNED:DND_DEFRAG_REASON_SCRATCH;
        *out=none;return 0;
    }
    if(search.best.move_count>capacity)return -ENOSPC;
    size_t n=0;
    /* A source evacuation is charged even when the source lies outside the
     * chosen interval. Infer it from the window or the ISO-only strategy. */
    int source_moved=search.best.strategy==DND_DEFRAG_EVACUATE_ISO ||
        touches(target,source,search.best.start,search.required);
    if(source_moved) {
        moves[n++]=(DndDefragMove){source,iso->bytes,
            clusters(iso->bytes,scratch->space.bytes_per_cluster)};
    }
    for(size_t i=0;i<target->file_count;i++) {
        if(i==source || !touches(target,(uint32_t)i,search.best.start,search.required))
            continue;
        moves[n++]=(DndDefragMove){(uint32_t)i,target->files[i].bytes,
            clusters(target->files[i].bytes,scratch->space.bytes_per_cluster)};
    }
    *out=search.best;
    return 0;
}
