#ifndef DND_EVAC_FIXTURE_H
#define DND_EVAC_FIXTURE_H
typedef struct {
    unsigned char wire[2][2][DND_EVAC_WIRE_BYTES],disk[2][2][DND_EVAC_WIRE_BYTES];
    int exists[2][2],saved_exists[2][2],missing[2],swapped[2];
    unsigned mutations,fail_event,event;
} Media;
static Media media,checkpoint_media;
static DndEvacRecord base_record(void) {
    DndEvacRecord r={.operation={42,73},.sequence=1,.target_id={1,2},.scratch_id={3,4},
        .target_partition=17,.scratch_partition=31,.target_serial=101,.scratch_serial=102,
        .bytes=1459978240,.mtime=123,.atime=124,.state=DND_EVAC_PREPARED,
        .strategy=DND_DEFRAG_EVACUATE_ISO,.attributes=32,
        .target_cluster_bytes=32768,.scratch_cluster_bytes=4096};
    strcpy(r.relative_path,"games/日本.iso");return r;
}
static int probe(void *ctx,unsigned v,uint64_t id[2],uint64_t *part,uint64_t *serial) {
    Media *m=ctx;if(m->missing[v])return -ENODEV;
    id[0]=v?3:1;id[1]=v?4:2;
    if(m->swapped[v])id[0]++;
    *part=v?31:17;*serial=v?102:101;return 0;
}
static int read_slot(void *ctx,unsigned v,unsigned slot,void *wire) {
    Media *m=ctx;if(m->missing[v])return -ENODEV;
    if(!m->exists[v][slot])return -ENOENT;
    memcpy(wire,m->wire[v][slot],DND_EVAC_WIRE_BYTES);return 0;
}
static int write_slot(void *ctx,unsigned v,unsigned slot,const void *wire) {
    Media *m=ctx;if(m->missing[v])return -ENODEV;
    m->mutations++;m->event++;m->exists[v][slot]=1;
    if(m->event==m->fail_event) {
        memcpy(m->wire[v][slot],wire,DND_EVAC_WIRE_BYTES/2);
        /* A torn write can reach stable media despite a later failed sync. */
        memcpy(m->disk[v][slot],m->wire[v][slot],DND_EVAC_WIRE_BYTES);
        m->saved_exists[v][slot]=1;return -EINTR;
    }
    memcpy(m->wire[v][slot],wire,DND_EVAC_WIRE_BYTES);return 0;
}
static int sync_volume(void *ctx,unsigned v) {
    Media *m=ctx;m->event++;
    if(m->event==m->fail_event)return -EINTR;
    memcpy(m->disk[v],m->wire[v],sizeof(m->disk[v]));
    memcpy(m->saved_exists[v],m->exists[v],sizeof(m->exists[v]));return 0;
}
static void reboot(Media *m) {
    memcpy(m->wire,m->disk,sizeof(m->wire));memcpy(m->exists,m->saved_exists,sizeof(m->exists));
    m->fail_event=0;m->event=0;
}
static DndEvacJournalOps ops(Media *m) {
    DndEvacJournalOps o={m,probe,read_slot,write_slot,sync_volume};return o;
}
static DndEvacRecord next(DndEvacRecord r) {
    r.sequence++;r.state++;
    if(r.state==DND_EVAC_COPYING || r.state==DND_EVAC_RESTORING) {
        r.durable_bytes=0;memset(r.prefix_sha256,0,32);
    }
    if(r.state==DND_EVAC_SCRATCH_VERIFIED || r.state==DND_EVAC_TARGET_VERIFIED) {
        r.durable_bytes=r.bytes;memset(r.file_sha256,0x5a,32);
        memcpy(r.prefix_sha256,r.file_sha256,32);
    }
    return r;
}
#endif
