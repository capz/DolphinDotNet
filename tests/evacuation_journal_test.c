#include "dnd_evacuation_journal.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

#include "evacuation_fixture.h"
int main(void) {
    DndEvacRecord a=base_record(),decoded={0},latest={0};
    unsigned char wire[DND_EVAC_WIRE_BYTES];
    assert(dnd_evac_encode(&a,wire)==0 && dnd_evac_decode(wire,&decoded)==0);
    assert(decoded.bytes==1459978240 && decoded.target_partition==17);
    assert(!strcmp(decoded.relative_path,a.relative_path));
    /* Every byte is covered, including reserved/padding bytes and digest. */
    for(unsigned i=0;i<DND_EVAC_WIRE_BYTES;i++) {
        wire[i]^=1;assert(dnd_evac_decode(wire,&decoded)<0);wire[i]^=1;
    }
    const char *bad[]={"../keep.iso","games/../keep.iso","/keep.iso","sd:/keep.iso",
        "games//keep.iso","games/keep.iso/","games\\keep.iso","games/.__dnd_bad",
        "games/keep.iso.","games/keep.iso ","games/\xc0\xafkeep.iso"};
    for(unsigned i=0;i<sizeof(bad)/sizeof(bad[0]);i++) {
        DndEvacRecord forged=a;strcpy(forged.relative_path,bad[i]);
        assert(dnd_evac_encode(&forged,wire)==-EINVAL);
    }
    assert(dnd_evac_inspect(ops(&media),&latest)==DND_EVAC_CLEAN);
    assert(dnd_evac_begin(ops(&media),&a)==0);
    assert(dnd_evac_begin(ops(&media),&a)==-EEXIST);
    assert(dnd_evac_inspect(ops(&media),&latest)==DND_EVAC_PENDING);
    char stage[64];assert(dnd_evac_stage_leaf(&a,stage,sizeof(stage))==0);
    assert(!strcmp(stage,".__dnd_evac_000000000000002a0000000000000049.tmp"));
    /* Power removal at every write and sync of every state, with two-slot
     * fallback and mandatory replica reconciliation before further progress. */
    while(a.state<DND_EVAC_DONE) {
        DndEvacRecord b=next(a);checkpoint_media=media;
        for(unsigned point=1;point<=4;point++) {
            media=checkpoint_media;media.event=0;media.fail_event=point;
            assert(dnd_evac_advance(ops(&media),&a,&b)==-EINTR);
            reboot(&media);
            int rc=dnd_evac_inspect(ops(&media),&latest);
            assert(rc==DND_EVAC_PENDING || rc==DND_EVAC_RECONCILE);
            if(rc==DND_EVAC_RECONCILE) {
                DndEvacRecord future=next(b);
                if(b.state<DND_EVAC_DONE)
                    assert(dnd_evac_advance(ops(&media),&b,&future)==-ESTALE);
                assert(dnd_evac_reconcile(ops(&media),&latest)==0);
                assert(dnd_evac_reconcile(ops(&media),&latest)==0);
            }
            assert(dnd_evac_inspect(ops(&media),&decoded)==DND_EVAC_PENDING);
            assert(decoded.sequence==a.sequence || decoded.sequence==b.sequence);
        }
        media=checkpoint_media;media.event=0;media.fail_event=0;
        assert(dnd_evac_advance(ops(&media),&a,&b)==0);
        a=b;
        for(unsigned v=0;v<2;v++) {
            unsigned before=media.mutations;media.missing[v]=1;
            assert(dnd_evac_inspect(ops(&media),&latest)==
                   (v?DND_EVAC_NEEDS_SCRATCH:DND_EVAC_NEEDS_TARGET));
            assert(media.mutations==before);
            assert(dnd_evac_reconcile(ops(&media),&latest)==-ENODEV);
            media.missing[v]=0;media.swapped[v]=1;
            assert(dnd_evac_inspect(ops(&media),&latest)==-ESTALE);
            assert(media.mutations==before);media.swapped[v]=0;
        }
    }
    /* A checkpoint for the same state may only advance durable bytes. */
    media=(Media){0};a=base_record();assert(dnd_evac_begin(ops(&media),&a)==0);
    DndEvacRecord b=next(a);assert(dnd_evac_advance(ops(&media),&a,&b)==0);
    a=b;b.sequence++;b.durable_bytes=32768;memset(b.prefix_sha256,0x11,32);
    assert(dnd_evac_advance(ops(&media),&a,&b)==0);
    assert(dnd_evac_advance(ops(&media),&a,&b)==-ESTALE);
    a=b;b.sequence++;b.durable_bytes=1;
    assert(dnd_evac_advance(ops(&media),&a,&b)==-EINVAL);
    b=next(a);strcpy(b.relative_path,"keep.iso");
    assert(dnd_evac_advance(ops(&media),&a,&b)==-EINVAL);
    b=next(a);b.operation[0]++;
    assert(dnd_evac_advance(ops(&media),&a,&b)==-EINVAL);
    /* Before release, an explicit abort is journal-only and recoverable. */
    b=a;b.sequence++;b.state=DND_EVAC_ABORTED;
    assert(dnd_evac_advance(ops(&media),&a,&b)==0);
    /* Conflicting partners and skipped sequences never become authority. */
    DndEvacRecord conflict=b;conflict.operation[0]++;
    assert(dnd_evac_encode(&conflict,media.wire[1][b.sequence&1])==0);
    assert(dnd_evac_inspect(ops(&media),&latest)==-EILSEQ);
    /* Missing/corrupt initial partner: a torn first journal is NOT silently
     * treated as a clean volume or overwritten as a fresh operation. */
    media=(Media){0};a=base_record();media.fail_event=1;
    assert(dnd_evac_begin(ops(&media),&a)==-EINTR);reboot(&media);
    assert(dnd_evac_inspect(ops(&media),&latest)==-EILSEQ);
    assert(dnd_evac_begin(ops(&media),&a)==-EILSEQ);
    puts("replicated journal: all states, torn writes, missing/swapped media, reconciliation and path guards passed");
    return 0;
}
