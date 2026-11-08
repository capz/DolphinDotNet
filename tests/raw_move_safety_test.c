#include "raw_move_fixture.h"

static RawFixture pending;
static void update_roots(const DndRawMove *move) {
    unsigned char request[512],binding[32],root[32];DndSha256 sha;
    assert(dnd_raw_request_encode(move,request)==0);dnd_sha256_init(&sha);
    dnd_sha256_update(&sha,request,512);dnd_sha256_finish(&sha,binding);
    DndRawCheckpoint cp;assert(dnd_raw_move_inspect(rm_ops(),move,&cp)==DND_RAW_PENDING);
    dnd_sha256_init(&sha);
    for(uint64_t i=0;i<cp.patch_count;i++)dnd_sha256_update(&sha,rf.journal[0].patch[i],2048);
    dnd_sha256_finish(&sha,root);
    for(unsigned v=0;v<2;v++)for(unsigned slot=0;slot<2;slot++)if(rf.journal[v].checkpoint_exists[slot]) {
        DndRawCheckpoint c;assert(dnd_raw_checkpoint_decode(rf.journal[v].checkpoint[slot],&c)==0);
        memcpy(c.patches_sha256,root,32);assert(dnd_raw_checkpoint_encode(&c,rf.journal[v].checkpoint[slot])==0);
    }
    memcpy(rf.saved,rf.journal,sizeof(rf.saved));
}
static void forge_patch(const DndRawMove *move,unsigned mode) {
    unsigned char request[512],binding[32];DndSha256 sha;
    assert(dnd_raw_request_encode(move,request)==0);dnd_sha256_init(&sha);
    dnd_sha256_update(&sha,request,512);dnd_sha256_finish(&sha,binding);
    DndRawPatch p;assert(dnd_raw_patch_decode(rf.journal[0].patch[0],0,binding,&p)==0);
    if(mode==0)p.lba=rf.heap+3; /* Unrelated protected data, not FAT metadata. */
    else if(mode==1)p.applied[100]^=0x80; /* Unauthorized neighboring FAT field. */
    else {p.before[100]^=1;p.applied[100]^=1;p.cleaned[100]^=1;}
    for(unsigned v=0;v<2;v++)assert(dnd_raw_patch_encode(&p,0,binding,rf.journal[v].patch[0])==0);
    update_roots(move);
}
int main(int argc,char **argv) {
    (void)argv;unsigned step=argc>1?1:256;
    DndRawMove move;DndRawResult result;
    for(unsigned sample=0;sample<2;sample++) {
        unsigned valid=sample?RM_BYTES/2+7:0;
        rm_exfat(1,4);unsigned char *entry=rf.image[rf.root]+64;
        rm_p64(entry+40,valid);rm_p16(entry+2,rm_set_sum(entry,3));rm_finish_fixture();
        assert(rm_plan(&move,1,2,30)==0);
        unsigned char logical[RM_BYTES],expected[32];DndSha256 hash;
        for(unsigned i=0;i<RM_BYTES;i++)logical[i]=i<valid?rm_pattern(i):0;
        dnd_sha256_init(&hash);dnd_sha256_update(&hash,logical,sizeof(logical));dnd_sha256_finish(&hash,expected);
        assert(!memcmp(expected,move.file_sha256,32));
        assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
        rm_oracle();
        assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
    }
    rm_exfat(1,4);rm_finish_fixture();assert(rm_plan(&move,1,2,30)==0);initial=rf;
    assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
    unsigned writes=rf.raw_writes,points[64];assert(writes<64);
    memcpy(points,rf.write_event,writes*sizeof(unsigned));
    for(unsigned phase=0;phase<2;phase++) {
        unsigned wanted=phase?DND_RAW_CLEANUP_INTENT:DND_RAW_METADATA_INTENT;
        int found=0;
        for(unsigned w=0;w<writes;w++) {
            rf=initial;rf.fail_event=points[w];rf.cut=0;
            assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-EINTR);
            rm_reboot();DndRawCheckpoint cp;
            assert(dnd_raw_move_inspect(rm_ops(),&move,&cp)==DND_RAW_PENDING);
            if(cp.state!=wanted)continue;
            pending=rf;found=1;
            assert(cp.patch_count<8);
            /* Every persisted subset represents all whole-sector reorderings
             * in this bounded phase. No factorial enumeration is needed. */
            for(unsigned mask=0;mask<(1u<<cp.patch_count);mask++) {
                rf=pending;
                for(uint64_t i=0;i<cp.patch_count;i++) {
                    unsigned char wire[512],binding[32];DndSha256 h;DndRawPatch p;
                    assert(dnd_raw_request_encode(&move,wire)==0);dnd_sha256_init(&h);
                    dnd_sha256_update(&h,wire,512);dnd_sha256_finish(&h,binding);
                    assert(dnd_raw_patch_decode(rf.journal[0].patch[i],i,binding,&p)==0);
                    const unsigned char *sector=phase?((mask&(1u<<i))?p.cleaned:p.applied):
                        ((mask&(1u<<i))?p.applied:p.before);
                    memcpy(rf.image[p.lba],sector,512);memcpy(rf.disk[p.lba],sector,512);
                }
                assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
                rm_oracle();assert(dnd_raw_move_mount_allowed(rm_ops(),&move)==1);
            }
            /* Start with every metadata sector at its new mapping so undo has
             * work to do too. The image remains quarantined until recovery. */
            rf=pending;
            for(uint64_t i=0;i<cp.patch_count;i++) {
                unsigned char wire[512],binding[32];DndSha256 h;DndRawPatch p;
                assert(dnd_raw_request_encode(&move,wire)==0);dnd_sha256_init(&h);
                dnd_sha256_update(&h,wire,512);dnd_sha256_finish(&h,binding);
                assert(dnd_raw_patch_decode(rf.journal[0].patch[i],i,binding,&p)==0);
                memcpy(rf.image[p.lba],p.applied,512);memcpy(rf.disk[p.lba],p.applied,512);
            }
            rf.raw_writes=0;pending=rf;
            assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
            unsigned recovery_writes=rf.raw_writes,recovery_points[64];assert(recovery_writes<64);
            memcpy(recovery_points,rf.write_event,recovery_writes*sizeof(unsigned));
            for(unsigned q=0;q<recovery_writes;q++)for(unsigned cut=0;cut<=512;cut+=step) {
                rf=pending;rf.fail_event=recovery_points[q];rf.cut=cut;
                assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-EINTR);
                rm_reboot();
                assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
                rm_oracle();assert(dnd_raw_move_mount_allowed(rm_ops(),&move)==1);
            }
            /* Full zero-fill erasure of an authorized pending metadata sector
             * is recovered from the validated original sector images. */
            rf=pending;memset(rf.image[rf.offset],0,512);memset(rf.disk[rf.offset],0,512);
            assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
            rm_oracle();
            break;
        }
        assert(found);
    }
    rm_fat32(0,4);rm_finish_fixture();assert(rm_plan(&move,1,2,30)==0);initial=rf;
    DndRawOps physical=rm_ops();physical.medium_kind=DND_RAW_DEVICE;
    assert(dnd_raw_move_execute(physical,&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-ENOTSUP);
    assert(!rf.raw_writes && !rf.record_writes);
    rf.stale=1;
    assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-ESTALE);
    rf=initial;rf.cancelled=1;
    assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-ECANCELED);
    assert(!rf.raw_writes && !rf.record_writes);
    rf=initial;rf.read_error=1;
    assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-EIO);
    assert(!rf.raw_writes && !rf.record_writes);
    rf=initial;rf.sync_error=1;
    assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-EIO);
    rf.sync_error=0;rm_reboot();
    assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);rm_oracle();
    rf=initial;
    assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
    unsigned events=rf.event;
    for(unsigned point=1;point<=events;point++) {
        rf=initial;rf.cancel_event=point;
        int rc=dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result);
        assert(rc==0 || rc==-ECANCELED);rm_reboot();
        assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);rm_oracle();
    }
    for(unsigned v=0;v<2;v++) {
        rf=initial;rf.missing[v]=1;
        assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-ENODEV);
        assert(!rf.raw_writes);
    }
    rf=initial;rf.drop_write=1;
    assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-EIO);
    rf.drop_write=0;rm_reboot();
    assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);rm_oracle();
    rf=initial;assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
    writes=rf.raw_writes;memcpy(points,rf.write_event,writes*sizeof(unsigned));
    for(unsigned forge=0;forge<3;forge++) {
        rf=initial;rf.fail_event=points[2];rf.cut=0;
        assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-EINTR);
        rm_reboot();forge_patch(&move,forge);unsigned before=rf.raw_writes;
        int rc=dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result);
        assert(rc<0 && rf.raw_writes==before); /* Recomputed hashes are not authorization. */
    }
    puts("P6 recovery interruptions, persisted write subsets, zero-fill recovery, forgery and physical-device gates passed");
    return 0;
}
