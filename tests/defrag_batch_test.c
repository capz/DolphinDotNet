#include "dnd_defrag_batch.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>
#include "evacuation_fixture.h"
#include "evacuation_model.h"

static struct World rows[3];
static Media journals[3];
static int selected=-1;
static struct Batch {
    unsigned char manifest[2][5][DND_EVAC_WIRE_BYTES];
    int exists[2][5],window_blocked,direct_done,evacuate_iso,fragmented;
    unsigned event,fail_at,direct_calls;
} batch;
static void save_row(void) {
    if(selected>=0) {rows[selected]=world;journals[selected]=media;}
}
static void select_row(size_t i) {
    if((int)i==selected)return;
    save_row();selected=(int)i;world=rows[i];media=journals[i];
}
static int batch_tick(void) {return ++batch.event==batch.fail_at?-EINTR:0;}
static void setup(void) {
    selected=-1;memset(&batch,0,sizeof(batch));
    for(unsigned i=0;i<3;i++) {reset();rows[i]=world;journals[i]=media;}
}
static int batch_guard(void *ctx,const DndDefragBatch *request) {
    (void)ctx;(void)request;save_row();return batch_tick();
}
static int manifest_read(void *ctx,unsigned v,size_t i,void *wire) {
    (void)ctx;assert(i<5);
    if(!batch.exists[v][i])return -ENOENT;
    memcpy(wire,batch.manifest[v][i],DND_EVAC_WIRE_BYTES);return batch_tick();
}
static int manifest_write(void *ctx,unsigned v,size_t i,const void *wire) {
    (void)ctx;assert(i<5 && !batch.exists[v][i]);
    batch.exists[v][i]=1;memcpy(batch.manifest[v][i],wire,DND_EVAC_WIRE_BYTES);return batch_tick();
}
static int manifest_sync(void *ctx,unsigned v) {(void)ctx;(void)v;return batch_tick();}
static DndEvacJournalOps batch_journal(void *ctx,size_t i) {
    (void)ctx;select_row(i);return journal();
}
static int batch_verify(void *ctx,const DndEvacRecord *r,unsigned location) {
    /* The ISO requires physical contiguity; blockers may restore fragmented,
     * with their extent count reported separately after SHA verification. */
    int fragmented=world.fragmented;
    if(r->strategy==DND_DEFRAG_EVACUATE_BLOCKERS)world.fragmented=0;
    int rc=verify(ctx,r,location);world.fragmented=fragmented;return rc;
}
static DndEvacFileOps batch_files(void *ctx,size_t i) {
    (void)ctx;select_row(i);DndEvacFileOps f=files();f.verify=batch_verify;return f;
}
static int window_free(void *ctx,uint64_t start,uint64_t clusters) {
    (void)ctx;assert(start==7 && clusters==4);save_row();
    for(unsigned i=1;i<3;i++)assert(!rows[i].exists[0] && rows[i].disk_exists[1]);
    if(batch.evacuate_iso)assert(!rows[0].exists[0]);
    return batch.window_blocked?-ENOSPC:batch_tick();
}
static int verify_iso(void *ctx,const DndEvacRecord *r) {
    (void)ctx;select_row(0);
    if(!world.exists[0])return -ENOENT;
    if(!world.source_deletes && !batch.direct_done)return -EAGAIN;
    return verify(NULL,r,0);
}
static int direct_iso(void *ctx,const DndEvacRecord *r,DndCopyResult *result) {
    (void)ctx;select_row(0);assert(world.exists[0]);
    assert(verify_data(NULL,r,0)==0);
    batch.direct_done=1;batch.direct_calls++;
    result->bytes_read=4*BYTES;result->bytes_written=BYTES;return batch_tick();
}
static int blocker_extents(void *ctx,size_t i,unsigned *extents) {
    (void)ctx;select_row(i+1);assert(world.exists[0]);
    *extents=batch.fragmented && i==1?2:1;return batch_tick();
}
static DndDefragBatchOps batch_ops(void) {
    return (DndDefragBatchOps){NULL,batch_guard,manifest_read,manifest_write,manifest_sync,
        batch_journal,batch_files,window_free,verify_iso,direct_iso,blocker_extents,NULL};
}
static int execute(const DndDefragBatch *b,DndDefragBatchResult *out) {
    int rc=dnd_defrag_batch_run(batch_ops(),b,scratch,sizeof(scratch),out);
    save_row();return rc;
}
static void reboot_all(void) {
    save_row();selected=-1;batch.fail_at=0;
    for(unsigned i=0;i<3;i++) {
        world=rows[i];media=journals[i];restart();rows[i]=world;journals[i]=media;
    }
}
int main(void) {
    (void)checkpoint_media;(void)next;(void)run;
    DndEvacRecord iso=base_record(),blockers[2];iso.bytes=BYTES;
    DndSha256 sha;dnd_sha256_init(&sha);reset();
    dnd_sha256_update(&sha,world.data[0],BYTES);dnd_sha256_finish(&sha,iso.file_sha256);
    blockers[0]=blockers[1]=iso;
    for(unsigned i=0;i<2;i++) {
        blockers[i].operation[0]+=i+1;blockers[i].strategy=DND_DEFRAG_EVACUATE_BLOCKERS;
        snprintf(blockers[i].relative_path,sizeof(blockers[i].relative_path),"games/blocker%u.bin",i);
    }
    DndDefragBatch b={.operation={91,92},
        .plan={.strategy=DND_DEFRAG_EVACUATE_BLOCKERS,.start=7,.clusters=4,.move_count=3},
        .iso=iso,.blockers=blockers,.blocker_count=2,.evacuate_iso=1};
    DndDefragBatchResult result;
    setup();batch.evacuate_iso=1;batch.fragmented=1;
    assert(execute(&b,&result)==0);
    assert(result.iso_verified && result.evacuated==3 && result.restored==2);
    assert(result.fragmented_restores==1 && result.bytes_written==6*BYTES);
    unsigned events=batch.event;
    /* Interrupt after every batch callback, then recover all per-file logs. */
    for(unsigned point=1;point<=events;point++) {
        setup();batch.evacuate_iso=1;batch.fail_at=point;
        int rc=execute(&b,&result);assert(rc==-EINTR || rc==0);
        reboot_all();assert(execute(&b,&result)==0);
        assert(execute(&b,&result)==0);
        for(unsigned i=0;i<3;i++)assert(rows[i].disk_exists[0] && !rows[i].disk_exists[1]);
    }
    setup();batch.evacuate_iso=1;batch.window_blocked=1;
    assert(execute(&b,&result)==-ENOSPC);
    for(unsigned i=0;i<3;i++)assert(!rows[i].disk_exists[0] && rows[i].disk_exists[1]);
    batch.window_blocked=0;assert(execute(&b,&result)==0);
    /* Existing manifest mismatch fails before evacuating any file. */
    setup();batch.exists[1][2]=1;memset(batch.manifest[1][2],0xaa,DND_EVAC_WIRE_BYTES);
    assert(execute(&b,&result)==-ESTALE);
    for(unsigned i=0;i<3;i++)assert(rows[i].disk_exists[0] && !journals[i].mutations);
    /* Mixed plan with source retained: blockers only, then safe direct copy. */
    setup();b.evacuate_iso=0;b.plan.move_count=2;
    assert(execute(&b,&result)==0 && batch.direct_calls==1 && result.bytes_written==5*BYTES);
    assert(execute(&b,&result)==0 && batch.direct_calls==1);
    blockers[1].operation[0]=blockers[0].operation[0];
    assert(execute(&b,&result)==-EINVAL);
    printf("P4 batch: %u manifest/coordinator interruptions, mixed evacuation, resume and fragmented-restore reporting passed\n",events);
    return 0;
}
