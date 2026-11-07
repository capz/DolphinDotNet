#include "dnd_defrag_rebuild.h"
#include <assert.h>
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>

int main(void) {
    DndDefragFile files[5]={{0}},sf[1]={{0}};
    files[0].flags=DND_DEFRAG_DIRECTORY|DND_DEFRAG_PINNED;
    files[1].bytes=1459978240ULL;files[1].name[0]='I';
    files[2].bytes=512;files[2].flags=DND_DEFRAG_PINNED;files[2].name[0]='S';
    files[3].bytes=9999;files[3].name[0]='B';
    files[4].flags=DND_DEFRAG_DIRECTORY|DND_DEFRAG_PINNED;
    DndDefragRun runs[]={{2,1,0},{3,44555,1},{44558,1,2},{44559,1,3},
                         {44560,1,4},{44561,55441,DND_DEFRAG_FREE}};
    DndDefragSnapshot target={.valid=1,.files=files,.file_count=5,.runs=runs,.run_count=6,
        .space={.bytes_per_cluster=32768,.total_clusters=100000,.free_clusters=55441},
        .device={{1,2},1,1}};
    DndDefragSnapshot scratch={.valid=1,.files=sf,.file_count=1,
        .space={.bytes_per_cluster=4096,.total_clusters=400000,.free_clusters=400000},
        .device={{3,4},1,1}};
    DndDefragRebuildEntry entries[5];DndDefragRebuildPlan plan;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==0);
    assert(plan.can_back_up && !plan.can_execute && plan.file_count==3 && plan.directory_count==2);
    assert(plan.reason==DND_REBUILD_NEEDS_OFFLINE_ACCEPTANCE);
    assert(plan.backup_bytes==1459988751ULL);
    assert(plan.scratch_peak_clusters==(1459978240ULL+4095)/4096+1+3+4);
    assert(entries[0].file==1 && entries[1].file==2 && entries[2].file==3);
    /* Same target cluster counts tie by stable index, not logical byte length. */
    assert(entries[1].flags&DND_REBUILD_METADATA_REVIEW);
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,1,&plan)==-ENOSPC);
    scratch.space.free_clusters=1;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==0);
    assert(!plan.can_back_up && plan.reason==DND_REBUILD_SCRATCH_CAPACITY);
    scratch.space.free_clusters=400000;scratch.device.physical_id[0]=1;scratch.device.physical_id[1]=2;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==0);
    assert(!plan.can_back_up && plan.reason==DND_REBUILD_NEEDS_SCRATCH);
    scratch.device.physical_id[0]=3;
    runs[5].owner=DND_DEFRAG_UNKNOWN;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==0);
    assert(!plan.can_back_up && plan.reason==DND_REBUILD_UNKNOWN_ALLOCATION);
    runs[5].owner=DND_DEFRAG_FREE;
    files[2].name[0]='.';files[2].name[1]='_';files[2].name[2]='_';
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==0);
    assert(plan.reason==DND_REBUILD_ACTIVE_TRANSACTION);
    files[2].name[0]='S';files[2].bytes=(uint64_t)INT32_MAX+1;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==0);
    assert(plan.reason==DND_REBUILD_FILE_TOO_LARGE);
    files[2].bytes=512;
    files[2].flags|=DND_DEFRAG_NAME_UNSUPPORTED;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==0);
    assert(plan.reason==DND_REBUILD_UNSUPPORTED_NAME);
    files[2].flags&=~DND_DEFRAG_NAME_UNSUPPORTED;
    files[2].bytes=65536;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,0,entries,5,&plan)==-EINVAL);
    files[2].bytes=512;
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,1,0,entries,5,&plan)==0);
    assert(plan.reason==DND_REBUILD_DEVICE_GUARD);
    assert(dnd_defrag_rebuild_plan(&target,1,&scratch,4,0,1,entries,5,&plan)==0);
    assert(plan.reason==DND_REBUILD_DEVICE_GUARD);
    puts("P5 read-only full backup inventory, scratch accounting, ISO-first order and explicit execution gate passed");
    return 0;
}
