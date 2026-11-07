#include "dnd_defrag_planner.h"
#include <assert.h>
#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "defrag_fixture.h"
int main(void) {
    Fixture f;
    DndSectorReader reader={&f,read_sector,0};
    DndDefragFreeSpace stats={0};
    uint64_t start=0;
    fat32(&f,0);
    assert(dnd_defrag_read_free_space(&reader,&stats)==0);
    assert(stats.filesystem==DND_DEFRAG_FAT32);
    assert(stats.total_clusters==62);
    assert(stats.bytes_per_cluster==512);
    assert(stats.free_clusters==13);
    assert(stats.largest_free_run_clusters==7);
    assert(stats.largest_free_run_start_cluster==20);
    assert(dnd_defrag_preflight_direct(&stats,7*512,&start)==1 && start==20);
    assert(dnd_defrag_preflight_direct(&stats,7*512+1,&start)==0 && start==0);
    assert(dnd_defrag_preflight_direct(&stats,1459978240ULL,&start)==0);
    assert(dnd_defrag_preflight_direct(&stats,(uint64_t)INT32_MAX+1,&start)==-EFBIG);
    assert(dnd_defrag_preflight_direct(&stats,0,&start)==1 && start==0);
    reader.partition_lba=4;
    fat32(&f,4);
    assert(dnd_defrag_read_free_space(&reader,&stats)==0);
    assert(stats.largest_free_run_start_cluster==20);
    f.read_error=1;
    stats.free_clusters=999;
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    assert(stats.free_clusters==999); /* Fail without publishing partial stats. */
    f.read_error=0;
    reader.partition_lba=0;
    exfat(&f,0);
    assert(dnd_defrag_read_free_space(&reader,&stats)==0);
    assert(stats.filesystem==DND_DEFRAG_EXFAT);
    assert(stats.total_clusters==60 && stats.free_clusters==16);
    assert(stats.largest_free_run_clusters==11);
    assert(stats.largest_free_run_start_cluster==24);
    assert(dnd_defrag_preflight_direct(&stats,11*512,&start)==1 && start==24);
    f.sectors[2][0]=0; /* Missing allocation bitmap. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    exfat(&f,0);
    wr64(f.sectors[2]+24,3); /* Bitmap too short for 60 clusters. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    exfat(&f,0);
    f.sectors[0][110]=2; /* TexFAT dual bitmap needs an active-bitmap policy. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-ENOTSUP);
    exfat(&f,0);
    wr32(f.sectors[0]+92,UINT32_MAX);
    wr64(f.sectors[0]+72,UINT64_C(1)<<42);
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    /* An attacker-controlled cluster count cannot wrap FAT-size checks. */
    fat32(&f,0);
    f.sectors[0][13]=3; /* Invalid non-power-of-two cluster size. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    assert(dnd_defrag_read_free_space(NULL,&stats)==-EINVAL);
    assert(dnd_defrag_read_free_space(&reader,NULL)==-EINVAL);
    stats=(DndDefragFreeSpace){.total_clusters=50,.free_clusters=20,
        .largest_free_run_clusters=10,.largest_free_run_start_cluster=49,
        .bytes_per_cluster=512};
    assert(dnd_defrag_preflight_direct(&stats,512,&start)==-EINVAL);
    stats.largest_free_run_start_cluster=2;stats.free_clusters=51;
    assert(dnd_defrag_preflight_direct(&stats,512,&start)==-EINVAL);
    puts("read-only FAT32/exFAT free-run preflight passed");
    return 0;
}
