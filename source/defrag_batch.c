#include "dnd_defrag_batch.h"
#include <errno.h>
#include <string.h>

static int checked(int rc) {return rc>0?-EIO:rc;}
static int digest_present(const unsigned char *p) {
    unsigned char bits=0;for(unsigned i=0;i<32;i++)bits|=p[i];return bits!=0;
}
static int record_valid(const DndEvacRecord *r) {
    unsigned char wire[DND_EVAC_WIRE_BYTES];
    return r && !dnd_evac_encode(r,wire) && r->sequence==1 &&
           r->state==DND_EVAC_PREPARED && digest_present(r->file_sha256);
}
static int same_volume(const DndEvacRecord *a,const DndEvacRecord *b) {
    return a->target_id[0]==b->target_id[0] && a->target_id[1]==b->target_id[1] &&
           a->scratch_id[0]==b->scratch_id[0] && a->scratch_id[1]==b->scratch_id[1] &&
           a->target_partition==b->target_partition && a->scratch_partition==b->scratch_partition &&
           a->target_serial==b->target_serial && a->scratch_serial==b->scratch_serial &&
           a->target_cluster_bytes==b->target_cluster_bytes &&
           a->scratch_cluster_bytes==b->scratch_cluster_bytes;
}
static int duplicate(const DndEvacRecord *a,const DndEvacRecord *b) {
    return !strcmp(a->relative_path,b->relative_path) ||
           (a->operation[0]==b->operation[0] && a->operation[1]==b->operation[1]);
}
static const DndEvacRecord *record(const DndDefragBatch *b,size_t at) {
    return at?b->blockers+at-1:&b->iso;
}
static int valid(const DndDefragBatch *b) {
    if(!b || !(b->operation[0]|b->operation[1]) || !record_valid(&b->iso) ||
       (!b->blockers && b->blocker_count) || b->blocker_count>UINT32_MAX-2 ||
       b->plan.start<2 || !b->plan.clusters ||
       b->plan.start>UINT32_MAX-b->plan.clusters ||
       b->plan.clusters!=b->iso.bytes/b->iso.target_cluster_bytes+
                          (b->iso.bytes%b->iso.target_cluster_bytes!=0) ||
       b->plan.move_count!=b->blocker_count+(b->evacuate_iso!=0) ||
       b->iso.strategy!=DND_DEFRAG_EVACUATE_ISO)return 0;
    if((!b->plan.move_count && b->plan.strategy!=DND_DEFRAG_DIRECT) ||
       (b->blocker_count && b->plan.strategy!=DND_DEFRAG_EVACUATE_BLOCKERS) ||
       (!b->blocker_count && b->evacuate_iso && b->plan.strategy!=DND_DEFRAG_EVACUATE_ISO))return 0;
    for(size_t i=0;i<b->blocker_count;i++) {
        const DndEvacRecord *r=b->blockers+i;
        if(!record_valid(r) || !same_volume(&b->iso,r) || duplicate(&b->iso,r) ||
           r->strategy!=DND_DEFRAG_EVACUATE_BLOCKERS)return 0;
        for(size_t j=0;j<i;j++)if(duplicate(b->blockers+j,r))return 0;
    }
    return 1;
}
static void put64(unsigned char *p,uint64_t x) {
    for(unsigned i=0;i<8;i++)p[i]=(unsigned char)(x>>(8*i));
}
static int manifest_wire(const DndDefragBatch *b,size_t at,unsigned char *out) {
    if(at)return dnd_evac_encode(record(b,at-1),out);
    memset(out,0,DND_EVAC_WIRE_BYTES);memcpy(out,"DNDBATCH1",9);
    put64(out+16,b->operation[0]);put64(out+24,b->operation[1]);
    put64(out+32,b->blocker_count+1);put64(out+40,b->evacuate_iso!=0);
    put64(out+48,b->plan.start);put64(out+56,b->plan.clusters);
    put64(out+64,b->plan.target_fingerprint);put64(out+72,b->plan.scratch_fingerprint);
    put64(out+80,(uint64_t)b->plan.strategy);
    DndSha256 sha;dnd_sha256_init(&sha);
    for(size_t i=0;i<b->blocker_count+1;i++) {
        unsigned char wire[DND_EVAC_WIRE_BYTES];
        int rc=dnd_evac_encode(record(b,i),wire);if(rc)return rc;
        dnd_sha256_update(&sha,wire,sizeof(wire));
    }
    dnd_sha256_finish(&sha,out+96);
    dnd_sha256_init(&sha);dnd_sha256_update(&sha,out,DND_EVAC_WIRE_BYTES-32);
    dnd_sha256_finish(&sha,out+DND_EVAC_WIRE_BYTES-32);return 0;
}
static int guard(DndDefragBatchOps ops,const DndDefragBatch *b) {
    if(ops.cancelled && ops.cancelled(ops.context))return -ECANCELED;
    return checked(ops.guard(ops.context,b));
}
static int manifest(DndDefragBatchOps ops,const DndDefragBatch *b) {
    unsigned char expected[DND_EVAC_WIRE_BYTES],actual[DND_EVAC_WIRE_BYTES];
    /* Check BOTH existing manifests for conflicts before writing either. */
    for(unsigned pass=0;pass<3;pass++)for(unsigned v=0;v<2;v++) {
        int rc=guard(ops,b);if(rc)return rc;
        for(size_t i=0;i<b->blocker_count+2;i++) {
            rc=manifest_wire(b,i,expected);if(rc)return rc;
            rc=ops.read_manifest(ops.context,v,i,actual);
            if(rc==-ENOENT && pass==0)continue;
            if(rc==-ENOENT && pass==1)
                rc=checked(ops.write_manifest(ops.context,v,i,expected));
            else if(!rc && memcmp(expected,actual,sizeof(expected)))return -ESTALE;
            if(rc)return checked(rc);
        }
        if(pass==1) {rc=checked(ops.sync_volume(ops.context,v));if(rc)return rc;}
    }
    return 0;
}
static int file_run(DndDefragBatchOps ops,const DndDefragBatch *b,size_t index,
                      int keep,void *scratch,size_t cap,DndDefragBatchResult *out) {
    int rc=guard(ops,b);if(rc)return rc;
    DndEvacResult result;
    rc=dnd_evac_run(ops.journal(ops.context,index),ops.files(ops.context,index),
                       record(b,index),keep,scratch,cap,&result);
    out->bytes_read+=result.io.bytes_read;out->bytes_written+=result.io.bytes_written;
    out->recovery_state=result.recovery_state;return rc;
}
int dnd_defrag_batch_run(DndDefragBatchOps ops,const DndDefragBatch *b,
                           void *scratch,size_t cap,DndDefragBatchResult *out) {
    if(!out)return -EINVAL;
    memset(out,0,sizeof(*out));
    if(!valid(b) || !scratch || (uintptr_t)scratch%32 || cap<DND_COPY_BUFFER ||
       !ops.guard || !ops.read_manifest || !ops.write_manifest || !ops.sync_volume ||
       !ops.journal || !ops.files || !ops.window_free || !ops.verify_iso ||
       !ops.direct_iso || !ops.blocker_extents)return -EINVAL;
    int rc=guard(ops,b);if(rc)return rc;
    rc=manifest(ops,b);if(rc)return rc;
    if(b->evacuate_iso) {
        rc=file_run(ops,b,0,1,scratch,cap,out);if(rc)return rc;
        out->evacuated++;
    }
    for(size_t i=0;i<b->blocker_count;i++) {
        rc=file_run(ops,b,i+1,1,scratch,cap,out);if(rc)return rc;
        out->evacuated++;
    }
    rc=checked(ops.verify_iso(ops.context,&b->iso));
    if(rc==-EAGAIN || rc==-ENOENT) {
        rc=guard(ops,b);if(rc)return rc;
        rc=checked(ops.window_free(ops.context,b->plan.start,b->plan.clusters));if(rc)return rc;
        if(b->evacuate_iso)rc=file_run(ops,b,0,0,scratch,cap,out);
        else {
            DndCopyResult result={0};rc=checked(ops.direct_iso(ops.context,&b->iso,&result));
            out->bytes_read+=result.bytes_read;out->bytes_written+=result.bytes_written;
        }
        if(rc)return rc;
        rc=checked(ops.verify_iso(ops.context,&b->iso));
    }
    if(rc)return rc;
    out->iso_verified=1;
    for(size_t i=0;i<b->blocker_count;i++) {
        rc=guard(ops,b);if(rc)return rc;
        /* Revalidate published ISO before every blocker restoration. */
        rc=checked(ops.verify_iso(ops.context,&b->iso));if(rc)return rc;
        rc=file_run(ops,b,i+1,0,scratch,cap,out);if(rc)return rc;
        unsigned count=0;rc=checked(ops.blocker_extents(ops.context,i,&count));
        if(rc)return rc;
        if(!count)return -EIO;
        out->fragmented_restores+=count!=1;out->restored++;
    }
    return 0;
}
