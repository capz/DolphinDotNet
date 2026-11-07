#include "dnd_evacuation.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>
#include "evacuation_fixture.h"

#include "evacuation_model.h"
int main(void) {
    (void)checkpoint_media;(void)next;
    DndEvacRecord expected=base_record();expected.bytes=BYTES;
    DndEvacResult out;
    reset();assert(run(&expected,0,&out)==0 && out.record.state==DND_EVAC_DONE);
    assert(world.source_deletes==1 && world.scratch_deletes==1 && world.publishes==1);
    assert(out.io.bytes_written==2*BYTES && out.io.bytes_read==4*BYTES);
    unsigned events=world.event;
    /* Stop AFTER every journal/filesystem/I/O callback, discard all volatile
     * data, resume twice. Never lose the last verified surviving copy. */
    for(unsigned point=1;point<=events;point++) {
        reset();world.fail_at=point;
        int rc=run(&expected,0,&out);
        assert(rc==-EINTR || rc==0);restart();
        assert(run(&expected,0,&out)==0 && out.record.state==DND_EVAC_DONE);
        assert(run(&expected,0,&out)==0);
        assert(world.exists[0] && !world.exists[1] && !world.exists[2]);
        surviving_copy();
    }
    reset();assert(run(&expected,1,&out)==0 && out.record.state==DND_EVAC_SOURCE_RELEASED);
    assert(!world.exists[0] && world.disk_exists[1]);
    unsigned mutations=media.mutations;media.missing[1]=1;
    assert(run(&expected,0,&out)==DND_EVAC_NEEDS_SCRATCH && media.mutations==mutations);
    media.missing[1]=0;
    world.fragmented=1;
    assert(run(&expected,0,&out)==-ENOSPC && world.disk_exists[1]);
    world.fragmented=0;
    assert(run(&expected,0,&out)==0);
    reset();world.collision=1;
    assert(run(&expected,0,&out)==-EEXIST && !media.mutations);
    reset();world.stale=1;
    assert(run(&expected,0,&out)==-ESTALE && !media.mutations);
    reset();world.cancel=1;
    assert(run(&expected,0,&out)==-ECANCELED && !media.mutations);
    reset();assert(run(&expected,1,&out)==0);
    world.data[1][0]^=1;world.disk[1][0]^=1;
    assert(run(&expected,0,&out)==-EIO && world.exists[1]);
    /* A journal cannot redirect the trusted caller's request to another file. */
    expected.relative_path[0]='X';
    assert(run(&expected,0,&out)==-ESTALE);
    printf("evacuation: %u callback interruption/reboot/resume cases, verified-copy authority and media guards passed\n",events);
    return 0;
}
