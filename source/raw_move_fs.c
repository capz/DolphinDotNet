#include "dnd_raw_move_internal.h"
#include <errno.h>
#include <limits.h>
#include <string.h>

static int bitmap_find(DndRawFs *v);
static int bitmap_contiguous(DndRawFs *v);
static int upcase_valid(DndRawFs *v);

static int checked(int rc) {return rc>0?-EIO:rc;}
static uint16_t u16(const unsigned char *p) {return p[0]|(uint16_t)p[1]<<8;}
static uint32_t u32(const unsigned char *p) {return u16(p)|(uint32_t)u16(p+2)<<16;}
static uint64_t u64(const unsigned char *p) {return u32(p)|(uint64_t)u32(p+4)<<32;}
static void p16(unsigned char *p,uint16_t x) {p[0]=(unsigned char)x;p[1]=(unsigned char)(x>>8);}
static void p32(unsigned char *p,uint32_t x) {p16(p,(uint16_t)x);p16(p+2,(uint16_t)(x>>16));}
static int read_meta(DndRawFs *v,uint64_t lba,void *out) {
    if(lba<v->reader.partition_lba || lba>=v->end)return -EIO;
    return checked(v->reader.read_sector(v->reader.context,lba,out));
}
static int cluster_ok(DndRawFs *v,uint32_t c) {return c>=2 && (uint64_t)c<v->clusters+2ULL;}
static uint64_t lba_of(DndRawFs *v,uint32_t c) {return v->heap_lba+(uint64_t)(c-2)*v->spc;}
static int fat(DndRawFs *v,uint32_t c,uint32_t *out) {
    if(c>=v->clusters+2ULL)return -EIO;
    uint64_t lba=v->fat_lba+(uint64_t)c/128;
    if(lba!=v->fat_cached) {
        int rc=read_meta(v,lba,v->fat_cache);if(rc)return rc;v->fat_cached=lba;
    }
    uint32_t x=u32(v->fat_cache+(c%128)*4);
    *out=v->exfat?x:x&0x0fffffff;return 0;
}
int dnd_raw_fs_next(DndRawFs *v,uint32_t c,uint32_t *out) {
    if(!cluster_ok(v,c))return -EIO;
    if(v->file.flags&DND_DEFRAG_NO_FAT_CHAIN) {*out=c+1;return 0;}
    int rc=fat(v,c,out);if(rc)return rc;
    return cluster_ok(v,*out)?0:-EIO;
}
int dnd_raw_fs_cluster(DndRawFs *v,uint64_t index,uint32_t *out) {
    uint64_t n=(v->file.bytes+v->spc*512-1)/(v->spc*512);
    if(index>=n)return -EINVAL;
    uint32_t c=v->file.first_cluster;
    if(v->file.flags&DND_DEFRAG_NO_FAT_CHAIN) {
        uint64_t at=c+index;if(at>=v->clusters+2ULL)return -EIO;*out=(uint32_t)at;return 0;
    }
    for(uint64_t i=0;i<index;i++) {int rc=dnd_raw_fs_next(v,c,&c);if(rc)return rc;}
    *out=c;return 0;
}
/* exFAT boot checksum excludes VolumeFlags and PercentInUse. */
static int exfat_boot_region(DndRawFs *v,uint64_t first,DndSha256 *sha) {
    uint32_t sum=0;
    for(unsigned s=0;s<12;s++) {
        _Alignas(32) unsigned char b[512];int rc=read_meta(v,first+s,b);if(rc)return rc;
        dnd_sha256_update(sha,b,512);
        if(s==11) {
            for(unsigned i=0;i<512;i+=4)if(u32(b+i)!=sum)return -EIO;
        } else for(unsigned i=0;i<512;i++) {
            if(!s && (i==106 || i==107 || i==112))continue;
            sum=((sum>>1)|(sum<<31))+b[i];
        }
    }
    return 0;
}
static int geometry(DndRawFs *v,int clean,unsigned char boot_hash[32]) {
    _Alignas(32) unsigned char b[512],other[512];
    v->end=v->request.sector_count;uint64_t start=v->reader.partition_lba;
    int rc=read_meta(v,start,b);if(rc)return rc;
    DndSha256 boot;dnd_sha256_init(&boot);
    if(!memcmp(b+3,"EXFAT   ",8)) {
        if(b[108]!=9 || b[109]>15 || b[110]!=1 || u16(b+104)!=0x100 ||
           (u16(b+106)&~0x0eu) || (clean && (u16(b+106)&0x0eu)))return -ENOTSUP;
        v->exfat=1;v->spc=1u<<b[109];v->fat_sectors=u32(b+84);v->clusters=u32(b+92);
        v->root=u32(b+96);v->fats=1;
        uint64_t size=u64(b+72),off=u32(b+80),heap=u32(b+88);
        if(!size || size>v->end-start || off<24 || !v->fat_sectors || !v->clusters ||
           v->clusters>0xfffffff5u || off+v->fat_sectors>heap || heap>=size ||
           (uint64_t)v->clusters*v->spc>size-heap ||
           ((uint64_t)v->clusters+2)*4>(uint64_t)v->fat_sectors*512)return -EIO;
        v->fat_lba=start+off;v->heap_lba=start+heap;v->end=start+size;
        rc=read_meta(v,v->fat_lba,other);if(rc)return rc;
        if(u32(other)!=0xfffffff8u || u32(other+4)!=0xffffffffu)return -EIO;
        rc=exfat_boot_region(v,start,&boot);if(rc)return rc;
        rc=exfat_boot_region(v,start+12,&boot);if(rc)return rc;
        rc=read_meta(v,start+12,other);if(rc)return rc;
        if(memcmp(b,other,512))return -EIO; /* Conservative supported variant. */
    } else {
        if(u16(b+11)!=512 || u16(b+17) || memcmp(b+82,"FAT32   ",8) ||
           b[510]!=0x55 || b[511]!=0xaa)return -ENOTSUP;
        uint32_t reserved=u16(b+14),total=u32(b+32),flags=u16(b+40);
        v->fats=b[16];v->fat_sectors=u32(b+36);v->spc=b[13];v->root=u32(b+44);
        v->mirrored=!(flags&0x80);v->active=v->mirrored?0:flags&15;
        uint64_t overhead=reserved+(uint64_t)v->fats*v->fat_sectors;
        if(!v->spc || (v->spc&(v->spc-1)) || v->spc>128 ||
           !reserved || !v->fat_sectors || !v->fats || v->fats>2 ||
           v->active>=v->fats || total>v->end-start || overhead>=total)return -EIO;
        uint64_t clusters=(total-overhead)/v->spc;
        if(!clusters || clusters>0x0fffffeeu ||
           (clusters+2)*4>(uint64_t)v->fat_sectors*512)return -EIO;
        v->clusters=(uint32_t)clusters;v->end=start+total;
        v->fat_lba=start+reserved+(uint64_t)v->active*v->fat_sectors;
        v->heap_lba=start+overhead;
        for(unsigned s=0;s<reserved;s++) {
            rc=read_meta(v,start+s,other);if(rc)return rc;dnd_sha256_update(&boot,other,512);
        }
        uint32_t backup=u16(b+50);
        if(backup && backup!=0xffff) {
            if(backup>=reserved)return -EIO;
            rc=read_meta(v,start+backup,other);if(rc)return rc;
            if(memcmp(b,other,512))return -EIO;
        }
        if(v->mirrored && v->fats==2)for(uint32_t s=0;s<v->fat_sectors;s++) {
            rc=read_meta(v,v->fat_lba+s,b);if(rc)return rc;
            rc=read_meta(v,v->fat_lba+v->fat_sectors+s,other);if(rc)return rc;
            if(memcmp(b,other,512))return -EIO;
        }
        rc=read_meta(v,v->fat_lba,b);if(rc)return rc;
        _Alignas(32) unsigned char volume_boot[512];
        rc=read_meta(v,start,volume_boot);if(rc)return rc;
        if((u32(b)&0x0fffffffu)!=(0x0fffff00u|volume_boot[21]) ||
           (u32(b+4)&0x03ffffffu)<0x03fffff8u || !(u32(b+4)&0x04000000u) ||
           (clean && !(u32(b+4)&0x08000000u)))return -EIO;
    }
    if(!cluster_ok(v,v->root))return -EIO;
    v->fat_cached=UINT64_MAX;dnd_sha256_finish(&boot,boot_hash);return 0;
}
typedef struct {DndSectorReader original;DndSha256 sha;} HashReader;
static int hash_read(void *ctx,uint64_t lba,void *out) {
    HashReader *h=ctx;int rc=checked(h->original.read_sector(h->original.context,lba,out));
    if(!rc)dnd_sha256_update(&h->sha,out,512);
    return rc;
}
static int fsinfo(DndRawFs *v,DndDefragSnapshot *m,int clean) {
    if(v->exfat)return 0;
    _Alignas(32) unsigned char b[512];int rc=read_meta(v,v->reader.partition_lba,b);if(rc)return rc;
    uint32_t info=u16(b+48),backup=u16(b+50),reserved=u16(b+14);
    if(info==0 || info==0xffff)return 0;
    if(info>=reserved)return -EIO;
    uint64_t places[2]={v->reader.partition_lba+info,0};unsigned n=1;
    if(backup && backup!=0xffff) {
        if(backup+info>=reserved)return -EIO;
        places[n++]=v->reader.partition_lba+backup+info;
    }
    for(unsigned i=0;i<n;i++) {
        rc=read_meta(v,places[i],b);if(rc)return rc;
        if(u32(b)!=0x41615252 || u32(b+484)!=0x61417272 || u32(b+508)!=0xaa550000)return -EIO;
        uint32_t free=u32(b+488),next=u32(b+492);
        if(clean && free!=UINT32_MAX && free!=m->space.free_clusters)return -EIO;
        if(next!=UINT32_MAX && !cluster_ok(v,next))return -EIO;
    }
    return 0;
}
int dnd_raw_fs_open(DndRawOps io,const DndSectorReader *reader,const DndRawMove *r,
                      DndDefragSnapshot *m,int original,int clean,DndRawFs *v,
                      unsigned char metadata_hash[32],unsigned char boot_hash[32]) {
    memset(v,0,sizeof(*v));v->io=io;v->reader=*reader;v->request=*r;
    int rc=geometry(v,clean,boot_hash);if(rc)return rc;
    HashReader h={.original=*reader};dnd_sha256_init(&h.sha);
    DndSectorReader hashing={&h,hash_read,reader->partition_lba};
    DndDefragDevice device={{r->image_id[0],r->image_id[1]},r->generation,1};
    DndDefragScanOps policy={0};rc=dnd_defrag_scan(&hashing,device,policy,m);if(rc)return rc;
    dnd_sha256_finish(&h.sha,metadata_hash);
    rc=fsinfo(v,m,clean);if(rc)return rc;
    if(v->exfat) {rc=bitmap_find(v);if(rc)return rc;rc=bitmap_contiguous(v);if(rc)return rc;rc=upcase_valid(v);if(rc)return rc;}
    uint32_t found=UINT32_MAX;
    for(size_t i=1;i<m->file_count;i++)if(m->files[i].entry_lba==r->entry_lba &&
        m->files[i].entry_offset==r->entry_offset) {found=(uint32_t)i;break;}
    if(found==UINT32_MAX)return -ESTALE;
    v->file=m->files[found];
    if(v->file.parent>=m->file_count || v->file.flags&(DND_DEFRAG_PINNED|DND_DEFRAG_DIRECTORY))return -ENOTSUP;
    v->parent=m->files[v->file.parent];
    if(v->file.bytes!=r->file_bytes || v->file.attributes!=r->attributes ||
       m->space.filesystem!=(int)r->filesystem || m->space.bytes_per_cluster!=r->bytes_per_cluster)
        return -ESTALE;
    if(original && (v->file.first_cluster!=r->original_first || v->file.flags!=r->file_flags))return -ESTALE;
    return 0;
}
int dnd_raw_fs_hash(DndRawFs *v,const DndRawMove *r,void *buffer,DndRawResult *metrics,
                      unsigned char file_hash[32],unsigned char window_hash[32]) {
    DndSha256 all,window;dnd_sha256_init(&all);dnd_sha256_init(&window);
    uint64_t remaining=v->file.bytes,n=(remaining+r->bytes_per_cluster-1)/r->bytes_per_cluster;
    uint32_t c=v->file.first_cluster;
    for(uint64_t i=0;i<n;i++) {
        if(!cluster_ok(v,c))return -EIO;
        for(uint32_t s=0;s<v->spc;s++) {
            int rc=checked(v->io.read_sector(v->io.context,lba_of(v,c)+s,buffer));if(rc)return rc;
            if(metrics)metrics->data_bytes_read+=512;
            size_t logical=remaining<512?(size_t)remaining:512;
            if(logical) {dnd_sha256_update(&all,buffer,logical);remaining-=logical;}
            if(i>=r->logical_first && i-r->logical_first<r->cluster_count)
                dnd_sha256_update(&window,buffer,512);
        }
        if(i+1<n) {int rc=dnd_raw_fs_next(v,c,&c);if(rc)return rc;}
    }
    dnd_sha256_finish(&all,file_hash);dnd_sha256_finish(&window,window_hash);return 0;
}
static int patch(DndRawFs *v,uint64_t lba,unsigned offset,const unsigned char *a,
                    const unsigned char *c,size_t bytes) {
    if(offset>512 || bytes>512-offset)return -EINVAL;
    DndRawPatch p;int rc=checked(v->io.build_get(v->io.context,lba,&p));
    if(rc==-ENOENT) {
        memset(&p,0,sizeof(p));p.lba=lba;rc=read_meta(v,lba,p.before);if(rc)return rc;
        memcpy(p.applied,p.before,512);memcpy(p.cleaned,p.before,512);
    } else if(rc)return rc;
    memcpy(p.applied+offset,a,bytes);memcpy(p.cleaned+offset,c,bytes);
    if(!memcmp(p.before,p.applied,512) && !memcmp(p.before,p.cleaned,512))return 0;
    return checked(v->io.build_put(v->io.context,&p));
}
static int fat_edit(DndRawFs *v,uint32_t cluster,int apply,uint32_t new_value,int clear) {
    uint32_t copies=v->mirrored?v->fats:1;
    for(uint32_t i=0;i<copies;i++) {
        uint64_t lba=v->fat_lba+(uint64_t)i*v->fat_sectors+cluster/128;
        DndRawPatch p;int rc=checked(v->io.build_get(v->io.context,lba,&p));
        if(rc==-ENOENT) {
            memset(&p,0,sizeof(p));p.lba=lba;rc=read_meta(v,lba,p.before);if(rc)return rc;
            memcpy(p.applied,p.before,512);memcpy(p.cleaned,p.before,512);
        } else if(rc)return rc;
        unsigned at=(cluster%128)*4;
        uint32_t old=u32(p.before+at),value=v->exfat?new_value:(old&0xf0000000)|(new_value&0x0fffffff);
        if(apply) {p32(p.applied+at,value);p32(p.cleaned+at,value);}
        if(clear)p32(p.cleaned+at,v->exfat?0:old&0xf0000000);
        if(memcmp(p.before,p.applied,512) || memcmp(p.before,p.cleaned,512)) {
            rc=checked(v->io.build_put(v->io.context,&p));if(rc)return rc;
        }
    }
    return 0;
}
static int bitmap_find(DndRawFs *v) {
    uint32_t c=v->root;
    for(uint64_t visits=0;visits<v->clusters;visits++) {
        for(uint32_t s=0;s<v->spc;s++) {
            _Alignas(32) unsigned char b[512];int rc=read_meta(v,lba_of(v,c)+s,b);if(rc)return rc;
            for(unsigned at=0;at<512;at+=32) {
                if(!b[at])return -EIO;
                if(b[at]==0x81) {
                    if(b[at+1])return -ENOTSUP;
                    v->bitmap_first=u32(b+at+20);v->bitmap_bytes=u64(b+at+24);
                    return cluster_ok(v,v->bitmap_first) && v->bitmap_bytes>=(v->clusters+7ULL)/8?0:-EIO;
                }
            }
        }
        int rc=fat(v,c,&c);if(rc || !cluster_ok(v,c))return rc?rc:-EIO;
    }
    return -EIO;
}
static int upcase_valid(DndRawFs *v) {
    uint32_t root=v->root,first=0,expected=0;uint64_t bytes=0;
    for(uint64_t visits=0;visits<v->clusters && !first;visits++) {
        int ended=0;
        for(uint32_t sec=0;sec<v->spc && !first && !ended;sec++) {
            _Alignas(32) unsigned char b[512];int rc=read_meta(v,lba_of(v,root)+sec,b);if(rc)return rc;
            for(unsigned at=0;at<512;at+=32) {
                if(!b[at]) {ended=1;break;}
                if(b[at]==0x82) {expected=u32(b+at+4);first=u32(b+at+20);bytes=u64(b+at+24);break;}
            }
        }
        if(first || ended)break;
        int rc=fat(v,root,&root);if(rc || !cluster_ok(v,root))return rc?rc:-EIO;
    }
    if(!cluster_ok(v,first) || !bytes || bytes>131072)return -EIO;
    uint32_t sum=0,c=first;uint64_t remaining=bytes;
    while(remaining) {
        for(uint32_t sec=0;sec<v->spc && remaining;sec++) {
            _Alignas(32) unsigned char b[512];int rc=read_meta(v,lba_of(v,c)+sec,b);if(rc)return rc;
            unsigned n=remaining<512?(unsigned)remaining:512;
            for(unsigned i=0;i<n;i++)sum=((sum>>1)|(sum<<31))+b[i];
            remaining-=n;
        }
        if(remaining) {int rc=fat(v,c,&c);if(rc || !cluster_ok(v,c))return rc?rc:-EIO;}
    }
    return sum==expected?0:-EIO;
}
static int bitmap_contiguous(DndRawFs *v) {
    uint64_t n=(v->bitmap_bytes+v->spc*512-1)/(v->spc*512);
    if(!n || n>v->clusters)return -EIO;
    uint32_t c=v->bitmap_first;
    for(uint64_t i=1;i<n;i++) {
        uint32_t next;int rc=fat(v,c,&next);if(rc)return rc;
        if(next!=c+1)return -ENOTSUP;
        c=next;
    }
    uint32_t end;int rc=fat(v,c,&end);if(rc)return rc;
    return end>=0xfffffff8u?0:-EIO;
}
static int bitmap_edit(DndRawFs *v,uint32_t cluster,int destination) {
    uint64_t byte=(cluster-2ULL)/8,skip=byte/(v->spc*512);
    uint32_t c=v->bitmap_first;
    for(uint64_t i=0;i<skip;i++) {int rc=fat(v,c,&c);if(rc || !cluster_ok(v,c))return rc?rc:-EIO;}
    uint64_t lba=lba_of(v,c)+(byte%(v->spc*512))/512;
    DndRawPatch p;int rc=checked(v->io.build_get(v->io.context,lba,&p));
    if(rc==-ENOENT) {
        memset(&p,0,sizeof(p));p.lba=lba;rc=read_meta(v,lba,p.before);if(rc)return rc;
        memcpy(p.applied,p.before,512);memcpy(p.cleaned,p.before,512);
    } else if(rc)return rc;
    unsigned at=(unsigned)(byte%512),mask=1u<<((cluster-2)%8);
    if(destination) {p.applied[at]|=(unsigned char)mask;p.cleaned[at]|=(unsigned char)mask;}
    else p.cleaned[at]&=(unsigned char)~mask;
    return checked(v->io.build_put(v->io.context,&p));
}
static int entry_next(DndRawFs *v,uint64_t *lba,unsigned *offset) {
    if((*offset+=32)<512)return 0;
    *offset=0;
    if(*lba<v->heap_lba)return -EIO;
    uint64_t relative=*lba-v->heap_lba;
    if(relative%v->spc+1<v->spc) {(*lba)++;return 0;}
    uint32_t c=(uint32_t)(relative/v->spc+2);
    if(v->parent.flags&DND_DEFRAG_NO_FAT_CHAIN)c++;
    else {int rc=fat(v,c,&c);if(rc)return rc;}
    if(!cluster_ok(v,c))return -EIO;
    *lba=lba_of(v,c);return 0;
}
static int directory_edit(DndRawFs *v,uint32_t first,int contiguous) {
    uint64_t lba=v->request.entry_lba;unsigned at=v->request.entry_offset;
    _Alignas(32) unsigned char b[512];int rc=read_meta(v,lba,b);if(rc)return rc;
    if(!v->exfat) {
        unsigned char hi[2],lo[2];p16(hi,(uint16_t)(first>>16));p16(lo,(uint16_t)first);
        rc=patch(v,lba,at+20,hi,hi,2);if(rc)return rc;
        return patch(v,lba,at+26,lo,lo,2);
    }
    unsigned entries=(unsigned)b[at+1]+1;
    if(b[at]!=0x85 || entries<3 || entries>19)return -EIO;
    unsigned char set[19][32];uint64_t places[19];unsigned offsets[19];
    for(unsigned i=0;i<entries;i++) {
        rc=read_meta(v,lba,b);if(rc)return rc;
        places[i]=lba;offsets[i]=at;memcpy(set[i],b+at,32);
        if(i+1<entries) {rc=entry_next(v,&lba,&at);if(rc)return rc;}
    }
    if(set[1][0]!=0xc0)return -ENOTSUP;
    set[1][1]=(unsigned char)((set[1][1]&~2u)|(contiguous?2u:0u));p32(set[1]+20,first);
    uint16_t checksum=0;
    for(unsigned i=0;i<entries;i++)for(unsigned k=0;k<32;k++) {
        if(!i && (k==2 || k==3))continue;
        checksum=(uint16_t)(((checksum&1)?0x8000:0)+(checksum>>1)+set[i][k]);
    }
    p16(set[0]+2,checksum);
    for(unsigned i=0;i<entries;i++) {
        rc=patch(v,places[i],offsets[i],set[i],set[i],32);if(rc)return rc;
    }
    return 0;
}
static int dirty_edit(DndRawFs *v) {
    if(v->exfat)for(unsigned copy=0;copy<2;copy++) {
        uint64_t lba=v->reader.partition_lba+copy*12;
        _Alignas(32) unsigned char b[512];int rc=read_meta(v,lba,b);if(rc)return rc;
        unsigned char dirty[2];p16(dirty,(uint16_t)(u16(b+106)|2));
        rc=patch(v,lba,106,dirty,b+106,2);if(rc)return rc;
    } else {
        uint32_t n=v->mirrored?v->fats:1;
        for(uint32_t i=0;i<n;i++) {
            uint64_t lba=v->fat_lba+(uint64_t)i*v->fat_sectors;
            _Alignas(32) unsigned char b[512];int rc=read_meta(v,lba,b);if(rc)return rc;
            unsigned char dirty[4];p32(dirty,u32(b+4)&~0x08000000u);
            rc=patch(v,lba,4,dirty,b+4,4);if(rc)return rc;
        }
    }
    return 0;
}
int dnd_raw_fs_build(DndRawFs *v,const DndRawMove *r) {
    int rc=checked(v->io.build_reset(v->io.context));if(rc)return rc;
    rc=dirty_edit(v);if(rc)return rc;
    uint64_t n=(r->file_bytes+r->bytes_per_cluster-1)/r->bytes_per_cluster;
    uint32_t c=v->file.first_cluster,prev=0,first=0;int contiguous=1;
    for(uint64_t i=0;i<n;i++) {
        uint32_t mapped=i>=r->logical_first && i-r->logical_first<r->cluster_count?
            r->destination+(uint32_t)(i-r->logical_first):c;
        if(!i)first=mapped;else if(mapped!=prev+1)contiguous=0;
        prev=mapped;if(i+1<n) {rc=dnd_raw_fs_next(v,c,&c);if(rc)return rc;}
    }
    if(v->exfat) {rc=bitmap_find(v);if(rc)return rc;}
    c=v->file.first_cluster;
    for(uint64_t i=0;i<n;i++) {
        uint32_t next=0;
        if(i+1<n) {rc=dnd_raw_fs_next(v,c,&next);if(rc)return rc;}
        uint32_t mapped=i>=r->logical_first && i-r->logical_first<r->cluster_count?
            r->destination+(uint32_t)(i-r->logical_first):c;
        if(!v->exfat || !contiguous) {
            uint32_t mapped_next=i+1>=r->logical_first && i+1-r->logical_first<r->cluster_count?
                r->destination+(uint32_t)(i+1-r->logical_first):next;
            uint32_t value=i+1==n?(v->exfat?0xffffffffu:0x0fffffffu):mapped_next;
            rc=fat_edit(v,mapped,1,value,0);if(rc)return rc;
        }
        if(i>=r->logical_first && i-r->logical_first<r->cluster_count) {
            if(v->exfat) {
                rc=bitmap_edit(v,mapped,1);if(rc)return rc;
                rc=bitmap_edit(v,c,0);if(rc)return rc;
            }
            rc=fat_edit(v,c,0,0,1);if(rc)return rc;
        }
        c=next;
    }
    return directory_edit(v,first,contiguous);
}
int dnd_raw_fs_copy(DndRawFs *v,const DndRawMove *r,void *buffer,DndRawResult *metrics) {
    uint32_t c;int rc=dnd_raw_fs_cluster(v,r->logical_first,&c);if(rc)return rc;
    DndSha256 sha;dnd_sha256_init(&sha);
    for(uint64_t i=0;i<r->cluster_count;i++) {
        for(uint32_t s=0;s<v->spc;s++) {
            rc=checked(v->io.read_sector(v->io.context,lba_of(v,c)+s,buffer));if(rc)return rc;
            metrics->data_bytes_read+=512;dnd_sha256_update(&sha,buffer,512);
            uint64_t dest=lba_of(v,r->destination+(uint32_t)i)+s;
            rc=checked(v->io.write_sector(v->io.context,dest,buffer));if(rc)return rc;
            metrics->data_bytes_written+=512;
        }
        if(i+1<r->cluster_count) {rc=dnd_raw_fs_next(v,c,&c);if(rc)return rc;}
    }
    unsigned char hash[32];dnd_sha256_finish(&sha,hash);
    return memcmp(hash,r->window_sha256,32)?-ESTALE:0;
}
int dnd_raw_fs_destination(DndRawFs *v,const DndRawMove *r,void *buffer,DndRawResult *metrics) {
    DndSha256 sha;dnd_sha256_init(&sha);
    for(uint64_t i=0;i<r->cluster_count;i++)for(uint32_t s=0;s<v->spc;s++) {
        int rc=checked(v->io.read_sector(v->io.context,lba_of(v,r->destination+(uint32_t)i)+s,buffer));
        if(rc)return rc;
        metrics->data_bytes_read+=512;dnd_sha256_update(&sha,buffer,512);
    }
    unsigned char hash[32];dnd_sha256_finish(&sha,hash);
    return memcmp(hash,r->window_sha256,32)?-EIO:0;
}
