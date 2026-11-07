#define _POSIX_C_SOURCE 200809L
#include "dnd_defrag_planner.h"
#include <errno.h>
#include <limits.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#define SECTOR 512u
typedef struct {
    const DndSectorReader *reader;
    uint64_t fat_lba, heap_lba, volume_end;
    uint32_t fat_sectors, clusters, spc, root;
    int exfat;
    uint64_t cached_fat;
    _Alignas(32) unsigned char fat_cache[SECTOR];
} Volume;
typedef struct {
    DndDefragFreeSpace stats;
    uint64_t current, current_start;
} Scan;

static uint16_t u16(const unsigned char *p) {
    return (uint16_t)p[0] | ((uint16_t)p[1]<<8);
}
static uint32_t u32(const unsigned char *p) {
    return (uint32_t)u16(p) | ((uint32_t)u16(p+2)<<16);
}
static uint64_t u64(const unsigned char *p) {
    return (uint64_t)u32(p) | ((uint64_t)u32(p+4)<<32);
}
static int sector(const Volume *v,uint64_t lba,void *out) {
    if(lba<v->reader->partition_lba || lba>=v->volume_end)return -EIO;
    int rc=v->reader->read_sector(v->reader->context,lba,out);
    return rc<0?rc:rc>0?-EIO:0;
}
static int boot(Volume *v) {
    _Alignas(32) unsigned char b[SECTOR];
    int rc=v->reader->read_sector(v->reader->context,
                                  v->reader->partition_lba,b);
    if(rc)return rc<0?rc:-EIO;
    if(b[510]!=0x55 || b[511]!=0xaa)return -ENOTSUP;
    uint64_t start=v->reader->partition_lba;
    if(!memcmp(b+3,"EXFAT   ",8)) {
        if(b[108]!=9 || b[109]>15 || b[110]!=1)
            return -ENOTSUP; /* TexFAT dual bitmap is not yet supported. */
        v->exfat=1;
        v->spc=1u<<b[109];
        v->fat_sectors=u32(b+84);
        v->clusters=u32(b+92);
        v->root=u32(b+96);
        uint64_t size=u64(b+72);
        uint64_t fat_offset=u32(b+80), heap_offset=u32(b+88);
        if(!size || !v->fat_sectors || !v->clusters ||
           fat_offset>=size || heap_offset>=size ||
           (uint64_t)v->clusters*v->spc>size-heap_offset ||
           (uint64_t)(v->clusters+2u)*4>(uint64_t)v->fat_sectors*SECTOR)
            return -EIO;
        v->volume_end=start+size;
        if(v->volume_end<start || fat_offset+v->fat_sectors>heap_offset)
            return -EIO;
        v->fat_lba=start+fat_offset;
        v->heap_lba=start+heap_offset;
    } else {
        if(u16(b+11)!=512 || u16(b+17)!=0 ||
           memcmp(b+82,"FAT32   ",8))return -ENOTSUP;
        uint32_t reserved=u16(b+14), fats=b[16],fat=u32(b+36);
        uint32_t total=u32(b+32), spc=b[13], extflags=u16(b+40);
        uint32_t active=(extflags&0x80)?(extflags&15):0;
        if(!spc || (spc&(spc-1)) || spc>128 || !fats || !fat ||
           !reserved || active>=fats)return -EIO;
        uint64_t overhead=(uint64_t)reserved+(uint64_t)fats*fat;
        if(!total || overhead>=total)return -EIO;
        uint64_t clusters=(total-overhead)/spc;
        if(clusters<1 || clusters>0x0ffffff0u ||
           (clusters+2)*4>(uint64_t)fat*SECTOR)return -EIO;
        v->clusters=(uint32_t)clusters;
        v->spc=spc;
        v->root=u32(b+44);
        v->fat_sectors=fat;
        v->volume_end=start+total;
        if(v->volume_end<start)return -EIO;
        v->fat_lba=start+reserved+(uint64_t)active*fat;
        v->heap_lba=start+overhead;
    }
    if(v->root<2 || (uint64_t)v->root>=(uint64_t)v->clusters+2 ||
       v->fat_lba+v->fat_sectors>v->volume_end ||
       v->heap_lba+(uint64_t)v->clusters*v->spc>v->volume_end ||
       v->spc>UINT32_MAX/SECTOR)return -EIO;
    v->cached_fat=UINT64_MAX;
    return 0;
}
static int fat(Volume *v,uint32_t cluster,uint32_t *out) {
    if(cluster<2 || (uint64_t)cluster>=(uint64_t)v->clusters+2)return -EIO;
    uint64_t offset=(uint64_t)cluster*4;
    uint64_t lba=v->fat_lba+offset/SECTOR;
    if(offset/SECTOR>=v->fat_sectors)return -EIO;
    if(lba!=v->cached_fat) {
        int rc=sector(v,lba,v->fat_cache);
        if(rc<0)return rc;
        v->cached_fat=lba;
    }
    uint32_t value=u32(v->fat_cache+(offset%SECTOR));
    *out=v->exfat?value:(value&0x0fffffffu);
    return 0;
}
static int cluster_lba(const Volume *v,uint32_t cluster,uint64_t *out) {
    if(cluster<2 || (uint64_t)cluster>=(uint64_t)v->clusters+2)return -EIO;
    *out=v->heap_lba+(uint64_t)(cluster-2)*v->spc;
    return 0;
}
static void observe(Scan *scan,uint64_t cluster,int is_free) {
    if(is_free) {
        scan->stats.free_clusters++;
        if(!scan->current)scan->current_start=cluster;
        scan->current++;
        if(scan->current>scan->stats.largest_free_run_clusters) {
            scan->stats.largest_free_run_clusters=scan->current;
            scan->stats.largest_free_run_start_cluster=scan->current_start;
        }
    } else scan->current=0;
}
static int scan_fat32(Volume *v,Scan *out) {
    for(uint64_t cluster=2;cluster<(uint64_t)v->clusters+2;cluster++) {
        uint32_t next;
        int rc=fat(v,(uint32_t)cluster,&next);
        if(rc<0)return rc;
        observe(out,cluster,next==0);
    }
    return 0;
}
/* Locate the exFAT allocation bitmap through a directory entry in the
 * on-disk root directory, following the root's FAT chain. This is read-only. */
