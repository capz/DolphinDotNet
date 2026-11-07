#include "dnd_defrag_planner.h"
#include <assert.h>
#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct {
    unsigned char sectors[128][512];
    int read_error;
    unsigned reads;
} Fixture;
static void wr16(unsigned char *p,uint16_t x) {p[0]=(unsigned char)x;p[1]=(unsigned char)(x>>8);}
static void wr32(unsigned char *p,uint32_t x) {wr16(p,(uint16_t)x);wr16(p+2,(uint16_t)(x>>16));}
static void wr64(unsigned char *p,uint64_t x) {wr32(p,(uint32_t)x);wr32(p+4,(uint32_t)(x>>32));}
static int read_sector(void *ctx,uint64_t lba,void *dest) {
    Fixture *f=ctx;
    if(f->read_error)return -EIO;
    if(lba>=128)return -EIO;
    f->reads++;
    memcpy(dest,f->sectors[lba],512);
    return 0;
}
static void fat32(Fixture *f,int offset) {
    memset(f,0,sizeof(*f));
    unsigned char *b=f->sectors[offset],*fat=f->sectors[offset+1];
    wr16(b+11,512);b[13]=1;wr16(b+14,1);b[16]=1;
    wr32(b+32,64);wr32(b+36,1);wr32(b+44,2);
    memcpy(b+82,"FAT32   ",8);b[510]=0x55;b[511]=0xaa;
    for(unsigned i=2;i<64;i++)wr32(fat+i*4,0x0fffffff);
    for(unsigned i=10;i<=12;i++)wr32(fat+i*4,0);
    for(unsigned i=20;i<=26;i++)wr32(fat+i*4,0);
    for(unsigned i=30;i<=32;i++)wr32(fat+i*4,0);
}
static void exfat(Fixture *f,int offset) {
    memset(f,0,sizeof(*f));
    unsigned char *b=f->sectors[offset],*fat=f->sectors[offset+1];
    unsigned char *root=f->sectors[offset+2],*bitmap=f->sectors[offset+3];
    memcpy(b+3,"EXFAT   ",8);wr64(b+72,64);
    wr32(b+80,1);wr32(b+84,1);
    wr32(b+88,2);wr32(b+92,60);wr32(b+96,2);
    b[108]=9;b[109]=0;b[110]=1;b[510]=0x55;b[511]=0xaa;
    wr32(fat+8,0xffffffff);wr32(fat+12,0xffffffff);
    root[0]=0x81;root[1]=0;
    wr32(root+20,3);wr64(root+24,8);
    memset(bitmap,0xff,8);
    for(unsigned i=5;i<=9;i++)bitmap[i/8] &= (unsigned char)~(1u<<(i%8));
    for(unsigned i=22;i<=32;i++)bitmap[i/8] &= (unsigned char)~(1u<<(i%8));
}
int main(void) {
    Fixture f;
    DndSectorReader reader={&f,read_sector,0};
    DndDefragFreeSpace stats={0};
    uint64_t start=0;
    fat32(&f,0);
    assert(dnd_defrag_read_free_space(&reader,&stats)==0);
    assert(stats.filesystem==DND_DEFRAG_FAT32);
    assert(stats.total_clusters==62);
    assert(stats.bytes_per_cluster==512);
    assert(stats.free_clusters==13);
    assert(stats.largest_free_run_clusters==7);
    assert(stats.largest_free_run_start_cluster==20);
    assert(dnd_defrag_preflight_direct(&stats,7*512,&start)==1 && start==20);
    assert(dnd_defrag_preflight_direct(&stats,7*512+1,&start)==0 && start==0);
    assert(dnd_defrag_preflight_direct(&stats,1459978240ULL,&start)==0);
    assert(dnd_defrag_preflight_direct(&stats,(uint64_t)INT32_MAX+1,&start)==-EFBIG);
    assert(dnd_defrag_preflight_direct(&stats,0,&start)==1 && start==0);
    reader.partition_lba=4;
    fat32(&f,4);
    assert(dnd_defrag_read_free_space(&reader,&stats)==0);
    assert(stats.largest_free_run_start_cluster==20);
    f.read_error=1;
    stats.free_clusters=999;
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    assert(stats.free_clusters==999); /* Fail without publishing partial stats. */
    f.read_error=0;
    reader.partition_lba=0;
    exfat(&f,0);
    assert(dnd_defrag_read_free_space(&reader,&stats)==0);
    assert(stats.filesystem==DND_DEFRAG_EXFAT);
    assert(stats.total_clusters==60 && stats.free_clusters==16);
    assert(stats.largest_free_run_clusters==11);
    assert(stats.largest_free_run_start_cluster==24);
    assert(dnd_defrag_preflight_direct(&stats,11*512,&start)==1 && start==24);
    f.sectors[2][0]=0; /* Missing allocation bitmap. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    exfat(&f,0);
    wr64(f.sectors[2]+24,3); /* Bitmap too short for 60 clusters. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    exfat(&f,0);
    f.sectors[0][110]=2; /* TexFAT dual bitmap needs an active-bitmap policy. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-ENOTSUP);
    fat32(&f,0);
    f.sectors[0][13]=3; /* Invalid non-power-of-two cluster size. */
    assert(dnd_defrag_read_free_space(&reader,&stats)==-EIO);
    assert(dnd_defrag_read_free_space(NULL,&stats)==-EINVAL);
    assert(dnd_defrag_read_free_space(&reader,NULL)==-EINVAL);
    puts("read-only FAT32/exFAT free-run preflight passed");
    return 0;
}
