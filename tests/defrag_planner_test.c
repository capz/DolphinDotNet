#include "dnd_defrag_planner.h"
#include <assert.h>
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>

/* An independent oracle enumerates EVERY subset of files and EVERY window,
 * rather than relying on the production extent-boundary candidate algorithm. */
static unsigned random_state=1234567;
static unsigned next_random(void) {
    random_state=random_state*1664525u+1013904223u;return random_state;
}
static uint64_t ceil_clusters(uint64_t bytes,unsigned size) {
    return bytes/size+(bytes%size!=0);
}
static int better(DndDefragPlan a,DndDefragPlan b) {
    if(a.evacuation_bytes!=b.evacuation_bytes)return a.evacuation_bytes<b.evacuation_bytes;
    if(a.move_count!=b.move_count)return a.move_count<b.move_count;
    if(a.bytes_written!=b.bytes_written)return a.bytes_written<b.bytes_written;
    if(a.start!=b.start)return a.start<b.start;
    return a.strategy<b.strategy;
}
static DndDefragPlan oracle(DndDefragSnapshot *s,DndDefragSnapshot *scratch,
                              unsigned reserve) {
    DndDefragPlan best={0};
    unsigned need=(unsigned)ceil_clusters(s->files[1].bytes,s->space.bytes_per_cluster);
    for(unsigned mask=0;mask<32;mask++) {
        DndDefragPlan p={0};int possible=1;
        for(unsigned f=1;f<=5;f++)if(mask&(1u<<(f-1))) {
            if(s->files[f].flags&DND_DEFRAG_PINNED)possible=0;
            p.evacuation_bytes+=s->files[f].bytes;p.move_count++;
            p.scratch_peak_clusters+=ceil_clusters(s->files[f].bytes,
                                                     scratch->space.bytes_per_cluster);
        }
        if(p.move_count && p.scratch_peak_clusters+reserve>scratch->space.free_clusters)
            possible=0;
        if(!possible)continue;
        if(p.move_count)p.scratch_peak_clusters+=reserve;
        int source_moved=(mask&1)!=0;
        p.strategy=!mask?DND_DEFRAG_DIRECT:
                   mask==1?DND_DEFRAG_EVACUATE_ISO:DND_DEFRAG_EVACUATE_BLOCKERS;
        p.bytes_written=s->files[1].bytes*(source_moved?0:1)+2*p.evacuation_bytes;
        for(unsigned start=2;start+need<=s->space.total_clusters+2;start++) {
            int clear=1;
            for(unsigned n=start;n<start+need;n++) {
                uint32_t owner=s->runs[n-2].owner;
                if(owner!=DND_DEFRAG_FREE &&
                   (owner<1 || owner>5 || !(mask&(1u<<(owner-1)))))clear=0;
            }
            if(clear) {
                p.start=start;
                if(!best.strategy || better(p,best))best=p;
            }
        }
    }
    return best;
}
int main(void) {
    DndDefragFile files[6]={{0}},scratch_files[1]={{0}};
    DndDefragRun runs[24],scratch_run={2,40,DND_DEFRAG_FREE};
    DndDefragSnapshot s={.files=files,.runs=runs,.file_count=6,.run_count=24,
        .device={{1,2},1,1},.space={.total_clusters=24,.bytes_per_cluster=512},.valid=1};
    DndDefragSnapshot scratch={.files=scratch_files,.runs=&scratch_run,
        .file_count=1,.run_count=1,.device={{3,4},1,1},
        .space={.total_clusters=40,.free_clusters=40,.bytes_per_cluster=1024},.valid=1};
    files[0].flags=DND_DEFRAG_DIRECTORY|DND_DEFRAG_PINNED;
    scratch_files[0].flags=DND_DEFRAG_DIRECTORY|DND_DEFRAG_PINNED;
    DndDefragMove moves[6];DndDefragPlan p;
    unsigned strategies[4]={0};
    for(unsigned trial=0;trial<2000;trial++) {
        for(unsigned f=1;f<6;f++) {
            files[f].bytes=0;files[f].flags=(next_random()%9==0)?DND_DEFRAG_PINNED:0;
        }
        files[1].flags=0;s.space.free_clusters=0;
        for(unsigned i=0;i<24;i++) {
            unsigned value=next_random()%9;
            uint32_t owner=value<=4?value+1:value==5?DND_DEFRAG_SYSTEM:DND_DEFRAG_FREE;
            if(i==0)owner=1;
            runs[i]=(DndDefragRun){i+2,1,owner};
            if(owner<6)files[owner].bytes+=512;
            if(owner==DND_DEFRAG_FREE)s.space.free_clusters++;
        }
        /* Non-cluster-sized files expose incorrect byte->scratch rounding. */
        for(unsigned f=1;f<6;f++)if(files[f].bytes)files[f].bytes-=next_random()%100;
        scratch.space.free_clusters=40; /* Keep the scratch map self-consistent. */
        unsigned reserve=next_random()%41;
        assert(dnd_defrag_plan(&s,1,&scratch,reserve,0,0,moves,6,&p)==0);
        DndDefragPlan expected=oracle(&s,&scratch,reserve);
        assert(p.strategy==expected.strategy);
        if(!p.strategy)continue;
        strategies[p.strategy]++;
        assert(p.start==expected.start && p.evacuation_bytes==expected.evacuation_bytes);
        assert(p.move_count==expected.move_count && p.bytes_written==expected.bytes_written);
        assert(p.scratch_peak_clusters==expected.scratch_peak_clusters);
        assert(p.bytes_read==2*p.bytes_written);
        uint64_t bytes=0,peak=0;
        for(size_t i=0;i<p.move_count;i++) {
            bytes+=moves[i].bytes;peak+=moves[i].scratch_clusters;
            for(size_t j=0;j<i;j++)assert(moves[i].file!=moves[j].file);
        }
        assert(bytes==p.evacuation_bytes);
        assert(!p.move_count || peak+reserve==p.scratch_peak_clusters);
    }
    assert(strategies[1] && strategies[2] && strategies[3]);
    /* Nearly full target: evacuating source makes its own run reachable. */
    DndDefragRun full_runs[]={{2,1,DND_DEFRAG_SYSTEM},{3,3,1},{6,1,DND_DEFRAG_FREE}};
    s.runs=full_runs;s.run_count=3;s.file_count=2;s.space.total_clusters=5;
    s.space.free_clusters=1;files[1].bytes=1536;files[1].flags=0;
    assert(dnd_defrag_plan(&s,1,&scratch,2,0,0,moves,6,&p)==0);
    assert(p.strategy==DND_DEFRAG_EVACUATE_ISO && p.start==3);
    assert(p.evacuation_bytes==1536 && p.scratch_peak_clusters==4);
    assert(dnd_defrag_plan(&s,1,&scratch,0,0,0,NULL,0,&p)==-ENOSPC);
    scratch.device.physical_id[0]=1;scratch.device.physical_id[1]=2;
    assert(dnd_defrag_plan(&s,1,&scratch,0,0,0,moves,6,&p)==0 && !p.strategy);
    scratch.device.physical_id[0]=3;
    scratch.device.writable=0;
    assert(dnd_defrag_plan(&s,1,&scratch,0,0,0,moves,6,&p)==0 && !p.strategy);
    scratch.device.writable=1;
    assert(dnd_defrag_plan(&s,1,&scratch,0,1,0,moves,6,&p)==0 && !p.strategy);
    assert(p.reason==DND_DEFRAG_REASON_GUARD);
    assert(dnd_defrag_plan(&s,1,&scratch,0,0,1,moves,6,&p)==0 && !p.strategy);
    files[1].flags=DND_DEFRAG_PINNED;
    assert(dnd_defrag_plan(&s,1,&scratch,0,0,0,moves,6,&p)==0 && !p.strategy);
    files[1].flags=0;
    full_runs[1].start=2;
    assert(dnd_defrag_plan(&s,1,NULL,0,0,0,moves,6,&p)==-EINVAL);
    full_runs[1].start=3;
    /* Full-size ISO is represented by two runs and fixed metadata only. */
    const uint64_t iso=1459978240ULL;
    s.space.bytes_per_cluster=32768;s.space.total_clusters=100000;
    uint64_t need=ceil_clusters(iso,32768);
    DndDefragRun large[]={{2,need,1},{2+need,100000-need,DND_DEFRAG_FREE}};
    s.runs=large;s.run_count=2;s.space.free_clusters=100000-need;files[1].bytes=iso;
    assert(dnd_defrag_plan(&s,1,NULL,0,0,0,moves,6,&p)==0);
    assert(p.strategy==DND_DEFRAG_DIRECT && p.clusters==need);
    assert(p.bytes_written==iso && p.bytes_read==2*iso);
    files[1].bytes=(uint64_t)INT32_MAX+1;
    large[0].count=ceil_clusters(files[1].bytes,32768);
    large[1].start=large[0].start+large[0].count;
    large[1].count=100000-large[0].count;s.space.free_clusters=large[1].count;
    assert(dnd_defrag_plan(&s,1,NULL,0,0,0,moves,6,&p)==-EFBIG);
    puts("deterministic planner: 2000 exhaustive subset/window oracle comparisons and full-size ISO passed");
    return 0;
}
