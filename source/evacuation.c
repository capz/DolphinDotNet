#include "dnd_evacuation.h"
#include <errno.h>
#include <string.h>

static int checked(int rc) {return rc>0?-EIO:rc;}
static int same_request(const DndEvacRecord *a,const DndEvacRecord *b) {
    DndEvacRecord request=*a;
    request.sequence=b->sequence;request.state=b->state;
    request.durable_bytes=b->durable_bytes;
    memcpy(request.prefix_sha256,b->prefix_sha256,32);
    /* A trusted caller may pin a manifest digest in the initial request. */
    unsigned char nonzero=0;for(unsigned i=0;i<32;i++)nonzero|=a->file_sha256[i];
    if(!nonzero)memcpy(request.file_sha256,b->file_sha256,32);
    unsigned char x[DND_EVAC_WIRE_BYTES],y[DND_EVAC_WIRE_BYTES];
    return !dnd_evac_encode(&request,x) && !dnd_evac_encode(b,y) && !memcmp(x,y,sizeof(x));
}
static int advance(DndEvacJournalOps journal,DndEvacRecord *r,unsigned state) {
    DndEvacRecord next=*r;next.sequence++;next.state=state;
    if(state==DND_EVAC_COPYING || state==DND_EVAC_RESTORING) {
        next.durable_bytes=0;memset(next.prefix_sha256,0,32);
    }
    int rc=dnd_evac_advance(journal,r,&next);
    if(!rc)*r=next;
    return rc;
}
typedef struct {
    DndEvacJournalOps journal;
    DndEvacFileOps files;
    DndEvacRecord *record;
    DndCopyOps io;
} Stream;
static int source_read(void *ctx,uint64_t at,void *buffer,size_t n) {
    Stream *s=ctx;return s->io.read_source(s->io.context,at,buffer,n);
}
static int stage_write(void *ctx,uint64_t at,const void *buffer,size_t n) {
    Stream *s=ctx;return s->io.write_stage(s->io.context,at,buffer,n);
}
static int stage_read(void *ctx,uint64_t at,void *buffer,size_t n) {
    Stream *s=ctx;return s->io.read_stage(s->io.context,at,buffer,n);
}
static int sync_stage(void *ctx) {
    Stream *s=ctx;return s->io.sync_stage(s->io.context);
}
static int reopen_stage(void *ctx) {
    Stream *s=ctx;return s->io.reopen_stage(s->io.context);
}
static int revalidate(void *ctx) {
    Stream *s=ctx;
    int rc=checked(s->files.guard(s->files.context,s->record));
    return rc?rc:s->io.revalidate(s->io.context);
}
static int cancelled(void *ctx) {
    Stream *s=ctx;
    return (s->files.cancelled && s->files.cancelled(s->files.context)) ||
           (s->io.cancelled && s->io.cancelled(s->io.context));
}
static void progress(void *ctx,const DndCopyResult *p) {
    Stream *s=ctx;if(s->io.progress)s->io.progress(s->io.context,p);
}
static int checkpoint(void *ctx,const DndCopyCheckpoint *cp) {
    Stream *s=ctx;DndEvacRecord next=*s->record;
    next.sequence++;next.durable_bytes=cp->durable_bytes;
    memcpy(next.prefix_sha256,cp->prefix_sha256,32);
    int rc=dnd_evac_advance(s->journal,s->record,&next);
    if(!rc)*s->record=next;
    return rc;
}
static int transfer(DndEvacJournalOps journal,DndEvacFileOps files,
                      DndEvacRecord *r,void *buffer,size_t capacity,
                      DndCopyResult *total) {
    Stream s={.journal=journal,.files=files,.record=r};
    int rc=checked(files.open_copy(files.context,r,r->state==DND_EVAC_RESTORING,&s.io));
    if(rc)return rc;
    DndCopyResult result={0};
    if(!s.io.read_source || !s.io.write_stage || !s.io.read_stage ||
       !s.io.sync_stage || !s.io.reopen_stage || !s.io.revalidate)rc=-EINVAL;
    DndCopyOps o={&s,source_read,stage_write,stage_read,sync_stage,reopen_stage,
                   checkpoint,revalidate,cancelled,progress};
    DndCopyCheckpoint cp={.version=DND_COPY_VERSION,.length=r->bytes,
                         .durable_bytes=r->durable_bytes,
                         .source_cookie={r->operation[0],r->operation[1]}};
    memcpy(cp.prefix_sha256,r->prefix_sha256,32);
    if(!rc)rc=dnd_verified_copy(o,&cp,1024*1024,buffer,capacity,&result);
    int closed=checked(files.close_copy(files.context));if(!rc)rc=closed;
    total->bytes_read+=result.bytes_read;total->bytes_written+=result.bytes_written;
    total->durable_bytes=result.durable_bytes;total->verified_bytes=result.verified_bytes;
    if(rc)return rc;
    if(r->state==DND_EVAC_COPYING) {
        unsigned char pinned=0;for(unsigned i=0;i<32;i++)pinned|=r->file_sha256[i];
        if(pinned && memcmp(r->file_sha256,result.sha256,32))return -EIO;
    } else if(memcmp(r->file_sha256,result.sha256,32))return -EIO;
    memcpy(total->sha256,result.sha256,32);
    DndEvacRecord next=*r;next.sequence++;
    if(r->state==DND_EVAC_COPYING) {
        next.state=DND_EVAC_SCRATCH_VERIFIED;memcpy(next.file_sha256,result.sha256,32);
    } else {
        /* The filesystem callback must check physical contiguity and metadata
         * even when all staged bytes already match the expected checksum. */
        rc=checked(files.verify(files.context,r,DND_EVAC_TARGET_STAGE));
        if(rc)return rc;
        next.state=DND_EVAC_TARGET_VERIFIED;
    }
    rc=dnd_evac_advance(journal,r,&next);if(!rc)*r=next;return rc;
}
int dnd_evac_run(DndEvacJournalOps journal,DndEvacFileOps files,
                   const DndEvacRecord *expected,int keep_evacuated,
                   void *buffer,size_t capacity,DndEvacResult *out) {
    if(!out)return -EINVAL;
    memset(out,0,sizeof(*out));
    unsigned char wire[DND_EVAC_WIRE_BYTES];
    if(!expected || dnd_evac_encode(expected,wire) || expected->sequence!=1 ||
       expected->state!=DND_EVAC_PREPARED || !buffer || (uintptr_t)buffer%32 ||
       capacity<DND_COPY_BUFFER || !files.guard || !files.stages_absent ||
       !files.open_copy || !files.close_copy || !files.verify ||
       !files.release_source || !files.publish || !files.remove_scratch)return -EINVAL;
    DndEvacRecord *r=&out->record;
    int rc=dnd_evac_inspect(journal,r);out->recovery_state=rc;
    if(rc<0)return rc;
    if(rc==DND_EVAC_NEEDS_SCRATCH || rc==DND_EVAC_NEEDS_TARGET)return rc;
    int state=rc;
    rc=checked(files.guard(files.context,expected));if(rc)return rc;
    rc=state;
    if(rc==DND_EVAC_CLEAN) {
        if(files.cancelled && files.cancelled(files.context))return -ECANCELED;
        rc=checked(files.stages_absent(files.context,expected));if(rc)return rc;
        rc=dnd_evac_begin(journal,expected);if(rc)return rc;*r=*expected;
    } else {
        if(!same_request(expected,r))return -ESTALE;
        if(rc==DND_EVAC_RECONCILE) {
            rc=dnd_evac_reconcile(journal,r);if(rc)return rc;
        }
    }
    for(;;) {
        out->recovery_state=DND_EVAC_PENDING;
        rc=checked(files.guard(files.context,r));if(rc)return rc;
        if(files.cancelled && files.cancelled(files.context))return -ECANCELED;
        switch(r->state) {
        case DND_EVAC_PREPARED:
            rc=advance(journal,r,DND_EVAC_COPYING);break;
        case DND_EVAC_COPYING:case DND_EVAC_RESTORING:
            rc=transfer(journal,files,r,buffer,capacity,&out->io);break;
        case DND_EVAC_SCRATCH_VERIFIED:
            rc=checked(files.verify(files.context,r,DND_EVAC_SCRATCH));
            if(!rc)rc=checked(files.verify(files.context,r,DND_EVAC_ORIGINAL));
            if(!rc)rc=advance(journal,r,DND_EVAC_RELEASE_READY);
            break;
        case DND_EVAC_RELEASE_READY:
            rc=checked(files.verify(files.context,r,DND_EVAC_SCRATCH));if(rc)break;
            rc=checked(files.verify(files.context,r,DND_EVAC_ORIGINAL));
            if(!rc)rc=checked(files.release_source(files.context,r));
            else if(rc==-ENOENT)rc=0; /* Delete completed before interruption. */
            if(!rc)rc=checked(journal.sync_volume(journal.context,0));
            if(!rc)rc=advance(journal,r,DND_EVAC_SOURCE_RELEASED);
            break;
        case DND_EVAC_SOURCE_RELEASED:
            rc=checked(files.verify(files.context,r,DND_EVAC_SCRATCH));
            if(!rc && keep_evacuated)return 0;
            if(!rc)rc=advance(journal,r,DND_EVAC_RESTORING);
            break;
        case DND_EVAC_TARGET_VERIFIED:
            rc=checked(files.verify(files.context,r,DND_EVAC_TARGET_STAGE));
            if(!rc)rc=advance(journal,r,DND_EVAC_PUBLISH_READY);
            break;
        case DND_EVAC_PUBLISH_READY:
            rc=checked(files.verify(files.context,r,DND_EVAC_ORIGINAL));
            if(rc==-ENOENT) {
                rc=checked(files.verify(files.context,r,DND_EVAC_TARGET_STAGE));
                if(!rc)rc=checked(files.publish(files.context,r));
            }
            if(!rc)rc=checked(journal.sync_volume(journal.context,0));
            if(!rc)rc=advance(journal,r,DND_EVAC_PUBLISHED);
            break;
        case DND_EVAC_PUBLISHED:
            rc=checked(files.verify(files.context,r,DND_EVAC_ORIGINAL));if(rc)break;
            rc=checked(files.remove_scratch(files.context,r));
            if(rc==-ENOENT)rc=0;
            if(!rc)rc=checked(journal.sync_volume(journal.context,1));
            if(!rc)rc=advance(journal,r,DND_EVAC_DONE);
            break;
        case DND_EVAC_DONE:
            return checked(files.verify(files.context,r,DND_EVAC_ORIGINAL));
        case DND_EVAC_ABORTED:return -ECANCELED;
        default:return -EILSEQ;
        }
        if(rc)return rc;
    }
}
