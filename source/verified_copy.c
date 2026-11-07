#include "dnd_verified_copy.h"
#include <errno.h>
#include <limits.h>
#include <string.h>

static uint32_t rotate(uint32_t x,unsigned n) {return (x>>n)|(x<<(32-n));}
static void transform(DndSha256 *s,const unsigned char *p) {
    static const uint32_t k[64]={
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
    uint32_t w[64];
    for(unsigned i=0;i<16;i++)w[i]=((uint32_t)p[i*4]<<24)|((uint32_t)p[i*4+1]<<16)|
                                      ((uint32_t)p[i*4+2]<<8)|p[i*4+3];
    for(unsigned i=16;i<64;i++) {
        uint32_t a=w[i-15],b=w[i-2];
        w[i]=w[i-16]+(rotate(a,7)^rotate(a,18)^(a>>3))+w[i-7]+
                       (rotate(b,17)^rotate(b,19)^(b>>10));
    }
    uint32_t a=s->state[0],b=s->state[1],c=s->state[2],d=s->state[3];
    uint32_t e=s->state[4],f=s->state[5],g=s->state[6],h=s->state[7];
    for(unsigned i=0;i<64;i++) {
        uint32_t t=h+(rotate(e,6)^rotate(e,11)^rotate(e,25))+
                      ((e&f)^(~e&g))+k[i]+w[i];
        uint32_t u=(rotate(a,2)^rotate(a,13)^rotate(a,22))+((a&b)^(a&c)^(b&c));
        h=g;g=f;f=e;e=d+t;d=c;c=b;b=a;a=t+u;
    }
    s->state[0]+=a;s->state[1]+=b;s->state[2]+=c;s->state[3]+=d;
    s->state[4]+=e;s->state[5]+=f;s->state[6]+=g;s->state[7]+=h;
}
void dnd_sha256_init(DndSha256 *s) {
    static const uint32_t initial[8]={0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,
                                     0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
    memset(s,0,sizeof(*s));memcpy(s->state,initial,sizeof(initial));
}
void dnd_sha256_update(DndSha256 *s,const void *data,size_t bytes) {
    const unsigned char *p=data;s->bytes+=bytes;
    while(bytes) {
        size_t n=64-s->used;if(n>bytes)n=bytes;
        memcpy(s->block+s->used,p,n);s->used+=n;p+=n;bytes-=n;
        if(s->used==64) {transform(s,s->block);s->used=0;}
    }
}
void dnd_sha256_finish(const DndSha256 *input,unsigned char out[32]) {
    DndSha256 s=*input;
    uint64_t bits=s.bytes*8;
    unsigned char padding[128]={0x80};
    size_t n=s.used<56?56-s.used:120-s.used;
    for(unsigned i=0;i<8;i++)padding[n+i]=(unsigned char)(bits>>(56-i*8));
    dnd_sha256_update(&s,padding,n+8);
    for(unsigned i=0;i<32;i++)out[i]=(unsigned char)(s.state[i/4]>>(24-(i%4)*8));
}
static int guard(DndCopyOps ops) {
    if(ops.cancelled && ops.cancelled(ops.context))return -ECANCELED;
    int rc=ops.revalidate(ops.context);
    return rc>0?-EIO:rc;
}
static int checked(int rc) {return rc>0?-EIO:rc;}
static size_t chunk(uint64_t remaining) {
    return remaining<DND_COPY_BUFFER?(size_t)remaining:DND_COPY_BUFFER;
}
int dnd_verified_copy(DndCopyOps ops,const DndCopyCheckpoint *resume,
                       uint64_t interval,void *scratch,size_t capacity,
                       DndCopyResult *result) {
    if(!result)return -EINVAL;
    memset(result,0,sizeof(*result));
    if(!resume || resume->version!=DND_COPY_VERSION ||
       resume->durable_bytes>resume->length || !interval ||
       interval%DND_COPY_BUFFER || !scratch || capacity<DND_COPY_BUFFER ||
       (uintptr_t)scratch%32 || !ops.read_source || !ops.write_stage ||
       !ops.read_stage || !ops.sync_stage || !ops.reopen_stage ||
       !ops.checkpoint || !ops.revalidate)return -EINVAL;
    if(resume->length>INT32_MAX)return -EFBIG;
    DndCopyCheckpoint cp=*resume;
    DndSha256 source,stage;dnd_sha256_init(&source);dnd_sha256_init(&stage);
    int rc=guard(ops);
    if(rc)return rc;
    if(cp.durable_bytes) {
        rc=checked(ops.reopen_stage(ops.context));if(rc)return rc;
        for(uint64_t at=0;at<cp.durable_bytes;) {
            if((rc=guard(ops)))return rc;
            size_t n=chunk(cp.durable_bytes-at);
            rc=checked(ops.read_source(ops.context,at,scratch,n));if(rc)return rc;
            result->bytes_read+=n;dnd_sha256_update(&source,scratch,n);
            rc=checked(ops.read_stage(ops.context,at,scratch,n));if(rc)return rc;
            result->bytes_read+=n;dnd_sha256_update(&stage,scratch,n);at+=n;
        }
        unsigned char a[32],b[32];dnd_sha256_finish(&source,a);dnd_sha256_finish(&stage,b);
        if(memcmp(a,cp.prefix_sha256,32) || memcmp(b,cp.prefix_sha256,32))return -EIO;
    }
    result->durable_bytes=cp.durable_bytes;
    uint64_t last_sync=cp.durable_bytes;
    for(uint64_t at=cp.durable_bytes;at<cp.length;) {
        if((rc=guard(ops)))return rc;
        size_t n=chunk(cp.length-at);
        rc=checked(ops.read_source(ops.context,at,scratch,n));if(rc)return rc;
        result->bytes_read+=n;dnd_sha256_update(&source,scratch,n);
        rc=checked(ops.write_stage(ops.context,at,scratch,n));if(rc)return rc;
        result->bytes_written+=n;at+=n;
        if(at-last_sync>=interval || at==cp.length) {
            if((rc=guard(ops)))return rc;
            rc=checked(ops.sync_stage(ops.context));if(rc)return rc;
            cp.durable_bytes=at;dnd_sha256_finish(&source,cp.prefix_sha256);
            rc=checked(ops.checkpoint(ops.context,&cp));if(rc)return rc;
            result->durable_bytes=at;last_sync=at;
            if(ops.progress)ops.progress(ops.context,result);
        }
    }
    dnd_sha256_finish(&source,result->sha256);
    rc=checked(ops.reopen_stage(ops.context));if(rc)return rc;
    dnd_sha256_init(&stage);
    for(uint64_t at=0;at<cp.length;) {
        if((rc=guard(ops)))return rc;
        size_t n=chunk(cp.length-at);
        rc=checked(ops.read_stage(ops.context,at,scratch,n));if(rc)return rc;
        result->bytes_read+=n;dnd_sha256_update(&stage,scratch,n);at+=n;
        result->verified_bytes=at;
        if(ops.progress)ops.progress(ops.context,result);
    }
    unsigned char digest[32];dnd_sha256_finish(&stage,digest);
    if(memcmp(digest,result->sha256,32))return -EIO;
    return guard(ops);
}
