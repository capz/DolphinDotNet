#include "raw_move_fixture.h"

int main(int argc,char **argv) {
    (void)argv;unsigned cut_step=argc>1?1:256;
    DndRawMove move;DndRawResult result;
    for(unsigned variant=0;variant<4;variant++) {
        if(variant<2)rm_fat32((int)variant,4);else rm_exfat(variant==3,4);
        rm_finish_fixture();
        int rc=rm_plan(&move,1,2,30);
        if(rc)fprintf(stderr,"plan variant %u: %d\n",variant,rc);
        assert(rc==0);initial=rf;
        rc=dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result);
        if(rc)fprintf(stderr,"execute variant %u: %d state %u\n",variant,rc,result.state);
        assert(rc==0 && result.state==DND_RAW_WINDOW_COMPLETE);rm_oracle();
        assert(dnd_raw_move_mount_allowed(rm_ops(),&move)==1);
        assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
        unsigned events=rf.event,writes=rf.raw_writes,write_points[64];
        assert(writes<64);memcpy(write_points,rf.write_event,writes*sizeof(unsigned));
        /* Every prefix of every raw sector write in the bounded image model. */
        for(unsigned w=0;w<writes;w++)for(unsigned cut=0;cut<=512;cut+=cut_step) {
            rf=initial;rf.fail_event=write_points[w];rf.cut=cut;
            assert(dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==-EINTR);
            rm_reboot();
            int recovered=dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result);
            if(recovered)fprintf(stderr,"torn variant %u write %u cut %u recovery %d\n",variant,w,cut,recovered);
            assert(recovered==0);rm_oracle();
            assert(dnd_raw_move_mount_allowed(rm_ops(),&move)==1);
        }

        for(unsigned point=1;point<=events;point++) {
            rf=initial;rf.fail_event=point;
            rc=dnd_raw_move_execute(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result);
            assert(rc==-EINTR || rc==0);rm_reboot();
            rc=dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result);
            if(rc) {
                /* Unsealed/torn initial journals may not authorize any raw
                 * image mutation; their image must still equal the backup. */
                assert(rc==-EINPROGRESS || rc==-EILSEQ || rc==-ESTALE);
                assert(!memcmp(rf.image,rf.original,sizeof(rf.image)));
                assert(dnd_raw_move_mount_allowed(rm_ops(),&move)!=1);
            } else {
                rm_oracle();assert(dnd_raw_move_mount_allowed(rm_ops(),&move)==1);
                assert(dnd_raw_move_recover(rm_ops(),&move,&rm_snapshot,rm_transfer,sizeof(rm_transfer),&result)==0);
            }
        }
        printf("raw move variant %u: %u interruption points and %u sector-prefix cuts passed\n",variant,events,writes*(512/cut_step+1));
    }
    return 0;
}
