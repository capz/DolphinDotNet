#include "dnd_verified_copy.h"
#include <assert.h>
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>

static _Alignas(32) unsigned char buffer[DND_COPY_BUFFER];
static unsigned char staged[3*DND_COPY_BUFFER+7],durable[sizeof(staged)];
typedef struct {
    uint64_t length,write_end,reads,writes,progress_durable;
    unsigned events,fail_event,cancel_after,synced,checkpoints,reopens,virtual_data;
    int corrupt_source,corrupt_stage,stale,read_error,write_error,sync_error,checkpoint_error;
    DndCopyCheckpoint cp;
} Fixture;
static unsigned char data_byte(uint64_t at) {return (unsigned char)(at*17u+(at>>16)*31u+3u);}
static int read_source(void *ctx,uint64_t at,void *out,size_t n) {
    Fixture *f=ctx;assert((uintptr_t)out%32==0 && n<=DND_COPY_BUFFER);
    assert(at<=INT32_MAX && n<=f->length-at);f->reads+=n;
    if(f->read_error)return -EIO;
    unsigned char *p=out;for(size_t i=0;i<n;i++)p[i]=data_byte(at+i);
    if(f->corrupt_source && !at)p[0]^=1;
    return 0;
}
static int write_stage(void *ctx,uint64_t at,const void *in,size_t n) {
    Fixture *f=ctx;assert((uintptr_t)in%32==0 && n<=DND_COPY_BUFFER);
    assert(at+n<=f->length);f->writes+=n;f->events++;
    if(f->write_error)return -ENOSPC;
    if(!f->virtual_data)memcpy(staged+at,in,n);
    else {const unsigned char *p=in;for(size_t i=0;i<n;i++)assert(p[i]==data_byte(at+i));}
    f->write_end=at+n;
    return f->events==f->fail_event?-EINTR:0;
}
static int read_stage(void *ctx,uint64_t at,void *out,size_t n) {
    Fixture *f=ctx;assert(f->reopens && n<=DND_COPY_BUFFER && at+n<=f->length);
    f->reads+=n;
    if(f->virtual_data) {
        unsigned char *p=out;for(size_t i=0;i<n;i++)p[i]=data_byte(at+i);
    } else memcpy(out,staged+at,n);
    if(f->corrupt_stage && !at)((unsigned char *)out)[0]^=1;
    return 0;
}
static int sync_stage(void *ctx) {
    Fixture *f=ctx;f->events++;
    if(f->sync_error)return -EIO;
    if(!f->virtual_data)memcpy(durable,staged,sizeof(staged));
    f->synced++;return f->events==f->fail_event?-EINTR:0;
}
static int reopen(void *ctx) {Fixture *f=ctx;f->reopens++;return 0;}
static int checkpoint(void *ctx,const DndCopyCheckpoint *cp) {
    Fixture *f=ctx;assert(f->synced && cp->durable_bytes==f->write_end);
    f->events++;if(f->checkpoint_error)return -EIO;
    f->cp=*cp;f->checkpoints++;
    return f->events==f->fail_event?-EINTR:0;
}
static int revalidate(void *ctx) {Fixture *f=ctx;return f->stale?-ESTALE:0;}
static int cancelled(void *ctx) {
    Fixture *f=ctx;return f->cancel_after && f->events>=f->cancel_after;
}
static void progress(void *ctx,const DndCopyResult *p) {
    Fixture *f=ctx;
    assert(p->durable_bytes<=f->cp.durable_bytes && p->durable_bytes>=f->progress_durable);
    assert(p->verified_bytes<=f->length);f->progress_durable=p->durable_bytes;
}
static DndCopyOps ops(Fixture *f) {
    DndCopyOps o={f,read_source,write_stage,read_stage,sync_stage,reopen,checkpoint,
                  revalidate,cancelled,progress};return o;
}
static void vector(const char *text,const char *expected) {
    DndSha256 s;unsigned char digest[32];dnd_sha256_init(&s);
    for(size_t i=0;i<strlen(text);i++)dnd_sha256_update(&s,text+i,1);
    dnd_sha256_finish(&s,digest);
    char hex[65];for(unsigned i=0;i<32;i++)snprintf(hex+i*2,3,"%02x",digest[i]);
    assert(!strcmp(hex,expected));
}
int main(int argc,char **argv) {
    (void)argv;
    vector("","e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    vector("abc","ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    vector("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq",
           "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1");
    DndSha256 million;dnd_sha256_init(&million);
    memset(buffer,'a',1000);for(unsigned i=0;i<1000;i++)dnd_sha256_update(&million,buffer,1000);
    unsigned char digest[32];dnd_sha256_finish(&million,digest);
    const unsigned char million_expected[32]={0xcd,0xc7,0x6e,0x5c,0x99,0x14,0xfb,0x92,
        0x81,0xa1,0xc7,0xe2,0x84,0xd7,0x3e,0x67,0xf1,0x80,0x9a,0x48,0xa4,0x97,0x20,0x0e,
        0x04,0x6d,0x39,0xcc,0xc7,0x11,0x2c,0xd0};
    assert(!memcmp(digest,million_expected,32));
    DndCopyCheckpoint start={.version=DND_COPY_VERSION,.length=sizeof(staged),.source_cookie={5,7}};
    DndCopyResult result;
    Fixture f={.length=start.length};
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==0);
    assert(result.durable_bytes==start.length && result.verified_bytes==start.length);
    assert(result.bytes_written==start.length && result.bytes_read==2*start.length);
    assert(f.checkpoints==4 && !memcmp(result.sha256,f.cp.prefix_sha256,32));
    /* Crash after each write, sync and persisted checkpoint. Volatile bytes
     * disappear; resume must either verify the durable prefix or fail closed. */
    for(unsigned point=1;point<=12;point++) {
        memset(staged,0,sizeof(staged));memset(durable,0,sizeof(durable));
        f=(Fixture){.length=start.length,.fail_event=point};
        assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-EINTR);
        DndCopyCheckpoint resume=f.cp.version?f.cp:start;
        memcpy(staged,durable,sizeof(staged));f.fail_event=0;f.progress_durable=0;
        assert(dnd_verified_copy(ops(&f),&resume,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==0);
        for(size_t i=0;i<sizeof(staged);i++)assert(staged[i]==data_byte(i));
        assert(result.bytes_written==start.length-resume.durable_bytes);
        /* Idempotent resume of a complete copy performs only validation. */
        resume=f.cp;f.progress_durable=0;
        assert(dnd_verified_copy(ops(&f),&resume,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==0);
        assert(!result.bytes_written && result.verified_bytes==start.length);
    }
    DndCopyCheckpoint resume=f.cp;
    f.corrupt_source=1;
    assert(dnd_verified_copy(ops(&f),&resume,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-EIO);
    f.corrupt_source=0;f.corrupt_stage=1;
    assert(dnd_verified_copy(ops(&f),&resume,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-EIO);
    f.corrupt_stage=0;f.stale=1;
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-ESTALE);
    f=(Fixture){.length=start.length,.sync_error=1};
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-EIO);
    assert(result.durable_bytes==0 && f.cp.version==0);
    f=(Fixture){.length=start.length,.checkpoint_error=1};
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-EIO);
    assert(result.durable_bytes==0);
    f=(Fixture){.length=start.length,.cancel_after=3};
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-ECANCELED);
    assert(result.durable_bytes==DND_COPY_BUFFER && f.cp.durable_bytes==DND_COPY_BUFFER);
    f=(Fixture){.length=start.length,.read_error=1};
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-EIO);
    f=(Fixture){.length=start.length,.write_error=1};
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-ENOSPC);
    assert(dnd_verified_copy(ops(&f),&start,1,buffer,sizeof(buffer),&result)==-EINVAL);
    start.length=(uint64_t)INT32_MAX+1;
    assert(dnd_verified_copy(ops(&f),&start,DND_COPY_BUFFER,buffer,sizeof(buffer),&result)==-EFBIG);
    if(argc>1) {
        start.length=1459978240ULL;
        f=(Fixture){.length=start.length,.virtual_data=1};
        assert(dnd_verified_copy(ops(&f),&start,1024*1024,buffer,sizeof(buffer),&result)==0);
        assert(result.bytes_written==1459978240ULL && result.bytes_read==2919956480ULL);
        assert(result.verified_bytes==1459978240ULL);
        puts("1,459,978,240 virtual bytes copied/verified in 32 KiB RAM; no ISO-sized allocation");
    }
    puts("SHA-256 NIST vectors, durable checkpoints, fault injection, resume, cancellation and limits passed");
    return 0;
}
