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
    unsigned bitmap_seen,upcase_seen;
    uint64_t fingerprint, serial;
    uint64_t cached_fat;
    _Alignas(32) unsigned char fat_cache[SECTOR];
} Volume;
typedef struct {
    DndDefragFreeSpace stats;
    uint64_t current, current_start;
    DndDefragSnapshot *map;
    int error;
} Scan;

static uint64_t hash_bytes(uint64_t hash,const void *data,size_t size) {
    const unsigned char *p=data;
    while(size--)hash=(hash^*p++)*UINT64_C(1099511628211);
    return hash;
}
static int add_run(DndDefragSnapshot *map,uint64_t start,uint64_t count,
                    uint32_t owner) {
    if(!count)return 0;
    for(size_t i=0;i<map->run_count;i++) {
        const DndDefragRun *r=&map->runs[i];
        if(start<r->start+r->count && r->start<start+count)return -EIO;
    }
    if(map->run_count>=map->run_capacity)return -ENOSPC;
    map->runs[map->run_count++]=(DndDefragRun){start,count,owner};
    return 0;
}

static uint16_t u16(const unsigned char *p) {
    return (uint16_t)p[0] | ((uint16_t)p[1]<<8);
}
static uint32_t u32(const unsigned char *p) {
    return (uint32_t)u16(p) | ((uint32_t)u16(p+2)<<16);
}
static uint64_t u64(const unsigned char *p) {
    return (uint64_t)u32(p) | ((uint64_t)u32(p+4)<<32);
}
static int sector(Volume *v,uint64_t lba,void *out) {
    if(lba<v->reader->partition_lba || lba>=v->volume_end)return -EIO;
    int rc=v->reader->read_sector(v->reader->context,lba,out);
    if(!rc)v->fingerprint=hash_bytes(v->fingerprint,out,SECTOR);
    return rc<0?rc:rc>0?-EIO:0;
}
static int boot(Volume *v) {
    _Alignas(32) unsigned char b[SECTOR];
    int rc=v->reader->read_sector(v->reader->context,
                                  v->reader->partition_lba,b);
    if(rc)return rc<0?rc:-EIO;
    v->fingerprint=hash_bytes(UINT64_C(14695981039346656037),b,SECTOR);
    if(b[510]!=0x55 || b[511]!=0xaa)return -ENOTSUP;
    uint64_t start=v->reader->partition_lba;
    if(!memcmp(b+3,"EXFAT   ",8)) {
        if(b[108]!=9 || b[109]>15 || b[110]!=1 || (u16(b+106)&1))
            return -ENOTSUP; /* TexFAT dual bitmap is not yet supported. */
        v->exfat=1;
        v->serial=u32(b+100);
        v->spc=1u<<b[109];
        v->fat_sectors=u32(b+84);
        v->clusters=u32(b+92);
        v->root=u32(b+96);
        uint64_t size=u64(b+72);
        uint64_t fat_offset=u32(b+80), heap_offset=u32(b+88);
        if(!size || !v->fat_sectors || !v->clusters ||
           v->clusters>0xfffffff5u || /* exFAT reserved cluster indices */
           fat_offset>=size || heap_offset>=size ||
           (uint64_t)v->clusters*v->spc>size-heap_offset ||
           ((uint64_t)v->clusters+2u)*4>(uint64_t)v->fat_sectors*SECTOR)
            return -EIO;
        v->volume_end=start+size;
        if(v->volume_end<start || fat_offset+v->fat_sectors>heap_offset)
            return -EIO;
        v->fat_lba=start+fat_offset;
        v->heap_lba=start+heap_offset;
    } else {
        v->serial=u32(b+67);
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
    } else {
        if(scan->map && scan->current && !scan->error)
            scan->error=add_run(scan->map,scan->current_start,scan->current,
                                 DND_DEFRAG_FREE);
        scan->current=0;
    }
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

/* The ownership scan deliberately reuses the validated allocation geometry.
 * Unknown allocations remain pinned, including lost chains and unsupported
 * exFAT system streams. A complete, disjoint cluster partition is published
 * only after directory/chain/free-map consistency has been checked. */
static int eoc(const Volume *v,uint32_t c) {
    return c>=(v->exfat?0xfffffff8u:0x0ffffff8u);
}
static int chain(Volume *v,DndDefragSnapshot *m,uint32_t first,
                  uint64_t bytes,uint32_t flags,uint32_t owner) {
    uint64_t needed=bytes/(v->spc*SECTOR)+(bytes%(v->spc*SECTOR)!=0);
    int directory=(flags&DND_DEFRAG_DIRECTORY)!=0;
    int known_length=!directory || (v->exfat && owner!=0);
    if(!directory && !needed)return first?-EIO:0;
    if(needed>v->clusters)return -EIO;
    uint32_t c=first;
    uint64_t start=c,count=0;
    for(uint64_t n=0;n<v->clusters;n++) {
        if(c<2 || (uint64_t)c>=(uint64_t)v->clusters+2)return -EIO;
        count++;
        uint32_t next;
        int rc=0;
        if(flags&DND_DEFRAG_NO_FAT_CHAIN)
            next=n+1==needed?UINT32_MAX:c+1;
        else rc=fat(v,c,&next);
        if(rc<0)return rc;
        if(eoc(v,next)) {
            if(known_length && n+1!=needed)return -EIO;
            return add_run(m,start,count,owner);
        }
        if(known_length && n+1>=needed)return -EIO;
        if(next!=c+1) {
            rc=add_run(m,start,count,owner);
            if(rc<0)return rc;
            start=next;count=0;
        }
        c=next;
    }
    return -EIO;
}
static unsigned short_checksum(const unsigned char *e) {
    unsigned sum=0;
    for(unsigned i=0;i<11;i++)sum=(((sum&1)?128:0)+(sum>>1)+e[i])&255;
    return sum;
}
typedef struct {
    DndDefragFile file;
    unsigned lfn_sequence, lfn_sum, lfn_length;
    unsigned remaining, name_length, written, set_sum, expected_sum;
    int active, stream;
} EntryState;
static int name_safe(const uint16_t *name) {
    if(!name[0])return 0;
    for(unsigned i=0;i<256;i++) {
        uint16_t ch=name[i];
        if(!ch)return 1;
        if(ch<32 || ch=='/' || ch=='\\' || ch==':' || ch==0xffff)return 0;
        if(ch>=0xd800 && ch<=0xdbff) {
            if(i>=254 || name[i+1]<0xdc00 || name[i+1]>0xdfff)return 0;
            i++;
        } else if(ch>=0xdc00 && ch<=0xdfff)return 0;
    }
    return 0;
}
static void defaults(DndDefragFile *f) {
    if(f->attributes&0x10)f->flags|=DND_DEFRAG_DIRECTORY|DND_DEFRAG_PINNED;
    if(f->attributes&7 || f->bytes>INT32_MAX || !name_safe(f->name))
        f->flags|=DND_DEFRAG_PINNED;
    /* Reserved staging names and memory-card containers are never blockers.
     * Mounted handle policy can pin other application-specific names. */
    if((f->name[0]=='.' && f->name[1]=='_' && f->name[2]=='_') ||
       (f->name[0]=='.' && !f->name[1]) ||
       (f->name[0]=='.' && f->name[1]=='.' && !f->name[2]))
        f->flags|=DND_DEFRAG_PINNED;
    size_t n=0;while(n<256 && f->name[n])n++;
    if(n>=4 && f->name[n-4]=='.' &&
       (f->name[n-3]=='g'||f->name[n-3]=='G') &&
       (f->name[n-2]=='c'||f->name[n-2]=='C') &&
       (f->name[n-1]=='i'||f->name[n-1]=='I'))f->flags|=DND_DEFRAG_PINNED;
}
/* 1: complete file, 0: secondary/deleted entry, negative: corrupt/unsupported. */
static int fat_entry(EntryState *s,const unsigned char *e,DndDefragFile *f) {
    if(e[0]==0xe5) {memset(s,0,sizeof(*s));return 0;}
    if(e[11]==15) {
        unsigned seq=e[0]&31;
        if(e[0]&0x40) {
            memset(s,0,sizeof(*s));s->lfn_sequence=seq;s->lfn_sum=e[13];
        }
        if(!seq || seq>20 || seq!=s->lfn_sequence ||
           e[13]!=s->lfn_sum || e[12] || u16(e+26))return -EIO;
        const unsigned positions[13]={1,3,5,7,9,14,16,18,20,22,24,28,30};
        for(unsigned i=0;i<13;i++) {
            unsigned at=(seq-1)*13+i;
            uint16_t ch=u16(e+positions[i]);
            if(at<255)s->file.name[at]=ch==0xffff?0:ch;
            else if(ch && ch!=0xffff)return -EIO;
        }
        s->lfn_sequence--;s->active=1;
        return 0;
    }
    if(e[11]&8) {memset(s,0,sizeof(*s));return 0;}
    memset(f,0,sizeof(*f));
    if(s->active) {
        if(s->lfn_sequence || short_checksum(e)!=s->lfn_sum)return -EIO;
        memcpy(f->name,s->file.name,sizeof(f->name));
    } else {
        unsigned n=0;
        for(unsigned i=0;i<11;i++) {
            if(i==8 && e[i]!=' ')f->name[n++]='.';
            if(e[i]==' ')continue;
            if(e[i]<32 || e[i]>126)f->flags|=DND_DEFRAG_PINNED;
            unsigned char ch=e[i];
            if(ch>='A' && ch<='Z' && (e[12]&(i<8?8:16)))ch=(unsigned char)(ch+32);
            f->name[n++]=ch;
        }
    }
    f->attributes=e[11];
    f->first_cluster=((uint32_t)u16(e+20)<<16)|u16(e+26);
    f->bytes=u32(e+28);
    memset(s,0,sizeof(*s));
    if(f->name[0]=='.' && (!f->name[1] ||
       (f->name[1]=='.' && !f->name[2]))) {
        if(!(f->attributes&16))return -EIO;
        return 0; /* Parent aliases are not independent allocations. */
    }
    return 1;
}
static unsigned set_checksum(unsigned sum,const unsigned char *e,int primary) {
    for(unsigned i=0;i<32;i++) {
        if(primary && (i==2 || i==3))continue;
        sum=(((sum&1)?0x8000:0)+(sum>>1)+e[i])&0xffff;
    }
    return sum;
}
static int ex_entry(EntryState *s,const unsigned char *e,DndDefragFile *f,
                      uint64_t lba,unsigned offset) {
    if(e[0]==0x85) {
        if(s->remaining)return -EIO;
        memset(s,0,sizeof(*s));
        if(e[1]<2 || e[1]>18)return -EIO;
        s->remaining=e[1];s->expected_sum=u16(e+2);
        s->set_sum=set_checksum(0,e,1);
        s->file.attributes=u16(e+4);
        s->file.entry_lba=lba;s->file.entry_offset=(uint16_t)offset;
        return 0;
    }
    if(!s->remaining) {
        /* Unsupported critical directory entries cannot safely hide owners. */
        if((e[0]&0x80) &&
           e[0]!=0x81 && e[0]!=0x82 && e[0]!=0x83)return -ENOTSUP;
        return 0;
    }
    s->set_sum=set_checksum(s->set_sum,e,0);
    if(e[0]==0xc0 && !s->stream && !s->written) {
        if(!(e[1]&1) || (e[1]&~3) || !e[3])return -EIO;
        s->stream=1;s->name_length=e[3];
        s->file.first_cluster=u32(e+20);s->file.bytes=u64(e+24);
        if(u64(e+8)>s->file.bytes)return -EIO;
        if(e[1]&2)s->file.flags|=DND_DEFRAG_NO_FAT_CHAIN;
    } else if(e[0]==0xc1 && s->stream) {
        if(s->written>=s->name_length)return -EIO;
        for(unsigned i=0;i<15 && s->written<s->name_length;i++) {
            uint16_t ch=u16(e+2+i*2);
            if(!ch)return -EIO;
            s->file.name[s->written++]=ch;
        }
    } else return -ENOTSUP;
    if(--s->remaining)return 0;
    if(!s->stream || s->written!=s->name_length ||
       s->set_sum!=s->expected_sum)return -EIO;
    *f=s->file;
    memset(s,0,sizeof(*s));
    return 1;
}
static int directory(Volume *v,DndDefragSnapshot *m,uint32_t owner,
                      DndDefragScanOps ops) {
    DndDefragFile dir=m->files[owner];
    uint32_t c=dir.first_cluster;
    uint64_t done=0;
    EntryState state={0};
    for(uint64_t n=0;n<v->clusters;n++) {
        uint64_t lba;
        int rc=cluster_lba(v,c,&lba);
        if(rc<0)return rc;
        for(uint32_t sec=0;sec<v->spc;sec++) {
            _Alignas(32) unsigned char b[SECTOR];
            rc=sector(v,lba+sec,b);
            if(rc<0)return rc;
            for(unsigned at=0;at<SECTOR;at+=32,done+=32) {
                if((dir.flags&DND_DEFRAG_NO_FAT_CHAIN) && done>=dir.bytes)
                    return state.remaining?-EIO:0;
                const unsigned char *e=b+at;
                if(!e[0])return state.remaining||state.active?-EIO:0;
                if(v->exfat && (e[0]==0x81 || e[0]==0x82)) {
                    if(owner!=0 || state.remaining)return -EIO;
                    if(e[0]==0x81) {
                        if(v->bitmap_seen++ || e[1])return -ENOTSUP;
                    } else {
                        if(v->upcase_seen++)return -EIO;
                        if(!u64(e+24) || u64(e+24)>131072)return -EIO;
                    }
                    rc=chain(v,m,u32(e+20),u64(e+24),0,DND_DEFRAG_SYSTEM);
                    if(rc<0)return rc;
                    continue;
                }
                DndDefragFile f;
                rc=v->exfat?ex_entry(&state,e,&f,lba+sec,at):
                            fat_entry(&state,e,&f);
                if(rc<0)return rc;
                if(!rc)continue;
                if(!v->exfat) {f.entry_lba=lba+sec;f.entry_offset=(uint16_t)at;}
                f.parent=owner;
                defaults(&f);
                if(ops.pin_file) {
                    rc=ops.pin_file(ops.context,&f);
                    if(rc<0)return rc;
                    if(rc)f.flags|=DND_DEFRAG_PINNED;
                }
                if(m->file_count>=m->file_capacity ||
                   m->file_count>=DND_DEFRAG_SYSTEM)return -ENOSPC;
                uint32_t id=(uint32_t)m->file_count;
                rc=chain(v,m,f.first_cluster,f.bytes,f.flags,id);
                if(rc<0)return rc;
                m->files[m->file_count++]=f;
            }
        }
        if(dir.flags&DND_DEFRAG_NO_FAT_CHAIN) {
            if(done>=dir.bytes)return state.remaining?-EIO:0;
            c++;
        } else {
            uint32_t next;
            rc=fat(v,c,&next);
            if(rc<0)return rc;
            if(eoc(v,next))return state.remaining||state.active?-EIO:0;
            c=next;
        }
    }
    return -EIO;
}
static int partition_runs(DndDefragSnapshot *m) {
    /* Insertion sort has fixed stack usage. Huge/fragmented maps fail the
     * caller's explicit cap rather than allocating a cluster bitmap. */
    for(size_t i=1;i<m->run_count;i++) {
        DndDefragRun r=m->runs[i];size_t j=i;
        while(j && m->runs[j-1].start>r.start) {
            m->runs[j]=m->runs[j-1];j--;
        }
        m->runs[j]=r;
    }
    uint64_t end=2;
    for(size_t i=0;i<m->run_count;i++) {
        DndDefragRun r=m->runs[i];
        if(r.start<end)return -EIO; /* Cross-links, loops, allocated-as-free. */
        if(r.start>end) {
            if(m->run_count>=m->run_capacity)return -ENOSPC;
            memmove(m->runs+i+1,m->runs+i,
                      (m->run_count-i)*sizeof(*m->runs));
            m->runs[i]=(DndDefragRun){end,r.start-end,DND_DEFRAG_UNKNOWN};
            m->run_count++;i++;
        }
        end=r.start+r.count;
    }
    return end<m->space.total_clusters+2?
        add_run(m,end,m->space.total_clusters+2-end,DND_DEFRAG_UNKNOWN):0;
}
int dnd_defrag_scan(const DndSectorReader *reader,DndDefragDevice device,
                     DndDefragScanOps ops,DndDefragSnapshot *m) {
    if(!m)return -EINVAL;
    m->valid=0;m->file_count=0;m->run_count=0;
    if(!reader || !reader->read_sector || !m->files || !m->runs ||
       !m->file_capacity || !m->run_capacity ||
       !(device.physical_id[0]|device.physical_id[1]))return -EINVAL;
    Volume v={0};v.reader=reader;
    int rc=boot(&v);
    if(rc<0)return rc;
    Scan scan={0};scan.map=m;
    scan.stats.filesystem=v.exfat?DND_DEFRAG_EXFAT:DND_DEFRAG_FAT32;
    scan.stats.bytes_per_cluster=v.spc*SECTOR;
    scan.stats.total_clusters=v.clusters;
    rc=v.exfat?scan_exfat(&v,&scan):scan_fat32(&v,&scan);
    if(rc<0)return rc;
    observe(&scan,0,0); /* Flush the final free interval. */
    if(scan.error)return scan.error;
    m->space=scan.stats;
    DndDefragFile root={0};
    root.first_cluster=v.root;root.parent=DND_DEFRAG_SYSTEM;
    root.flags=DND_DEFRAG_DIRECTORY|DND_DEFRAG_PINNED;
    m->files[m->file_count++]=root;
    /* Root has an unknown logical length on both formats. */
    rc=chain(&v,m,v.root,0,DND_DEFRAG_DIRECTORY,0);
    if(rc<0)return rc;
    for(size_t i=0;i<m->file_count;i++) {
        if(!(m->files[i].flags&DND_DEFRAG_DIRECTORY))continue;
        rc=directory(&v,m,(uint32_t)i,ops);
        if(rc<0)return rc;
    }
    if(v.exfat && (v.bitmap_seen!=1 || v.upcase_seen!=1))return -EIO;
    rc=partition_runs(m);
    if(rc<0)return rc;
    m->device=device;m->partition_lba=reader->partition_lba;
    m->volume_serial=v.serial;
    uint64_t hash=v.fingerprint;
    for(size_t i=0;i<m->file_count;i++)
        hash=hash_bytes(hash,&m->files[i].flags,sizeof(m->files[i].flags));
    m->fingerprint=hash;m->valid=1;
    return 0;
}
int dnd_defrag_snapshot_matches(const DndDefragSnapshot *a,
                                 const DndDefragSnapshot *b) {
    if(!a || !b || !a->valid || !b->valid)return -EINVAL;
    if(a->device.physical_id[0]!=b->device.physical_id[0] ||
       a->device.physical_id[1]!=b->device.physical_id[1] ||
       a->device.generation!=b->device.generation ||
       a->device.writable!=b->device.writable ||
       a->partition_lba!=b->partition_lba || a->volume_serial!=b->volume_serial ||
       a->fingerprint!=b->fingerprint)return -ESTALE;
    return 0;
}
