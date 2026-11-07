#ifndef DND_EVAC_MODEL_H
#define DND_EVAC_MODEL_H
#define BYTES 100003
static struct World {
    unsigned char data[3][BYTES],disk[3][BYTES];
    int exists[3],disk_exists[3],direction,stale,fragmented,cancel,collision;
    unsigned event,fail_at,source_deletes,scratch_deletes,publishes;
} world;
static _Alignas(32) unsigned char scratch[DND_COPY_BUFFER];
static int tick(void) {return ++world.event==world.fail_at?-EINTR:0;}
static void surviving_copy(void) {
    int good=0;
    for(unsigned f=0;f<3;f++)if(world.disk_exists[f]) {
        int matches=1;
        for(unsigned i=0;i<BYTES;i++)if(world.disk[f][i]!=(unsigned char)(i*17u+3u))matches=0;
        good|=matches;
    }
    assert(good);
}
static void reset(void) {
    memset(&world,0,sizeof(world));memset(&media,0,sizeof(media));
    world.exists[0]=world.disk_exists[0]=1;
    for(unsigned i=0;i<BYTES;i++)world.data[0][i]=world.disk[0][i]=(unsigned char)(i*17u+3u);
}
static void restart(void) {
    surviving_copy();reboot(&media);world.fail_at=0;
    memcpy(world.data,world.disk,sizeof(world.data));
    memcpy(world.exists,world.disk_exists,sizeof(world.exists));
}
static int journal_probe(void *ctx,unsigned v,uint64_t id[2],uint64_t *part,uint64_t *serial) {
    int rc=probe(ctx,v,id,part,serial);return rc?rc:tick();
}
static int journal_read(void *ctx,unsigned v,unsigned slot,void *wire) {
    int rc=read_slot(ctx,v,slot,wire);return rc?rc:tick();
}
static int journal_write(void *ctx,unsigned v,unsigned slot,const void *wire) {
    int rc=write_slot(ctx,v,slot,wire);return rc?rc:tick();
}
static int journal_sync(void *ctx,unsigned v) {
    int rc=sync_volume(ctx,v);return rc?rc:tick();
}
static int guard(void *ctx,const DndEvacRecord *r) {
    (void)ctx;(void)r;
    if(world.stale)return -ESTALE;
    return tick();
}
static int stages_absent(void *ctx,const DndEvacRecord *r) {
    (void)ctx;(void)r;
    return world.collision || world.exists[1] || world.exists[2]?-EEXIST:tick();
}
static int stream_source(void *ctx,uint64_t at,void *out,size_t n) {
    (void)ctx;unsigned f=world.direction?1:0;
    assert(world.exists[f] && at+n<=BYTES);memcpy(out,world.data[f]+at,n);return tick();
}
static int stream_write(void *ctx,uint64_t at,const void *in,size_t n) {
    (void)ctx;unsigned f=world.direction?2:1;
    assert(world.exists[f] && at+n<=BYTES);memcpy(world.data[f]+at,in,n);return tick();
}
static int stream_read(void *ctx,uint64_t at,void *out,size_t n) {
    (void)ctx;unsigned f=world.direction?2:1;
    assert(world.exists[f] && at+n<=BYTES);memcpy(out,world.data[f]+at,n);return tick();
}
static int stream_sync(void *ctx) {
    (void)ctx;unsigned f=world.direction?2:1;
    memcpy(world.disk[f],world.data[f],BYTES);world.disk_exists[f]=world.exists[f];return tick();
}
static int stream_reopen(void *ctx) {(void)ctx;return tick();}
static int stream_guard(void *ctx) {(void)ctx;return tick();}
static int open_copy(void *ctx,const DndEvacRecord *r,unsigned direction,DndCopyOps *o) {
    (void)ctx;(void)r;world.direction=(int)direction;
    unsigned f=direction?2:1;world.exists[f]=1;
    *o=(DndCopyOps){.read_source=stream_source,.write_stage=stream_write,
        .read_stage=stream_read,.sync_stage=stream_sync,.reopen_stage=stream_reopen,
        .revalidate=stream_guard};return tick();
}
static int close_copy(void *ctx) {(void)ctx;return tick();}
static int verify_data(void *ctx,const DndEvacRecord *r,unsigned f) {
    (void)ctx;
    if(!world.exists[f])return -ENOENT;
    if(f!=1 && r->state>=DND_EVAC_RESTORING && world.fragmented)return -ENOSPC;
    DndSha256 sha;unsigned char digest[32];dnd_sha256_init(&sha);
    dnd_sha256_update(&sha,world.data[f],BYTES);dnd_sha256_finish(&sha,digest);
    return memcmp(digest,r->file_sha256,32)?-EIO:0;
}
static int verify(void *ctx,const DndEvacRecord *r,unsigned f) {
    int rc=verify_data(ctx,r,f);return rc?rc:tick();
}
static int release_source(void *ctx,const DndEvacRecord *r) {
    (void)ctx;
    DndEvacRecord a;
    /* Assert both durable partner journals authorize release and scratch is
     * complete before the model permits source deletion. */
    assert(dnd_evac_inspect(ops(&media),&a)==DND_EVAC_PENDING && a.state==DND_EVAC_RELEASE_READY);
    assert(verify_data(NULL,r,DND_EVAC_SCRATCH)==0);
    surviving_copy();world.exists[0]=world.disk_exists[0]=0;world.source_deletes++;
    surviving_copy();return tick();
}
static int publish(void *ctx,const DndEvacRecord *r) {
    (void)ctx;(void)r;assert(!world.exists[0] && world.exists[2]);
    memcpy(world.data[0],world.data[2],BYTES);memcpy(world.disk[0],world.data[2],BYTES);
    world.exists[0]=world.disk_exists[0]=1;world.exists[2]=world.disk_exists[2]=0;
    world.publishes++;surviving_copy();return tick();
}
static int remove_scratch(void *ctx,const DndEvacRecord *r) {
    (void)ctx;
    DndEvacRecord a;assert(dnd_evac_inspect(ops(&media),&a)==DND_EVAC_PENDING);
    assert(a.state==DND_EVAC_PUBLISHED && verify_data(NULL,r,DND_EVAC_ORIGINAL)==0);
    if(!world.exists[1])return -ENOENT;
    world.exists[1]=world.disk_exists[1]=0;world.scratch_deletes++;
    surviving_copy();return tick();
}
static int cancelled(void *ctx) {(void)ctx;return world.cancel;}
static DndEvacJournalOps journal(void) {
    return (DndEvacJournalOps){&media,journal_probe,journal_read,journal_write,journal_sync};
}
static DndEvacFileOps files(void) {
    return (DndEvacFileOps){NULL,guard,stages_absent,open_copy,close_copy,verify,
                           release_source,publish,remove_scratch,cancelled};
}
static int run(const DndEvacRecord *r,int stop,DndEvacResult *out) {
    return dnd_evac_run(journal(),files(),r,stop,scratch,sizeof(scratch),out);
}
#endif
