#include "dnd_raw_move.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

#define CLUSTERS 100000u
#ifndef RM_LARGE_SPC
#define RM_LARGE_SPC 64u
#endif
#define SPC RM_LARGE_SPC
#define FAT_SECTORS ((CLUSTERS+2u+127u)/128u)
#define HEAP (FAT_SECTORS+1u)
#define ISO_BYTES 1459978240ULL
#define ISO_CLUSTERS ((ISO_BYTES+SPC*512u-1)/(SPC*512u))
#define DEST 50000u
#define PATCHES 1024u
static struct {
    unsigned char boot[512],fat[FAT_SECTORS][512],root[512];
    DndRawPatch builder[PATCHES];unsigned count;
    unsigned char request[2][512],patches[2][PATCHES][2048],checkpoint[2][2][512];
    int request_exists[2],patch_exists[2][PATCHES],checkpoint_exists[2][2];
    uint64_t written;
} disk;
static void p16(unsigned char *p,uint16_t x) {p[0]=(unsigned char)x;p[1]=(unsigned char)(x>>8);}
static void p32(unsigned char *p,uint32_t x) {p16(p,(uint16_t)x);p16(p+2,(uint16_t)(x>>16));}
static uint32_t u32(const unsigned char *p) {return p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static void set_fat(unsigned c,uint32_t v) {p32(disk.fat[c/128]+(c%128)*4,v);}
static void data_sector(void *out,uint64_t logical_sector) {
    uint32_t key=(uint32_t)logical_sector+0x9e3779b9u;
    key^=key>>16;key*=0x7feb352du;key^=key>>15;key*=0x846ca68bu;key^=key>>16;
    unsigned char *p=out;for(unsigned i=0;i<512;i++)p[i]=(unsigned char)((i*17u+3u)^(key>>((i%4)*8)));
}
static int guard(void *ctx,const DndRawMove *r) {
    (void)ctx;return r->image_id[0]==71 && r->image_id[1]==73?0:-ESTALE;
}
static int read_sector(void *ctx,uint64_t lba,void *out) {
    (void)ctx;assert((uintptr_t)out%32==0);
    if(lba>=HEAP+(uint64_t)CLUSTERS*SPC)return -EIO;
    if(!lba)memcpy(out,disk.boot,512);
    else if(lba<=FAT_SECTORS)memcpy(out,disk.fat[lba-1],512);
    else if(lba==HEAP)memcpy(out,disk.root,512);
    else if(lba>=HEAP+SPC && lba<HEAP+(ISO_CLUSTERS+1)*SPC)
        data_sector(out,lba-(HEAP+SPC));
    else if(lba>=HEAP+(DEST-2ULL)*SPC && lba<HEAP+(DEST-2ULL)*SPC+disk.written)
        data_sector(out,lba-(HEAP+(DEST-2ULL)*SPC));
    else memset(out,0,512);
    return 0;
}
static int write_sector(void *ctx,uint64_t lba,const void *in) {
    (void)ctx;assert((uintptr_t)in%32==0);
    if(lba>=1 && lba<=FAT_SECTORS)memcpy(disk.fat[lba-1],in,512);
    else if(lba==HEAP)memcpy(disk.root,in,512);
    else {
        assert(lba==HEAP+(DEST-2ULL)*SPC+disk.written && disk.written<ISO_CLUSTERS*SPC);
        _Alignas(32) unsigned char expected[512];data_sector(expected,disk.written);
        assert(!memcmp(expected,in,512));disk.written++;
    }return 0;
}
static int zero(void *ctx) {(void)ctx;return 0;}
static int sync_journal(void *ctx,unsigned v) {(void)ctx;(void)v;return 0;}
static int read_record(void *ctx,unsigned v,unsigned kind,uint64_t i,void *out,size_t bytes) {
    (void)ctx;
    if(kind==0) {if(!disk.request_exists[v])return -ENOENT;assert(bytes==512);memcpy(out,disk.request[v],512);}
    else if(kind==1) {assert(i<PATCHES && bytes==2048);if(!disk.patch_exists[v][i])return -ENOENT;memcpy(out,disk.patches[v][i],2048);}
    else {assert(kind==2 && i<2 && bytes==512);if(!disk.checkpoint_exists[v][i])return -ENOENT;memcpy(out,disk.checkpoint[v][i],512);}
    return 0;
}
static int write_record(void *ctx,unsigned v,unsigned kind,uint64_t i,const void *in,size_t bytes) {
    (void)ctx;
    if(kind==0) {assert(bytes==512);disk.request_exists[v]=1;memcpy(disk.request[v],in,512);}
    else if(kind==1) {assert(i<PATCHES && bytes==2048);disk.patch_exists[v][i]=1;memcpy(disk.patches[v][i],in,2048);}
    else {assert(kind==2 && i<2 && bytes==512);disk.checkpoint_exists[v][i]=1;memcpy(disk.checkpoint[v][i],in,512);}
    return 0;
}
static int build_reset(void *ctx) {(void)ctx;disk.count=0;return 0;}
static int build_get(void *ctx,uint64_t lba,DndRawPatch *p) {
    (void)ctx;for(unsigned i=0;i<disk.count;i++)if(disk.builder[i].lba==lba) {*p=disk.builder[i];return 0;}
    return -ENOENT;
}
static int build_put(void *ctx,const DndRawPatch *p) {
    (void)ctx;for(unsigned i=0;i<disk.count;i++)if(disk.builder[i].lba==p->lba) {disk.builder[i]=*p;return 0;}
    if(disk.count==PATCHES)return -ENOSPC;
    disk.builder[disk.count++]=*p;return 0;
}
static int build_count(void *ctx,uint64_t *n) {(void)ctx;*n=disk.count;return 0;}
static int build_read(void *ctx,uint64_t i,DndRawPatch *p) {
    (void)ctx;if(i>=disk.count)return -ENOENT;*p=disk.builder[i];return 0;
}
int main(void) {
    unsigned char *b=disk.boot;
    p16(b+11,512);b[13]=SPC;b[21]=0xf8;p16(b+14,1);b[16]=1;
    p32(b+32,HEAP+CLUSTERS*SPC);p32(b+36,FAT_SECTORS);p32(b+44,2);
    memcpy(b+82,"FAT32   ",8);b[510]=0x55;b[511]=0xaa;
    set_fat(0,0x0ffffff8);set_fat(1,0x0fffffff);set_fat(2,0x0fffffff);
    for(unsigned i=0;i<ISO_CLUSTERS;i++)set_fat(3+i,i+1==ISO_CLUSTERS?0x0fffffff:4+i);
    for(unsigned i=0;i<ISO_CLUSTERS;i++)set_fat(DEST+i,0xc0000000);
    memcpy(disk.root,"LARGE   ISO",11);disk.root[11]=32;p16(disk.root+26,3);p32(disk.root+28,ISO_BYTES);
    DndRawOps o={.medium_kind=DND_RAW_IMAGE,.guard=guard,.read_sector=read_sector,.write_sector=write_sector,
        .sync_image=zero,.invalidate=zero,.read_record=read_record,.write_record=write_record,
        .sync_journal=sync_journal,.build_reset=build_reset,.build_get=build_get,.build_put=build_put,
        .build_count=build_count,.build_read=build_read};
    DndDefragFile files[8];DndDefragRun runs[32];DndDefragSnapshot m={.files=files,.runs=runs,.file_capacity=8,.run_capacity=32};
    DndSectorReader reader={NULL,read_sector,0};DndDefragDevice device={{71,73},1,1};DndDefragScanOps policy={0};
    assert(dnd_defrag_scan(&reader,device,policy,&m)==0);
    _Alignas(32) unsigned char transfer[DND_COPY_BUFFER];DndRawMove r;
    assert(dnd_raw_move_plan(o,&reader,device,HEAP+(uint64_t)CLUSTERS*SPC,&m,1,0,ISO_CLUSTERS,DEST,&r,transfer,sizeof(transfer))==0);
    r.operation[0]=79;r.operation[1]=83;
    /* The simulated backup is independently reconstructible from immutable
     * geometry and the seeded original data generator; no ISO-sized array. */
    memset(r.backup_sha256,0x55,32);
    DndRawResult result;
    int rc=dnd_raw_move_execute(o,&r,&m,transfer,sizeof(transfer),&result);
    if(rc)fprintf(stderr,"large virtual raw move failed: %d state %u\n",rc,result.state);
    assert(rc==0 && result.state==DND_RAW_WINDOW_COMPLETE && result.after_extents==1);
    assert(result.data_bytes_written==ISO_CLUSTERS*SPC*512 && disk.written*512==result.data_bytes_written);
    assert(r.file_bytes==ISO_BYTES);
    assert((disk.root[26]|(unsigned)disk.root[27]<<8)==DEST);
    for(unsigned i=0;i<ISO_CLUSTERS;i++) {
        assert((u32(disk.fat[(3+i)/128]+((3+i)%128)*4)&0x0fffffff)==0);
        assert(u32(disk.fat[(DEST+i)/128]+((DEST+i)%128)*4)==
               (0xc0000000u|(i+1==ISO_CLUSTERS?0x0fffffffu:DEST+i+1)));
    }
    assert(dnd_raw_move_mount_allowed(o,&r)==1);
    printf("P6 virtual full ISO: %llu bytes moved, %llu verified read bytes, %llu metadata sector writes; 32 KiB transfer buffer\n",
        (unsigned long long)result.data_bytes_written,(unsigned long long)result.data_bytes_read,
        (unsigned long long)result.metadata_sectors_written);
    return 0;
}
