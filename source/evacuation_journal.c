#include "dnd_evacuation_journal.h"
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>

static void put32(unsigned char *p,uint32_t x) {
    for(unsigned i=0;i<4;i++)p[i]=(unsigned char)(x>>(i*8));
}
static void put64(unsigned char *p,uint64_t x) {
    for(unsigned i=0;i<8;i++)p[i]=(unsigned char)(x>>(i*8));
}
static uint32_t get32(const unsigned char *p) {
    uint32_t x=0;for(unsigned i=0;i<4;i++)x|=(uint32_t)p[i]<<(i*8);return x;
}
static uint64_t get64(const unsigned char *p) {
    uint64_t x=0;for(unsigned i=0;i<8;i++)x|=(uint64_t)p[i]<<(i*8);return x;
}
static int path_safe(const char *path) {
    if(!memchr(path,0,DND_EVAC_PATH) || !*path || *path=='/')return 0;
    const unsigned char *p=(const unsigned char *)path;
    while(*p) {
        const unsigned char *start=p;
        while(*p && *p!='/') {
            if(*p<32 || *p=='\\' || *p==':' || *p==127)return 0;
            if(*p<128) {p++;continue;}
            unsigned size;uint32_t cp;
            if(*p>=0xc2 && *p<=0xdf) {size=2;cp=*p&31;}
            else if(*p>=0xe0 && *p<=0xef) {size=3;cp=*p&15;}
            else if(*p>=0xf0 && *p<=0xf4) {size=4;cp=*p&7;}
            else return 0;
            for(unsigned i=1;i<size;i++) {
                if(!p[i] || (p[i]&0xc0)!=0x80)return 0;
                cp=(cp<<6)|(p[i]&63);
            }
            if((size==2 && cp<128) || (size==3 && cp<2048) ||
               (size==4 && cp<65536) || cp>0x10ffff ||
               (cp>=0xd800 && cp<=0xdfff))return 0;
            p+=size;
        }
        size_t n=(size_t)(p-start);
        if(!n || (n==1 && start[0]=='.') ||
           (n==2 && start[0]=='.' && start[1]=='.') ||
           (n>=3 && !memcmp(start,".__",3)) || start[n-1]=='.' ||
           start[n-1]==' ')return 0;
        if(*p && !*++p)return 0;
    }
    return 1;
}
static int nonzero(const unsigned char *p,unsigned n) {
    unsigned char value=0;while(n--)value|=*p++;return value!=0;
}
static int valid(const DndEvacRecord *r) {
    if(!r || !(r->operation[0]|r->operation[1]) || !r->sequence ||
       !(r->target_id[0]|r->target_id[1]) || !(r->scratch_id[0]|r->scratch_id[1]) ||
       (r->target_id[0]==r->scratch_id[0] && r->target_id[1]==r->scratch_id[1]) ||
       !r->bytes || r->bytes>INT32_MAX || r->durable_bytes>r->bytes ||
       r->state<DND_EVAC_PREPARED || r->state>DND_EVAC_ABORTED ||
       (r->strategy!=DND_DEFRAG_EVACUATE_ISO &&
        r->strategy!=DND_DEFRAG_EVACUATE_BLOCKERS) ||
       r->target_cluster_bytes<512 || r->scratch_cluster_bytes<512 ||
       (r->target_cluster_bytes&(r->target_cluster_bytes-1)) ||
       (r->scratch_cluster_bytes&(r->scratch_cluster_bytes-1)) ||
       !path_safe(r->relative_path))return 0;
    if(r->state==DND_EVAC_PREPARED && r->durable_bytes)return 0;
    if(r->state>=DND_EVAC_SCRATCH_VERIFIED && r->state<=DND_EVAC_DONE &&
       !nonzero(r->file_sha256,32))return 0;
    if((r->state>=DND_EVAC_SCRATCH_VERIFIED && r->state<=DND_EVAC_SOURCE_RELEASED) ||
       (r->state>=DND_EVAC_TARGET_VERIFIED && r->state<=DND_EVAC_DONE))
        if(r->durable_bytes!=r->bytes || memcmp(r->prefix_sha256,r->file_sha256,32))return 0;
    return 1;
}
int dnd_evac_encode(const DndEvacRecord *r,unsigned char w[DND_EVAC_WIRE_BYTES]) {
    if(!w || !valid(r))return -EINVAL;
    memset(w,0,DND_EVAC_WIRE_BYTES);memcpy(w,"DNDEVAC1",8);
    put32(w+8,1);put32(w+12,r->strategy);put64(w+16,r->sequence);
    put64(w+24,r->operation[0]);put64(w+32,r->operation[1]);
    put64(w+40,r->target_id[0]);put64(w+48,r->target_id[1]);
    put64(w+56,r->scratch_id[0]);put64(w+64,r->scratch_id[1]);
    put64(w+72,r->target_partition);put64(w+80,r->scratch_partition);
    put64(w+88,r->target_serial);put64(w+96,r->scratch_serial);
    put64(w+104,r->bytes);put32(w+112,r->state);put32(w+116,r->attributes);
    put64(w+120,r->mtime);put64(w+128,r->atime);put64(w+136,r->durable_bytes);
    memcpy(w+144,r->prefix_sha256,32);memcpy(w+176,r->file_sha256,32);
    memcpy(w+208,r->relative_path,strlen(r->relative_path)+1);
    put32(w+1232,r->target_cluster_bytes);put32(w+1236,r->scratch_cluster_bytes);
    DndSha256 sha;dnd_sha256_init(&sha);
    dnd_sha256_update(&sha,w,DND_EVAC_WIRE_BYTES-32);
    dnd_sha256_finish(&sha,w+DND_EVAC_WIRE_BYTES-32);
    return 0;
}
int dnd_evac_decode(const unsigned char w[DND_EVAC_WIRE_BYTES],DndEvacRecord *out) {
    if(!w || !out)return -EINVAL;
    unsigned char digest[32];DndSha256 sha;dnd_sha256_init(&sha);
    dnd_sha256_update(&sha,w,DND_EVAC_WIRE_BYTES-32);dnd_sha256_finish(&sha,digest);
    if(memcmp(w,"DNDEVAC1",8) || get32(w+8)!=1 ||
       memcmp(digest,w+DND_EVAC_WIRE_BYTES-32,32))return -EILSEQ;
    DndEvacRecord r={0};r.strategy=get32(w+12);r.sequence=get64(w+16);
    r.operation[0]=get64(w+24);r.operation[1]=get64(w+32);
    r.target_id[0]=get64(w+40);r.target_id[1]=get64(w+48);
    r.scratch_id[0]=get64(w+56);r.scratch_id[1]=get64(w+64);
    r.target_partition=get64(w+72);r.scratch_partition=get64(w+80);
    r.target_serial=get64(w+88);r.scratch_serial=get64(w+96);
    r.bytes=get64(w+104);r.state=get32(w+112);r.attributes=get32(w+116);
    r.mtime=get64(w+120);r.atime=get64(w+128);r.durable_bytes=get64(w+136);
    memcpy(r.prefix_sha256,w+144,32);memcpy(r.file_sha256,w+176,32);
    memcpy(r.relative_path,w+208,DND_EVAC_PATH);
    r.target_cluster_bytes=get32(w+1232);r.scratch_cluster_bytes=get32(w+1236);
    if(!valid(&r))return -EILSEQ;
    /* Require canonical zero padding, so records compare by wire bytes. */
    unsigned char canonical[DND_EVAC_WIRE_BYTES];
    if(dnd_evac_encode(&r,canonical) || memcmp(w,canonical,sizeof(canonical)))return -EILSEQ;
    *out=r;return 0;
}
static int same(const DndEvacRecord *a,const DndEvacRecord *b) {
    unsigned char x[DND_EVAC_WIRE_BYTES],y[DND_EVAC_WIRE_BYTES];
    return !dnd_evac_encode(a,x) && !dnd_evac_encode(b,y) && !memcmp(x,y,sizeof(x));
}
static int follows(const DndEvacRecord *a,const DndEvacRecord *b) {
    if(!valid(a) || !valid(b) || a->sequence==UINT64_MAX ||
       b->sequence!=a->sequence+1)return 0;
    DndEvacRecord expected=*a;
    expected.sequence=b->sequence;expected.state=b->state;
    expected.durable_bytes=b->durable_bytes;
    memcpy(expected.prefix_sha256,b->prefix_sha256,32);
    if(a->state<DND_EVAC_SCRATCH_VERIFIED)
        memcpy(expected.file_sha256,b->file_sha256,32);
    if(!same(&expected,b))return 0; /* Immutable paths, metadata and devices. */
    if(b->state==a->state) {
        return (a->state==DND_EVAC_COPYING || a->state==DND_EVAC_RESTORING) &&
               b->durable_bytes>a->durable_bytes;
    }
    if(b->state==DND_EVAC_ABORTED)return a->state<=DND_EVAC_SCRATCH_VERIFIED;
    if(b->state!=a->state+1 || a->state>=DND_EVAC_DONE)return 0;
    if(b->state==DND_EVAC_COPYING || b->state==DND_EVAC_RESTORING)
        return !b->durable_bytes;
    if(b->state==DND_EVAC_SCRATCH_VERIFIED || b->state==DND_EVAC_TARGET_VERIFIED)
        return b->durable_bytes==b->bytes;
    return a->durable_bytes==b->durable_bytes &&
           !memcmp(a->prefix_sha256,b->prefix_sha256,32);
}
static int ops_valid(DndEvacJournalOps o) {
    return o.probe && o.read_slot && o.write_slot && o.sync_volume;
}
static int probe(DndEvacJournalOps o,unsigned volume,const DndEvacRecord *r) {
    uint64_t id[2],partition,serial;
    int rc=o.probe(o.context,volume,id,&partition,&serial);
    if(rc)return rc<0?rc:-EIO;
    const uint64_t *expected=volume?r->scratch_id:r->target_id;
    return id[0]!=expected[0] || id[1]!=expected[1] ||
           partition!=(volume?r->scratch_partition:r->target_partition) ||
           serial!=(volume?r->scratch_serial:r->target_serial)?-ESTALE:0;
}
static int load(DndEvacJournalOps o,unsigned volume,DndEvacRecord *out) {
    DndEvacRecord records[2];int good[2]={0},corrupt=0;
    for(unsigned slot=0;slot<2;slot++) {
        unsigned char wire[DND_EVAC_WIRE_BYTES];
        int rc=o.read_slot(o.context,volume,slot,wire);
        if(rc==-ENOENT)continue;
        if(rc)return rc<0?rc:-EIO;
        rc=dnd_evac_decode(wire,&records[slot]);
        if(rc) {corrupt=1;continue;}
        if((records[slot].sequence&1)!=slot) {corrupt=1;continue;}
        good[slot]=1;
    }
    if(!good[0] && !good[1])return corrupt?-EILSEQ:-ENOENT;
    unsigned latest=good[1] && (!good[0] || records[1].sequence>records[0].sequence)?1:0;
    if(good[0] && good[1] && !follows(&records[1-latest],&records[latest]))return -EILSEQ;
    int rc=probe(o,volume,&records[latest]);if(rc)return rc;
    *out=records[latest];return 0;
}
int dnd_evac_inspect(DndEvacJournalOps o,DndEvacRecord *latest) {
    if(!ops_valid(o) || !latest)return -EINVAL;
    DndEvacRecord a,b;int x=load(o,0,&a),y=load(o,1,&b);
    if(x && x!=-ENOENT && x!=-ENODEV)return x;
    if(y && y!=-ENOENT && y!=-ENODEV)return y;
    if(x && y)return x==-ENODEV?DND_EVAC_NEEDS_TARGET:
                       y==-ENODEV?DND_EVAC_NEEDS_SCRATCH:DND_EVAC_CLEAN;
    if(!x && y) {*latest=a;return y==-ENODEV?DND_EVAC_NEEDS_SCRATCH:DND_EVAC_RECONCILE;}
    if(x && !y) {*latest=b;return x==-ENODEV?DND_EVAC_NEEDS_TARGET:DND_EVAC_RECONCILE;}
    if(same(&a,&b)) {*latest=a;return DND_EVAC_PENDING;}
    const DndEvacRecord *high=a.sequence>b.sequence?&a:&b;
    const DndEvacRecord *low=a.sequence>b.sequence?&b:&a;
    if(!follows(low,high))return -EILSEQ;
    *latest=*high;return DND_EVAC_RECONCILE;
}
static int store(DndEvacJournalOps o,const DndEvacRecord *r) {
    unsigned char wire[DND_EVAC_WIRE_BYTES],check[DND_EVAC_WIRE_BYTES];
    int rc=dnd_evac_encode(r,wire);if(rc)return rc;
    /* Probe BOTH volumes before the first mutation, then before each write. */
    for(unsigned v=0;v<2;v++) {rc=probe(o,v,r);if(rc)return rc;}
    for(unsigned v=0;v<2;v++) {
        rc=probe(o,v,r);if(rc)return rc;
        rc=o.write_slot(o.context,v,(unsigned)(r->sequence&1),wire);
        if(!rc)rc=o.sync_volume(o.context,v);
        if(!rc)rc=o.read_slot(o.context,v,(unsigned)(r->sequence&1),check);
        if(rc)return rc<0?rc:-EIO;
        if(memcmp(wire,check,sizeof(wire)))return -EIO;
    }
    return 0;
}
int dnd_evac_begin(DndEvacJournalOps o,const DndEvacRecord *r) {
    if(!ops_valid(o) || !valid(r) || r->sequence!=1 || r->state!=DND_EVAC_PREPARED)
        return -EINVAL;
    DndEvacRecord existing;int rc=dnd_evac_inspect(o,&existing);
    if(rc<0)return rc;
    if(rc!=DND_EVAC_CLEAN)return -EEXIST;
    return store(o,r);
}
int dnd_evac_reconcile(DndEvacJournalOps o,const DndEvacRecord *r) {
    if(!ops_valid(o) || !valid(r))return -EINVAL;
    DndEvacRecord latest;int rc=dnd_evac_inspect(o,&latest);
    if(rc<0)return rc;
    if(rc==DND_EVAC_NEEDS_SCRATCH || rc==DND_EVAC_NEEDS_TARGET)return -ENODEV;
    if(rc==DND_EVAC_CLEAN || !same(&latest,r))return -ESTALE;
    return rc==DND_EVAC_PENDING?0:store(o,r);
}
int dnd_evac_advance(DndEvacJournalOps o,const DndEvacRecord *previous,
                      const DndEvacRecord *next) {
    if(!ops_valid(o) || !follows(previous,next))return -EINVAL;
    DndEvacRecord latest;int rc=dnd_evac_inspect(o,&latest);
    if(rc<0)return rc;
    if(rc==DND_EVAC_NEEDS_SCRATCH || rc==DND_EVAC_NEEDS_TARGET)return -ENODEV;
    if(rc!=DND_EVAC_PENDING || !same(&latest,previous))return -ESTALE;
    return store(o,next);
}
int dnd_evac_stage_leaf(const DndEvacRecord *r,char *out,unsigned capacity) {
    if(!valid(r) || !out || capacity<53)return -EINVAL;
    int n=snprintf(out,capacity,".__dnd_evac_%016llx%016llx.tmp",
                   (unsigned long long)r->operation[0],(unsigned long long)r->operation[1]);
    return n<0 || (unsigned)n>=capacity?-ENAMETOOLONG:0;
}
