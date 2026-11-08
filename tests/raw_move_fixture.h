#ifndef DND_RAW_MOVE_FIXTURE_H
#define DND_RAW_MOVE_FIXTURE_H
#include "dnd_raw_move.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

#define RM_SECTORS 256
#define RM_PATCHES 32
#define RM_BYTES (4*512-11)
typedef struct {
    unsigned char request[512],patch[RM_PATCHES][2048],checkpoint[2][512];
    int request_exists,patch_exists[RM_PATCHES],checkpoint_exists[2];
} RawReplica;
typedef struct {
    _Alignas(32) unsigned char image[RM_SECTORS][512],disk[RM_SECTORS][512],original[RM_SECTORS][512];
    DndRawPatch builder[RM_PATCHES];unsigned count;
    RawReplica journal[2],saved[2];
    unsigned event,fail_event,cancel_event,tear_mode,cut,raw_writes,write_event[64],record_writes,syncs;
    int missing[2],stale,cancelled,read_error,sync_error,drop_write,exfat,nofat,active;
    unsigned root,fat,heap,offset;
} RawFixture;
static RawFixture rf,initial;
static DndDefragFile rm_files[32];static DndDefragRun rm_runs[128];
static DndDefragSnapshot rm_snapshot={.files=rm_files,.runs=rm_runs,.file_capacity=32,.run_capacity=128};
static _Alignas(32) unsigned char rm_transfer[DND_COPY_BUFFER];
static void rm_p16(unsigned char *p,uint16_t x) {p[0]=(unsigned char)x;p[1]=(unsigned char)(x>>8);}
static void rm_p32(unsigned char *p,uint32_t x) {rm_p16(p,(uint16_t)x);rm_p16(p+2,(uint16_t)(x>>16));}
static void rm_p64(unsigned char *p,uint64_t x) {rm_p32(p,(uint32_t)x);rm_p32(p+4,(uint32_t)(x>>32));}
static uint32_t rm_u32(const unsigned char *p) {return p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static int rm_tick(RawFixture *f) {
    f->event++;if(f->event==f->cancel_event)f->cancelled=1;
    return f->event==f->fail_event?-EINTR:0;
}
static unsigned char rm_pattern(uint64_t offset) {
    uint32_t key=(uint32_t)(offset/512)+0x9e3779b9u;
    key^=key>>16;key*=0x7feb352du;key^=key>>15;key*=0x846ca68bu;key^=key>>16;
    return (unsigned char)((offset*17u+3u)^(key>>((offset%4)*8)));
}
static unsigned rm_source(RawFixture *f,unsigned logical) {return f->nofat?8+logical:(f->exfat?8:4)+logical*4;}
static int rm_guard(void *ctx,const DndRawMove *r) {
    RawFixture *f=ctx;
    if(f->stale || r->image_id[0]!=17 || r->image_id[1]!=19)return -ESTALE;
    return f->missing[0]||f->missing[1]?-ENODEV:0;
}
static int rm_read(void *ctx,uint64_t lba,void *out) {
    RawFixture *f=ctx;assert((uintptr_t)out%32==0);
    if(f->read_error || lba>=RM_SECTORS)return -EIO;
    memcpy(out,f->image[lba],512);return 0;
}
static int rm_write(void *ctx,uint64_t lba,const void *in) {
    RawFixture *f=ctx;assert(lba<RM_SECTORS && (uintptr_t)in%32==0);
    if(f->raw_writes<64)f->write_event[f->raw_writes]=f->event+1;
    f->raw_writes++;int rc=rm_tick(f);
    size_t n=rc?f->cut:512;
    if(!f->drop_write) {
        if(rc && f->tear_mode==1)memcpy(f->image[lba]+512-n,(const unsigned char *)in+512-n,n);
        else if(rc && f->tear_mode==2) {
            for(unsigned i=0;i<512;i++)if((i*17+f->cut)%7<3)f->image[lba][i]=((const unsigned char *)in)[i];
        } else memcpy(f->image[lba],in,n);
    }
    if(rc)memcpy(f->disk[lba],f->image[lba],512);
    return rc;
}
static int rm_sync_image(void *ctx) {
    RawFixture *f=ctx;f->syncs++;
    if(f->sync_error)return -EIO;
    memcpy(f->disk,f->image,sizeof(f->disk));return rm_tick(f);
}
static int rm_invalidate(void *ctx) {(void)ctx;return 0;}
static int rm_read_record(void *ctx,unsigned replica,unsigned kind,uint64_t i,void *out,size_t n) {
    RawFixture *f=ctx;if(f->missing[replica])return -ENODEV;
    RawReplica *v=f->journal+replica;
    if(kind==0) {assert(!i && n==512);if(!v->request_exists)return -ENOENT;memcpy(out,v->request,512);}
    else if(kind==1) {assert(i<RM_PATCHES && n==2048);if(!v->patch_exists[i])return -ENOENT;memcpy(out,v->patch[i],2048);}
    else {assert(kind==2 && i<2 && n==512);if(!v->checkpoint_exists[i])return -ENOENT;memcpy(out,v->checkpoint[i],512);}
    return 0;
}
static int rm_write_record(void *ctx,unsigned replica,unsigned kind,uint64_t i,const void *in,size_t n) {
    RawFixture *f=ctx;if(f->missing[replica])return -ENODEV;
    RawReplica *v=f->journal+replica;f->record_writes++;int rc=rm_tick(f);
    unsigned char *out=NULL;
    if(kind==0) {assert(!i && n==512);out=v->request;v->request_exists=1;}
    else if(kind==1) {assert(i<RM_PATCHES && n==2048);out=v->patch[i];v->patch_exists[i]=1;}
    else {assert(kind==2 && i<2 && n==512);out=v->checkpoint[i];v->checkpoint_exists[i]=1;}
    size_t bytes=rc && f->cut<n?f->cut:n;memcpy(out,in,bytes);
    if(rc)f->saved[replica]=*v;
    return rc;
}
static int rm_sync_journal(void *ctx,unsigned replica) {
    RawFixture *f=ctx;if(f->missing[replica])return -ENODEV;
    f->saved[replica]=f->journal[replica];return rm_tick(f);
}
static int rm_build_reset(void *ctx) {((RawFixture *)ctx)->count=0;return 0;}
static int rm_build_get(void *ctx,uint64_t lba,DndRawPatch *out) {
    RawFixture *f=ctx;for(unsigned i=0;i<f->count;i++)if(f->builder[i].lba==lba) {*out=f->builder[i];return 0;}
    return -ENOENT;
}
static int rm_build_put(void *ctx,const DndRawPatch *p) {
    RawFixture *f=ctx;
    for(unsigned i=0;i<f->count;i++)if(f->builder[i].lba==p->lba) {f->builder[i]=*p;return 0;}
    if(f->count>=RM_PATCHES)return -ENOSPC;
    f->builder[f->count++]=*p;return 0;
}
static int rm_build_count(void *ctx,uint64_t *out) {*out=((RawFixture *)ctx)->count;return 0;}
static int rm_build_read(void *ctx,uint64_t i,DndRawPatch *out) {
    RawFixture *f=ctx;if(i>=f->count)return -ENOENT;*out=f->builder[i];return 0;
}
static int rm_cancelled(void *ctx) {return ((RawFixture *)ctx)->cancelled;}
static DndRawOps rm_ops(void) {
    DndRawOps o={.context=&rf,.medium_kind=DND_RAW_IMAGE,.guard=rm_guard,.read_sector=rm_read,
        .write_sector=rm_write,.sync_image=rm_sync_image,.invalidate=rm_invalidate,
        .read_record=rm_read_record,.write_record=rm_write_record,.sync_journal=rm_sync_journal,
        .build_reset=rm_build_reset,.build_get=rm_build_get,.build_put=rm_build_put,
        .build_count=rm_build_count,.build_read=rm_build_read,.cancelled=rm_cancelled};return o;
}
static void rm_reboot(void) {
    memcpy(rf.image,rf.disk,sizeof(rf.image));memcpy(rf.journal,rf.saved,sizeof(rf.journal));
    rf.fail_event=0;rf.cancel_event=0;rf.event=0;rf.cancelled=0;
}
static uint16_t rm_set_sum(unsigned char *e,unsigned n) {
    uint16_t sum=0;for(unsigned i=0;i<n*32;i++) {
        if(i==2 || i==3)continue;
        sum=(uint16_t)(((sum&1)?0x8000:0)+(sum>>1)+e[i]);
    }return sum;
}
static void rm_fat32(int active,unsigned offset) {
    memset(&rf,0,sizeof(rf));rf.active=active;rf.offset=offset;rf.fat=offset+8;rf.heap=offset+10;rf.root=rf.heap;
    unsigned char *b=rf.image[offset];
    rm_p16(b+11,512);b[13]=1;b[21]=0xf8;rm_p16(b+14,8);b[16]=2;rm_p32(b+32,110);rm_p32(b+36,1);
    rm_p32(b+44,2);rm_p16(b+48,1);rm_p16(b+50,6);rm_p16(b+40,active?0x81:0);
    memcpy(b+82,"FAT32   ",8);b[510]=0x55;b[511]=0xaa;memcpy(rf.image[offset+6],b,512);
    for(unsigned copy=0;copy<2;copy++) {
        unsigned char *fat=rf.image[rf.fat+copy];
        rm_p32(fat,0x0ffffff8);rm_p32(fat+4,0x0fffffff);rm_p32(fat+8,0x0fffffff);
        rm_p32(fat+5*4,0x0fffffff);
        for(unsigned i=0;i<4;i++)rm_p32(fat+rm_source(&rf,i)*4,i==3?0x0fffffff:rm_source(&rf,i+1));
        rm_p32(fat+30*4,0xa0000000);rm_p32(fat+31*4,0xb0000000);
    }
    if(active)rf.image[rf.fat][100]^=0x55;
    unsigned char *info=rf.image[offset+1];rm_p32(info,0x41615252);rm_p32(info+484,0x61417272);
    rm_p32(info+488,94);rm_p32(info+492,30);rm_p32(info+508,0xaa550000);
    memcpy(rf.image[offset+7],info,512);
    unsigned char *entry=rf.image[rf.root];memcpy(entry,"GAME    ISO",11);rm_p16(entry+26,4);rm_p32(entry+28,RM_BYTES);
    entry+=32;memcpy(entry,"SAVE    GCI",11);rm_p16(entry+26,5);rm_p32(entry+28,512);
    memset(rf.image[rf.heap+3],0x5a,512);
}
static void rm_exfat(int nofat,unsigned offset) {
    memset(&rf,0,sizeof(rf));rf.exfat=1;rf.nofat=nofat;rf.offset=offset;rf.fat=offset+24;
    rf.heap=offset+25;rf.root=rf.heap;
    unsigned char *b=rf.image[offset];memcpy(b+3,"EXFAT   ",8);rm_p64(b+64,offset);rm_p64(b+72,125);
    rm_p32(b+80,24);rm_p32(b+84,1);rm_p32(b+88,25);rm_p32(b+92,100);rm_p32(b+96,2);
    rm_p16(b+104,0x100);b[108]=9;b[109]=0;b[110]=1;b[112]=8;b[510]=0x55;b[511]=0xaa;
    uint32_t checksum=0;
    for(unsigned s=0;s<11;s++)for(unsigned i=0;i<512;i++) {
        if(!s && (i==106 || i==107 || i==112))continue;
        checksum=((checksum>>1)|(checksum<<31))+rf.image[offset+s][i];
    }
    for(unsigned i=0;i<512;i+=4)rm_p32(rf.image[offset+11]+i,checksum);
    memcpy(rf.image[offset+12],rf.image[offset],12*512);
    unsigned char *fat=rf.image[rf.fat];
    rm_p32(fat,0xfffffff8);rm_p32(fat+4,0xffffffff);
    rm_p32(fat+8,0xffffffff);rm_p32(fat+12,0xffffffff);rm_p32(fat+16,0xffffffff);
    if(!nofat)for(unsigned i=0;i<4;i++)rm_p32(fat+rm_source(&rf,i)*4,i==3?0xffffffff:rm_source(&rf,i+1));
    unsigned char *root=rf.image[rf.root];root[0]=0x81;rm_p32(root+20,3);rm_p64(root+24,13);
    root[32]=0x82;rm_p32(root+52,4);rm_p64(root+56,512);
    unsigned char *entry=root+64;entry[0]=0x85;entry[1]=2;entry[32]=0xc0;entry[33]=(unsigned char)(nofat?3:1);
    entry[35]=4;rm_p64(entry+40,RM_BYTES);rm_p32(entry+52,rm_source(&rf,0));rm_p64(entry+56,RM_BYTES);
    entry[64]=0xc1;const char *name="game";for(unsigned i=0;i<4;i++)rm_p16(entry+66+i*2,(unsigned char)name[i]);
    rm_p16(entry+2,rm_set_sum(entry,3));
    unsigned char *bits=rf.image[rf.heap+1];
    for(unsigned i=0;i<3;i++)bits[0]|=(unsigned char)(1u<<i);
    for(unsigned i=0;i<4;i++) {unsigned c=rm_source(&rf,i)-2;bits[c/8]|=(unsigned char)(1u<<(c%8));}
}
static void rm_finish_fixture(void) {
    for(unsigned i=0;i<4;i++)for(unsigned k=0;k<512;k++)
        rf.image[rf.heap+rm_source(&rf,i)-2][k]=rm_pattern(i*512+k);
    memcpy(rf.original,rf.image,sizeof(rf.image));memcpy(rf.disk,rf.image,sizeof(rf.image));rf.cut=512;
}
static int rm_plan(DndRawMove *r,uint64_t first,uint64_t count,uint32_t dest) {
    DndSectorReader reader={&rf,rm_read,rf.offset};DndDefragDevice device={{17,19},1,1};DndDefragScanOps ops={0};
    int rc=dnd_defrag_scan(&reader,device,ops,&rm_snapshot);if(rc)return rc;
    rc=dnd_raw_move_plan(rm_ops(),&reader,device,RM_SECTORS,&rm_snapshot,1,first,count,dest,r,rm_transfer,sizeof(rm_transfer));
    if(!rc) {
        r->operation[0]=23;r->operation[1]=29;DndSha256 sha;dnd_sha256_init(&sha);
        dnd_sha256_update(&sha,rf.original,sizeof(rf.original));dnd_sha256_finish(&sha,r->backup_sha256);
    }return rc;
}
static void rm_oracle(void) {
    unsigned char *entry=rf.image[rf.root]+(rf.exfat?64:0);
    uint32_t c=rf.exfat?rm_u32(entry+52):rm_u32(entry+26)&0xffff;
    int nofat=rf.exfat && (entry[33]&2);unsigned char used[102]={0};
    for(unsigned i=0;i<4;i++) {
        assert(c>=2 && c<102 && !used[c]);used[c]=1;
        unsigned char *data=rf.image[rf.heap+c-2];
        for(unsigned k=0;k<512;k++)assert(data[k]==rm_pattern(i*512+k));
        if(rf.exfat)assert(rf.image[rf.heap+1][(c-2)/8]&(1u<<((c-2)%8)));
        if(i<3)c=nofat?c+1:rm_u32(rf.image[rf.fat+rf.active]+c*4)&(rf.exfat?0xffffffffu:0x0fffffffu);
    }
    if(rf.exfat) {
        assert(rm_u32(entry+56)==RM_BYTES && rm_set_sum(entry,3)==(entry[2]|(unsigned)entry[3]<<8));
        assert(!(rf.image[rf.offset][106]&2) && !(rf.image[rf.offset+12][106]&2));
    } else {
        assert(rm_u32(entry+28)==RM_BYTES);
        assert(!memcmp(rf.image[rf.heap+3],rf.original[rf.heap+3],512));
        for(unsigned i=0;i<2;i++)if(!rf.active || i==1) {
            assert((rm_u32(rf.image[rf.fat+i]+30*4)&0xf0000000)==0xa0000000);
            assert((rm_u32(rf.image[rf.fat+i]+31*4)&0xf0000000)==0xb0000000);
        }
        if(rf.active)assert(!memcmp(rf.image[rf.fat],rf.original[rf.fat],512));
        else assert(!memcmp(rf.image[rf.fat],rf.image[rf.fat+1],512));
        assert(!memcmp(rf.image[rf.offset+1],rf.original[rf.offset+1],512));
    }
    assert(dnd_raw_move_mount_allowed(rm_ops(),&(DndRawMove){0})<0);
}
#endif