static int bitmap_location(Volume *v,uint32_t *first,uint64_t *bytes) {
    uint32_t cluster=v->root;
    for(uint64_t visited=0;visited<v->clusters;visited++) {
        uint64_t lba;
        int rc=cluster_lba(v,cluster,&lba);
        if(rc<0)return rc;
        for(uint32_t sec=0;sec<v->spc;sec++) {
            _Alignas(32) unsigned char b[SECTOR];
            rc=sector(v,lba+sec,b);
            if(rc<0)return rc;
            for(unsigned at=0;at<SECTOR;at+=32) {
                const unsigned char *entry=b+at;
                if(entry[0]==0)return -EIO;
                if(entry[0]==0x81) {
                    if(entry[1]!=0)return -ENOTSUP; /* TexFAT alternate bitmap. */
                    *first=u32(entry+20);
                    *bytes=u64(entry+24);
                    if(*first<2 || (uint64_t)*first>=
                       (uint64_t)v->clusters+2 ||
                       *bytes<((uint64_t)v->clusters+7)/8)
                        return -EIO;
                    return 0;
                }
            }
        }
        uint32_t next;
        rc=fat(v,cluster,&next);
        if(rc<0)return rc;
        if(next>=0xfffffff8u)return -EIO;
        cluster=next;
    }
    return -EIO; /* Loop or missing allocation bitmap. */
}
static int scan_exfat(Volume *v,Scan *out) {
    uint32_t bitmap;
    uint64_t bytes;
    int rc=bitmap_location(v,&bitmap,&bytes);
    if(rc<0)return rc;
    uint64_t done=0;
    uint32_t cluster=bitmap;
    for(uint64_t visited=0;visited<v->clusters && done<v->clusters;
        visited++) {
        uint64_t lba;
        rc=cluster_lba(v,cluster,&lba);
        if(rc<0)return rc;
        for(uint32_t sec=0;sec<v->spc && done<v->clusters;sec++) {
            _Alignas(32) unsigned char b[SECTOR];
            rc=sector(v,lba+sec,b);
            if(rc<0)return rc;
            for(unsigned i=0;i<SECTOR && done<v->clusters;i++) {
                unsigned char bits=b[i];
                for(unsigned bit=0;bit<8 && done<v->clusters;bit++,done++)
                    observe(out,done+2,(bits&(1u<<bit))==0);
            }
        }
        if(done==v->clusters)return 0;
        uint32_t next;
        rc=fat(v,cluster,&next);
        if(rc<0)return rc;
        if(next>=0xfffffff8u)return -EIO; /* Truncated bitmap chain. */
        cluster=next;
    }
    return -EIO;
}
int dnd_defrag_read_free_space(const DndSectorReader *reader,
                               DndDefragFreeSpace *result) {
    if(!reader || !reader->read_sector || !result)return -EINVAL;
    Volume v={0};
    v.reader=reader;
    int rc=boot(&v);
    if(rc<0)return rc;
    Scan out={0};
    out.stats.filesystem=v.exfat?DND_DEFRAG_EXFAT:DND_DEFRAG_FAT32;
    out.stats.bytes_per_cluster=v.spc*SECTOR;
    out.stats.total_clusters=v.clusters;
    rc=v.exfat?scan_exfat(&v,&out):scan_fat32(&v,&out);
    if(rc<0)return rc;
    *result=out.stats;
    return 0;
}
int dnd_defrag_preflight_direct(const DndDefragFreeSpace *stats,
                                uint64_t file_bytes,uint64_t *start_cluster) {
    if(!stats || !start_cluster || !stats->bytes_per_cluster ||
       !stats->total_clusters)return -EINVAL;
    if(file_bytes>INT32_MAX)return -EFBIG;
    uint64_t required=(file_bytes+stats->bytes_per_cluster-1)/
                      stats->bytes_per_cluster;
    if(required==0) {*start_cluster=0;return 1;}
    *start_cluster=0;
    if(required>stats->largest_free_run_clusters)return 0;
    *start_cluster=stats->largest_free_run_start_cluster;
    return *start_cluster>=2?1:-EINVAL;
}
