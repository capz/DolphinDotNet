#include "dnd_raw_move_internal.h"
#include <errno.h>
#include <limits.h>
#include <string.h>

static int checked(int rc) {return rc>0?-EIO:rc;}
static void sha(const void *data,size_t n,unsigned char out[32]) {
    DndSha256 h;dnd_sha256_init(&h);dnd_sha256_update(&h,data,n);dnd_sha256_finish(&h,out);
}
static int nonzero(const unsigned char *p,size_t n) {
    unsigned char bits=0;while(n--)bits|=*p++;return bits!=0;
}
static int callbacks(DndRawOps o) {
    return o.guard && o.read_sector && o.write_sector && o.sync_image && o.invalidate &&
        o.read_record && o.write_record && o.sync_journal && o.build_reset && o.build_get &&
        o.build_put && o.build_count && o.build_read;
}
static int guard(DndRawOps o,const DndRawMove *r) {
    if(o.medium_kind!=DND_RAW_IMAGE)return -ENOTSUP;
    return checked(o.guard(o.context,r));
}
static int cancel(DndRawOps o) {return o.cancelled && o.cancelled(o.context)?-ECANCELED:0;}
typedef struct {
    DndRawOps original;
    const DndRawMove *request;
    unsigned char binding[32];
    uint64_t patch_count;
    int overlay,cancellable;
    uint64_t dirty_lba[2];unsigned dirty_count;
} Reader;
static int guarded_read(void *ctx,uint64_t lba,void *out) {
    Reader *c=ctx;
    if(lba>=c->request->sector_count)return -EIO;
    int rc=c->cancellable?cancel(c->original):0;if(rc)return rc;
    rc=guard(c->original,c->request);if(rc)return rc;
    return checked(c->original.read_sector(c->original.context,lba,out));
}
static int guarded_write(void *ctx,uint64_t lba,const void *data) {
    Reader *c=ctx;
    if(lba>=c->request->sector_count)return -EIO;
    int rc=c->cancellable?cancel(c->original):0;if(rc)return rc;
    rc=guard(c->original,c->request);if(rc)return rc;
    return checked(c->original.write_sector(c->original.context,lba,data));
}
static int read_patch(Reader *c,unsigned replica,uint64_t index,DndRawPatch *out) {
    if(index>=c->patch_count)return -EILSEQ;
    unsigned char wire[DND_RAW_PATCH_BYTES];
    int rc=checked(c->original.read_record(c->original.context,replica,1,index,wire,sizeof(wire)));
    if(rc)return rc;
    rc=dnd_raw_patch_decode(wire,index,c->binding,out);if(rc)return rc;
    return out->lba<c->request->partition_lba || out->lba>=c->request->sector_count?-EILSEQ:0;
}
static int original_read(void *ctx,uint64_t lba,void *out) {
    Reader *c=ctx;
    if(c->overlay) {
        uint64_t index=0;
        if(c->original.find_patch) {
            int rc=checked(c->original.find_patch(c->original.context,0,lba,&index));
            if(rc && rc!=-ENOENT)return rc;
            if(!rc) {
                DndRawPatch p;rc=read_patch(c,0,index,&p);if(rc)return rc;
                if(p.lba!=lba)return -EILSEQ;
                memcpy(out,p.before,512);return 0;
            }
        }
        for(index=0;index<c->patch_count;index++) {
            DndRawPatch p;int rc=read_patch(c,0,index,&p);if(rc)return rc;
            if(p.lba==lba) {memcpy(out,p.before,512);return 0;}
        }
    }
    return guarded_read(c,lba,out);
}
static int bridge_get(void *ctx,uint64_t lba,DndRawPatch *p) {
    Reader *c=ctx;return c->original.build_get(c->original.context,lba,p);
}
static int bridge_put(void *ctx,const DndRawPatch *p) {
    Reader *c=ctx;return c->original.build_put(c->original.context,p);
}
static int bridge_reset(void *ctx) {
    Reader *c=ctx;return c->original.build_reset(c->original.context);
}
static int bridge_cancelled(void *ctx) {
    Reader *c=ctx;return c->cancellable && cancel(c->original)!=0;
}
static DndRawOps fs_ops(Reader *c) {
    DndRawOps io={0};io.context=c;
    io.cancelled=bridge_cancelled;
    io.read_sector=guarded_read;io.write_sector=guarded_write;
    io.build_get=bridge_get;io.build_put=bridge_put;io.build_reset=bridge_reset;
    return io;
}
static int destination_free(const DndDefragSnapshot *m,const DndRawMove *r) {
    uint64_t end=(uint64_t)r->destination+r->cluster_count,current=r->destination;
    if(r->destination<2 || end>m->space.total_clusters+2)return -EINVAL;
    for(size_t i=0;i<m->run_count && current<end;i++) {
        const DndDefragRun *p=m->runs+i;
        if(p->start+p->count<=current)continue;
        if(p->start>current || p->owner!=DND_DEFRAG_FREE)return -ENOSPC;
        current=p->start+p->count;
    }
    return current>=end?0:-ENOSPC;
}
static int open_original(Reader *c,DndDefragSnapshot *workspace,DndRawFs *fs,
                           void *transfer,DndRawResult *metrics) {
    DndSectorReader reader={c,original_read,c->request->partition_lba};
    unsigned char meta[32],boot[32],file[32],window[32];
    int rc=dnd_raw_fs_open(fs_ops(c),&reader,c->request,workspace,1,1,fs,meta,boot);
    if(rc)return rc;
    c->dirty_count=fs->exfat?2:fs->mirrored?fs->fats:1;
    for(unsigned i=0;i<c->dirty_count;i++)c->dirty_lba[i]=fs->exfat?
        c->request->partition_lba+i*12:fs->fat_lba+(uint64_t)i*fs->fat_sectors;
    if(memcmp(meta,c->request->metadata_sha256,32) || memcmp(boot,c->request->boot_sha256,32))return -ESTALE;
    rc=destination_free(workspace,c->request);if(rc)return rc;
    rc=dnd_raw_fs_hash(fs,c->request,transfer,metrics,file,window);if(rc)return rc;
    return memcmp(file,c->request->file_sha256,32) || memcmp(window,c->request->window_sha256,32)?-ESTALE:0;
}
static int verify_current(Reader *c,DndDefragSnapshot *workspace,void *transfer,
                            int clean,DndRawResult *metrics) {
    DndSectorReader reader={c,guarded_read,c->request->partition_lba};DndRawFs fs;
    unsigned char meta[32],boot[32],file[32],window[32];
    int rc=dnd_raw_fs_open(fs_ops(c),&reader,c->request,workspace,0,clean,&fs,meta,boot);
    if(rc)return rc;
    rc=dnd_raw_fs_hash(&fs,c->request,transfer,metrics,file,window);if(rc)return rc;
    if(memcmp(file,c->request->file_sha256,32))return -EIO;
    metrics->after_extents=0;
    for(size_t i=0;i<workspace->run_count;i++) {
        uint32_t owner=workspace->runs[i].owner;
        if(owner<workspace->file_count && workspace->files[owner].entry_lba==c->request->entry_lba &&
           workspace->files[owner].entry_offset==c->request->entry_offset)metrics->after_extents++;
    }
    return 0;
}
int dnd_raw_move_plan(DndRawOps o,const DndSectorReader *reader,DndDefragDevice device,
                        uint64_t sectors,DndDefragSnapshot *m,uint32_t file,
                        uint64_t first,uint64_t count,uint32_t dest,DndRawMove *out,
                        void *transfer,size_t cap) {
    if(!out || !reader || !reader->read_sector || !m || !m->valid || file>=m->file_count ||
       !o.read_sector || m->space.bytes_per_cluster<512 || m->space.bytes_per_cluster>16777216 ||
       (m->space.bytes_per_cluster&(m->space.bytes_per_cluster-1)) || !transfer || (uintptr_t)transfer%32 || cap<DND_COPY_BUFFER ||
       !(device.physical_id[0]|device.physical_id[1]) || !device.writable ||
       reader->partition_lba>=sectors || !count || dest<2)return -EINVAL;
    DndDefragFile selected=m->files[file];
    if(selected.flags&(DND_DEFRAG_PINNED|DND_DEFRAG_DIRECTORY))return -ENOTSUP;
    if(!selected.bytes || selected.bytes>INT32_MAX)return -EFBIG;
    uint64_t n=(selected.bytes+m->space.bytes_per_cluster-1)/m->space.bytes_per_cluster;
    if(first>=n || count>n-first || count>UINT32_MAX-dest)return -EINVAL;
    DndRawMove r={.image_id={device.physical_id[0],device.physical_id[1]},
        .partition_lba=reader->partition_lba,.sector_count=sectors,.generation=device.generation,
        .entry_lba=selected.entry_lba,.file_bytes=selected.bytes,.logical_first=first,
        .cluster_count=count,.destination=dest,.original_first=selected.first_cluster,
        .file_flags=selected.flags,.filesystem=(uint32_t)m->space.filesystem,
        .bytes_per_cluster=m->space.bytes_per_cluster,.entry_offset=selected.entry_offset,
        .attributes=selected.attributes};
    int rc=destination_free(m,&r);if(rc)return rc;
    DndRawFs fs;
    rc=dnd_raw_fs_open(o,reader,&r,m,1,1,&fs,r.metadata_sha256,r.boot_sha256);if(rc)return rc;
    rc=destination_free(m,&r);if(rc)return rc;
    rc=dnd_raw_fs_hash(&fs,&r,transfer,NULL,r.file_sha256,r.window_sha256);if(rc)return rc;
    *out=r;return 0;
}
static int same_checkpoint(const DndRawCheckpoint *a,const DndRawCheckpoint *b) {
    return a->sequence==b->sequence && a->state==b->state && a->patch_count==b->patch_count &&
        !memcmp(a->request_sha256,b->request_sha256,32) && !memcmp(a->patches_sha256,b->patches_sha256,32);
}
static int follows(const DndRawCheckpoint *a,const DndRawCheckpoint *b) {
    return a->sequence!=UINT64_MAX && b->sequence==a->sequence+1 &&
        a->patch_count==b->patch_count && !memcmp(a->request_sha256,b->request_sha256,32) &&
        !memcmp(a->patches_sha256,b->patches_sha256,32) &&
        ((a->state<DND_RAW_WINDOW_COMPLETE && b->state==a->state+1) ||
         (a->state<DND_RAW_COMMITTED && b->state==DND_RAW_ROLLED_BACK));
}
static int inspect(DndRawOps o,const DndRawMove *r,DndRawCheckpoint *out,int *replicated) {
    unsigned char expected[512],binding[32];int rc=dnd_raw_request_encode(r,expected);if(rc)return rc;
    sha(expected,512,binding);
    DndRawCheckpoint heads[2];int have[2]={0},requests[2]={0},absent=0,corrupt=0;
    memset(out,0,sizeof(*out));*replicated=0;
    for(unsigned v=0;v<2;v++) {
        unsigned char wire[512];rc=checked(o.read_record(o.context,v,0,0,wire,512));
        if(rc==-ENODEV) {absent=1;continue;}
        if(!rc) {if(memcmp(wire,expected,512))return -ESTALE;requests[v]=1;}
        else if(rc!=-ENOENT)return rc;
        DndRawCheckpoint records[2];int valid[2]={0};
        for(unsigned slot=0;slot<2;slot++) {
            rc=checked(o.read_record(o.context,v,2,slot,wire,512));
            if(rc==-ENODEV) {absent=1;continue;}
            if(rc==-ENOENT)continue;
            if(rc)return rc;
            rc=dnd_raw_checkpoint_decode(wire,&records[slot]);
            if(rc || (records[slot].sequence&1)!=slot) {corrupt=1;continue;}
            if(!requests[v] || memcmp(records[slot].request_sha256,binding,32) ||
               records[slot].patch_count>r->sector_count)return -EILSEQ;
            valid[slot]=1;
        }
        if(valid[0] || valid[1]) {
            unsigned latest=valid[1] && (!valid[0] || records[1].sequence>records[0].sequence)?1:0;
            if(valid[0] && valid[1] && !follows(&records[1-latest],&records[latest]))return -EILSEQ;
            heads[v]=records[latest];have[v]=1;
        }
    }
    if(absent)return DND_RAW_NEEDS_PARTNER;
    if(!have[0] && !have[1]) {
        if(corrupt)return -EILSEQ;
        return requests[0]||requests[1]?DND_RAW_PENDING:DND_RAW_NONE;
    }
    if(corrupt && (!have[0] || !have[1]))return -EILSEQ;
    unsigned latest=have[1] && (!have[0] || heads[1].sequence>heads[0].sequence)?1:0;
    if(have[0] && have[1]) {
        if(same_checkpoint(&heads[0],&heads[1]))*replicated=1;
        else if(!follows(&heads[1-latest],&heads[latest]))return -EILSEQ;
    }
    *out=heads[latest];return DND_RAW_PENDING;
}
int dnd_raw_move_inspect(DndRawOps o,const DndRawMove *r,DndRawCheckpoint *out) {
    if(!r || !out || !o.read_record)return -EINVAL;
    int replicas=0;return inspect(o,r,out,&replicas);
}
static int write_confirm(DndRawOps o,unsigned replica,unsigned kind,uint64_t index,
                            const void *wire,size_t size,DndRawResult *metrics) {
    unsigned char check[DND_RAW_PATCH_BYTES];
    if(size>sizeof(check))return -EINVAL;
    int rc=checked(o.write_record(o.context,replica,kind,index,wire,size));if(rc)return rc;
    metrics->journal_bytes_written+=size;
    rc=checked(o.sync_journal(o.context,replica));if(rc)return rc;
    rc=checked(o.read_record(o.context,replica,kind,index,check,size));if(rc)return rc;
    return memcmp(check,wire,size)?-EIO:0;
}
static int checkpoint(DndRawOps o,const DndRawMove *r,DndRawCheckpoint *cp,unsigned state,
                         int same,DndRawResult *metrics) {
    DndRawCheckpoint next=*cp;
    if(!same) {if(cp->sequence==UINT64_MAX)return -EOVERFLOW;next.sequence++;next.state=state;}
    unsigned char wire[512];int rc=dnd_raw_checkpoint_encode(&next,wire);if(rc)return rc;
    for(unsigned v=0;v<2;v++) {rc=guard(o,r);if(rc)return rc;}
    for(unsigned v=0;v<2;v++) {
        rc=write_confirm(o,v,2,next.sequence&1,wire,512,metrics);if(rc)return rc;
    }
    *cp=next;metrics->state=cp->state;return 0;
}
static int compare_inventory(Reader *c,const DndRawCheckpoint *cp,int preparing,DndRawResult *metrics) {
    uint64_t count=0;int rc=checked(c->original.build_count(c->original.context,&count));if(rc)return rc;
    if(!count || count>c->request->sector_count || (!preparing && count!=cp->patch_count))return -EILSEQ;
    DndSha256 root;dnd_sha256_init(&root);
    for(uint64_t i=0;i<count;i++) {
        DndRawPatch p;rc=checked(c->original.build_read(c->original.context,i,&p));if(rc)return rc;
        if(p.lba<c->request->partition_lba || p.lba>=c->request->sector_count)return -EILSEQ;
        unsigned char wire[DND_RAW_PATCH_BYTES],other[DND_RAW_PATCH_BYTES];
        rc=dnd_raw_patch_encode(&p,i,c->binding,wire);if(rc)return rc;
        dnd_sha256_update(&root,wire,sizeof(wire));
        for(unsigned v=0;v<2;v++) {
            if(preparing) {
                rc=guard(c->original,c->request);if(rc)return rc;
                rc=write_confirm(c->original,v,1,i,wire,sizeof(wire),metrics);
            } else {
                rc=checked(c->original.read_record(c->original.context,v,1,i,other,sizeof(other)));
                if(!rc && memcmp(wire,other,sizeof(wire)))rc=-EILSEQ;
            }
            if(rc)return rc;
        }
    }
    unsigned char hash[32];dnd_sha256_finish(&root,hash);
    if(!preparing && memcmp(hash,cp->patches_sha256,32))return -EILSEQ;
    return 0;
}
static int inventory_root(DndRawOps o,const unsigned char binding[32],uint64_t count,unsigned char out[32]) {
    DndSha256 sha256;dnd_sha256_init(&sha256);
    for(uint64_t i=0;i<count;i++) {
        DndRawPatch p;int rc=checked(o.build_read(o.context,i,&p));if(rc)return rc;
        unsigned char wire[DND_RAW_PATCH_BYTES];rc=dnd_raw_patch_encode(&p,i,binding,wire);if(rc)return rc;
        dnd_sha256_update(&sha256,wire,sizeof(wire));
    }
    dnd_sha256_finish(&sha256,out);return 0;
}
static int apply(Reader *c,int mode,int recovery,DndRawResult *metrics) {
    for(unsigned pass=0;pass<2;pass++) {
      for(uint64_t i=0;i<c->patch_count;i++) {
        DndRawPatch p;int rc=read_patch(c,0,i,&p);if(rc)return rc;
        int dirty=0;for(unsigned d=0;d<c->dirty_count;d++)dirty|=p.lba==c->dirty_lba[d];
        if(dirty!=(mode==1?(pass==0):(pass==1)))continue;
        _Alignas(32) unsigned char current[512];rc=guarded_read(c,p.lba,current);if(rc)return rc;
        const unsigned char *target=mode==0?p.before:mode==1?p.applied:p.cleaned;
        if(!memcmp(current,target,512))continue;
        if(!recovery && memcmp(current,mode==1?p.before:p.applied,512))return -ESTALE;
        if(recovery)for(unsigned k=0;k<512;k++) {
            /* Interrupted prefix/suffix/mixed/zero-filled writes are replayable
             * ONLY inside a proven operation-owned sector. Foreign bytes are
             * not silently overwritten. Before images bind untouched fields. */
            if(current[k]!=p.before[k] && current[k]!=p.applied[k] &&
               current[k]!=p.cleaned[k] && current[k]!=0)return -ESTALE;
        }
        rc=guarded_write(c,p.lba,target);if(rc)return rc;
        metrics->metadata_sectors_written++;
    }
      int synced=checked(c->original.sync_image(c->original.context));if(synced)return synced;
    }
    int rc=checked(c->original.sync_image(c->original.context));if(rc)return rc;
    rc=checked(c->original.invalidate(c->original.context));if(rc)return rc;
    for(uint64_t i=0;i<c->patch_count;i++) {
        DndRawPatch p;rc=read_patch(c,0,i,&p);if(rc)return rc;
        _Alignas(32) unsigned char b[512];rc=guarded_read(c,p.lba,b);if(rc)return rc;
        const unsigned char *target=mode==0?p.before:mode==1?p.applied:p.cleaned;
        if(memcmp(b,target,512))return -EIO;
    }
    return 0;
}
static int arguments(DndRawOps o,const DndRawMove *r,void *transfer,size_t cap,Reader *reader) {
    if(!callbacks(o) || !r || !transfer || (uintptr_t)transfer%32 || cap<DND_COPY_BUFFER)return -EINVAL;
    if(o.medium_kind!=DND_RAW_IMAGE)return -ENOTSUP;
    if(!nonzero(r->backup_sha256,32))return -EINVAL;
    unsigned char wire[512];int rc=dnd_raw_request_encode(r,wire);if(rc)return rc;
    memset(reader,0,sizeof(*reader));reader->original=o;reader->request=r;sha(wire,512,reader->binding);
    return guard(o,r);
}
int dnd_raw_move_execute(DndRawOps o,const DndRawMove *r,DndDefragSnapshot *m,
                           void *transfer,size_t cap,DndRawResult *out) {
    if(!out || !m)return -EINVAL;
    memset(out,0,sizeof(*out));
    Reader reader;int rc=arguments(o,r,transfer,cap,&reader);if(rc)return rc;
    reader.cancellable=1;
    DndRawCheckpoint cp;int both=0;rc=inspect(o,r,&cp,&both);
    if(rc<0)return rc;
    if(rc==DND_RAW_NEEDS_PARTNER)return -ENODEV;
    if(rc!=DND_RAW_NONE)return -EINPROGRESS;
    rc=cancel(o);if(rc)return rc;
    rc=checked(o.invalidate(o.context));if(rc)return rc;
    DndRawFs fs;rc=open_original(&reader,m,&fs,transfer,out);if(rc)return rc;
    out->before_extents=0;
    for(size_t i=0;i<m->run_count;i++) {
        uint32_t owner=m->runs[i].owner;
        if(owner<m->file_count && m->files[owner].entry_lba==r->entry_lba &&
           m->files[owner].entry_offset==r->entry_offset)out->before_extents++;
    }
    rc=dnd_raw_fs_build(&fs,r);if(rc)return rc;
    memset(&cp,0,sizeof(cp));memcpy(cp.request_sha256,reader.binding,32);
    rc=checked(o.build_count(o.context,&cp.patch_count));if(rc)return rc;
    if(!cp.patch_count || cp.patch_count>r->sector_count)return -EILSEQ;
    rc=inventory_root(o,reader.binding,cp.patch_count,cp.patches_sha256);if(rc)return rc;
    /* Complete immutable manifests/sector images precede the first image write. */
    unsigned char wire[512];rc=dnd_raw_request_encode(r,wire);if(rc)return rc;
    for(unsigned v=0;v<2;v++) {rc=write_confirm(o,v,0,0,wire,512,out);if(rc)return rc;}
    rc=compare_inventory(&reader,&cp,1,out);if(rc)return rc;
    rc=compare_inventory(&reader,&cp,0,out);if(rc)return rc;
    reader.patch_count=cp.patch_count;
    rc=checkpoint(o,r,&cp,DND_RAW_PREPARED,0,out);if(rc)return rc;
    rc=cancel(o);if(rc)return rc;
    rc=dnd_raw_fs_copy(&fs,r,transfer,out);if(rc)return rc;
    rc=checked(o.sync_image(o.context));if(rc)return rc;
    rc=checked(o.invalidate(o.context));if(rc)return rc;
    rc=dnd_raw_fs_destination(&fs,r,transfer,out);if(rc)return rc;
    rc=checkpoint(o,r,&cp,DND_RAW_DESTINATION_VERIFIED,0,out);if(rc)return rc;
    /* Source and unrelated metadata must still match before publishing intent. */
    rc=open_original(&reader,m,&fs,transfer,out);if(rc)return rc;
    rc=cancel(o);if(rc)return rc;
    rc=checkpoint(o,r,&cp,DND_RAW_METADATA_INTENT,0,out);if(rc)return rc;
    rc=apply(&reader,1,0,out);if(rc)return rc;
    rc=verify_current(&reader,m,transfer,0,out);if(rc)return rc;
    rc=checkpoint(o,r,&cp,DND_RAW_METADATA_APPLIED,0,out);if(rc)return rc;
    rc=cancel(o);if(rc)return rc;
    rc=checkpoint(o,r,&cp,DND_RAW_COMMITTED,0,out);if(rc)return rc;
    reader.cancellable=0;
    rc=checkpoint(o,r,&cp,DND_RAW_CLEANUP_INTENT,0,out);if(rc)return rc;
    rc=apply(&reader,2,0,out);if(rc)return rc;
    rc=verify_current(&reader,m,transfer,1,out);if(rc)return rc;
    return checkpoint(o,r,&cp,DND_RAW_WINDOW_COMPLETE,0,out);
}
int dnd_raw_move_recover(DndRawOps o,const DndRawMove *r,DndDefragSnapshot *m,
                           void *transfer,size_t cap,DndRawResult *out) {
    if(!out || !m)return -EINVAL;
    memset(out,0,sizeof(*out));out->recovered=1;
    Reader reader;int rc=arguments(o,r,transfer,cap,&reader);if(rc)return rc;
    DndRawCheckpoint cp;int both=0;rc=inspect(o,r,&cp,&both);
    if(rc<0)return rc;
    if(rc==DND_RAW_NEEDS_PARTNER)return -ENODEV;
    if(rc==DND_RAW_NONE)return 0;
    /* An incomplete preparation never authorizes metadata writes. Keep it
     * quarantined for explicit inspection; do not invent a committed state. */
    if(!cp.sequence)return -EINPROGRESS;
    rc=checked(o.invalidate(o.context));if(rc)return rc;
    if(cp.state==DND_RAW_WINDOW_COMPLETE || cp.state==DND_RAW_ROLLED_BACK) {
        /* Terminal transactions never replay historical sector images after
         * later accepted moves may have reused their old source clusters. */
        rc=verify_current(&reader,m,transfer,1,out);if(rc)return rc;
        if(!both) {rc=checkpoint(o,r,&cp,cp.state,1,out);if(rc)return rc;}
        out->state=cp.state;out->rolled_back=cp.state==DND_RAW_ROLLED_BACK;return 0;
    }
    reader.patch_count=cp.patch_count;reader.overlay=1;
    DndRawFs fs;rc=open_original(&reader,m,&fs,transfer,out);if(rc)return rc;
    rc=dnd_raw_fs_build(&fs,r);if(rc)return rc;
    rc=compare_inventory(&reader,&cp,0,out);if(rc)return rc;
    if(!both) {rc=checkpoint(o,r,&cp,cp.state,1,out);if(rc)return rc;}
    if(cp.state<DND_RAW_COMMITTED) {
        rc=apply(&reader,0,1,out);if(rc)return rc;
        reader.overlay=0;rc=open_original(&reader,m,&fs,transfer,out);if(rc)return rc;
        out->rolled_back=1;
        return checkpoint(o,r,&cp,DND_RAW_ROLLED_BACK,0,out);
    }
    /* Recheck surviving destination data before repairing any committed map. */
    rc=dnd_raw_fs_destination(&fs,r,transfer,out);if(rc)return rc;
    rc=apply(&reader,1,1,out);if(rc)return rc;
    rc=verify_current(&reader,m,transfer,0,out);if(rc)return rc;
    if(cp.state<DND_RAW_CLEANUP_INTENT) {
        rc=checkpoint(o,r,&cp,DND_RAW_CLEANUP_INTENT,0,out);if(rc)return rc;
    }
    rc=apply(&reader,2,1,out);if(rc)return rc;
    rc=verify_current(&reader,m,transfer,1,out);if(rc)return rc;
    return checkpoint(o,r,&cp,DND_RAW_WINDOW_COMPLETE,0,out);
}
int dnd_raw_move_mount_allowed(DndRawOps o,const DndRawMove *r) {
    DndRawCheckpoint cp;int both=0;
    if(!callbacks(o) || !r)return -EINVAL;
    int rc=guard(o,r);if(rc)return rc;
    rc=inspect(o,r,&cp,&both);if(rc<0)return rc;
    return rc==DND_RAW_PENDING && both &&
        (cp.state==DND_RAW_WINDOW_COMPLETE || cp.state==DND_RAW_ROLLED_BACK)?1:0;
}
