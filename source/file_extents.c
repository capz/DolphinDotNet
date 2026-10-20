#include "dnd_file_extents.h"
#include <errno.h>
#include <limits.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#define DND_SECTOR 512u
#define DND_NAME 256
typedef struct {
    const DndSectorReader *reader;
    bool exfat;
    uint64_t fat_lba, data_lba, cache_lba;
    uint32_t clusters, root, spc;
    _Alignas(32) unsigned char fat_cache[DND_SECTOR];
} Volume;
typedef struct {
    uint32_t first;
    uint64_t length;
    bool directory, contiguous;
} FileInfo;
typedef struct {
    char name[DND_NAME];
    uint16_t unicode[DND_NAME];
    unsigned sequence, checksum;
    bool lfn_good;
    unsigned remaining, name_length, name_written;
    bool ex_valid, ex_stream;
    FileInfo entry;
} DirectoryState;
static uint16_t le16(const unsigned char *p) {
    return (uint16_t)(p[0] | (uint16_t)p[1] << 8);
}
static uint32_t le32(const unsigned char *p) {
    return (uint32_t)le16(p) | (uint32_t)le16(p+2) << 16;
}
static uint64_t le64(const unsigned char *p) {
    return (uint64_t)le32(p) | (uint64_t)le32(p+4) << 32;
}
static int read_sector(const Volume *v, uint64_t lba, void *buffer) {
    return v->reader->read_sector(v->reader->context,lba,buffer);
}
static int boot(Volume *v) {
    _Alignas(32) unsigned char sector[DND_SECTOR];
    int rc = read_sector(v,v->reader->partition_lba,sector);
    if (rc < 0) return rc;
    if (sector[510] != 0x55 || sector[511] != 0xaa) return -ENOTSUP;
    if (!memcmp(sector+3,"EXFAT   ",8)) {
        unsigned ss=sector[108], cs=sector[109];
        if (ss != 9 || cs > 15 || sector[110] == 0) return -ENOTSUP;
        v->exfat=true;
        v->spc=1u<<cs;
        v->fat_lba=v->reader->partition_lba+le32(sector+80);
        v->data_lba=v->reader->partition_lba+le32(sector+88);
        v->clusters=le32(sector+92);
        v->root=le32(sector+96);
    } else {
        if (le16(sector+11)!=512 || le16(sector+17)!=0 ||
            memcmp(sector+82,"FAT32   ",8)) return -ENOTSUP;
        unsigned spc=sector[13], fats=sector[16];
        uint32_t reserved=le16(sector+14), fat=le32(sector+36);
        uint32_t sectors=le32(sector+32);
        if (!spc || (spc&(spc-1)) || !fats || !reserved || !fat)
            return -EIO;
        uint64_t overhead=(uint64_t)reserved + (uint64_t)fats*fat;
        if (overhead >= sectors) return -EIO;
        v->spc=spc;
        v->fat_lba=v->reader->partition_lba+reserved;
        v->data_lba=v->reader->partition_lba+overhead;
        v->clusters=(uint32_t)((sectors-overhead)/spc);
        v->root=le32(sector+44);
    }
    if (!v->spc || v->clusters < 1 || v->root < 2 ||
        v->root >= v->clusters+2u || v->clusters > 0x0ffffff0u)
        return -EIO;
    v->cache_lba=UINT64_MAX;
    return 0;
}
static int next_cluster(Volume *v, uint32_t current, uint32_t *next) {
    if (current<2 || (uint64_t)current>=(uint64_t)v->clusters+2) return -EIO;
    uint64_t off=(uint64_t)current*4, lba=v->fat_lba+off/DND_SECTOR;
    if (lba != v->cache_lba) {
        int rc=read_sector(v,lba,v->fat_cache);
        if (rc<0) return rc;
        v->cache_lba=lba;
    }
    uint32_t value=le32(v->fat_cache+(off%DND_SECTOR));
    if (!v->exfat) value &= 0x0fffffff;
    *next=value;
    return 0;
}
static bool is_eoc(const Volume *v,uint32_t cluster) {
    return cluster >= (v->exfat ? 0xfffffff8u : 0x0ffffff8u);
}
static int cluster_lba(const Volume *v,uint32_t cluster,uint64_t *lba) {
    if (cluster<2 || (uint64_t)cluster>=(uint64_t)v->clusters+2) return -EIO;
    *lba=v->data_lba+(uint64_t)(cluster-2)*v->spc;
    return 0;
}
static int same_ascii(const char *a,const char *b) {
    while (*a && *b) {
        unsigned char x=(unsigned char)*a++, y=(unsigned char)*b++;
        if (x>='A' && x<='Z') x=(unsigned char)(x+32);
        if (y>='A' && y<='Z') y=(unsigned char)(y+32);
        if (x!=y) return 0;
    }
    return !*a && !*b;
}
static int lfn_checksum(const unsigned char *s) {
    unsigned v=0;
    for (unsigned i=0;i<11;i++) v=((v&1u)?0x80u:0u)+(v>>1)+s[i];
    return (int)(v&255u);
}
static bool short_name(const unsigned char *e,char *out) {
    unsigned n=0;
    for (unsigned i=0;i<8;i++) {
        if (e[i]==' ') break;
        if (!e[i] || e[i]>127) return false;
        out[n++]=(char)e[i];
    }
    if (e[8]!=' ') {
        out[n++]='.';
        for (unsigned i=8;i<11;i++) {
            if (e[i]==' ') break;
            if (!e[i] || e[i]>127) return false;
            out[n++]=(char)e[i];
        }
    }
    out[n]=0;
    return n>0;
}
static bool lfn_name(DirectoryState *s, char *out) {
    if (!s->lfn_good || s->sequence != 1) return false;
    unsigned n=0;
    for (;n<255;n++) {
        uint16_t ch=s->unicode[n];
        if (!ch || ch==0xffff) break;
        if (ch>127) return false;
        out[n]=(char)ch;
    }
    out[n]=0;
    return n>0;
}
static void lfn_part(DirectoryState *s,const unsigned char *e) {
    unsigned order=e[0]&31u;
    if (e[0]&0x40) {
        memset(s->unicode,0,sizeof(s->unicode));
        s->sequence=order+1;
        s->checksum=e[13];
        s->lfn_good=order>=1 && order<=20;
    }
    if (!s->lfn_good || order==0 || order>=s->sequence ||
        e[13]!=s->checksum || order+1!=s->sequence) {
        s->lfn_good=false;
        return;
    }
    static const unsigned offsets[13]=
        {1,3,5,7,9,14,16,18,20,22,24,28,30};
    for (unsigned i=0;i<13;i++) {
        unsigned at=(order-1)*13+i;
        if (at<256) s->unicode[at]=le16(e+offsets[i]);
    }
    s->sequence=order;
}
static int fat_entry(DirectoryState *s,const unsigned char *e,
                     const char *wanted,FileInfo *found) {
    if (e[0]==0xe5) {s->lfn_good=false;return 0;}
    if (e[11]==0x0f) {lfn_part(s,e);return 0;}
    if (e[11]&0x08) {s->lfn_good=false;return 0;}
    char name[DND_NAME];
    bool valid=false;
    if (s->lfn_good && s->checksum==(unsigned)lfn_checksum(e))
        valid=lfn_name(s,name);
    if (!valid) valid=short_name(e,name);
    s->lfn_good=false;
    if (!valid || !same_ascii(name,wanted)) return 0;
    found->directory=(e[11]&0x10)!=0;
    found->first=((uint32_t)le16(e+20)<<16)|le16(e+26);
    found->length=le32(e+28);
    found->contiguous=false;
    return 1;
}
static int exfat_entry(DirectoryState *s,const unsigned char *e,
                       const char *wanted,FileInfo *found) {
    if (e[0]==0x85) {
        memset(s,0,sizeof(*s));
        s->remaining=e[1];
        s->ex_valid=s->remaining>=2 && s->remaining<=18;
        s->entry.directory=(le16(e+4)&0x10u)!=0;
        return 0;
    }
    if (!s->remaining) return 0;
    if (e[0]==0xc0 && !s->ex_stream) {
        s->ex_stream=true;
        s->name_length=e[3];
        s->ex_valid &= s->name_length>0 && s->name_length<256;
        s->entry.contiguous=(e[1]&2u)!=0; /* NoFatChain */
        s->entry.first=le32(e+20);
        s->entry.length=le64(e+24);
    } else if (e[0]==0xc1 && s->ex_stream) {
        for (unsigned i=0;i<15 && s->name_written<s->name_length;i++) {
            uint16_t c=le16(e+2+i*2);
            if (c>127 || !c) s->ex_valid=false;
            else s->name[s->name_written]=(char)c;
            s->name_written++;
        }
    } else s->ex_valid=false;
    s->remaining--;
    if (!s->remaining) {
        if (s->ex_valid && s->name_written==s->name_length &&
            same_ascii(s->name,wanted)) {
            *found=s->entry;
            return 1;
        }
    }
    return 0;
}
static int find_file(Volume *v,uint32_t first,uint64_t dir_size,bool nofat,
                     const char *wanted,FileInfo *out) {
    DirectoryState state={0};
    uint32_t current=first;
    uint64_t done=0, cluster_bytes=(uint64_t)v->spc*DND_SECTOR;
    uint64_t max= nofat ? (dir_size+cluster_bytes-1)/cluster_bytes : v->clusters;
    if (max>v->clusters) return -EIO;
    for (uint64_t visited=0;visited<max;visited++) {
        uint64_t lba;
        int rc=cluster_lba(v,current,&lba);
        if (rc<0) return rc;
        for (uint32_t s=0;s<v->spc;s++) {
            _Alignas(32) unsigned char sector[DND_SECTOR];
            rc=read_sector(v,lba+s,sector);
            if (rc<0) return rc;
            for (unsigned at=0;at<DND_SECTOR;at+=32) {
                if (nofat && done>=dir_size) return -ENOENT;
                const unsigned char *e=sector+at;
                if (e[0]==0) return -ENOENT;
                int match=v->exfat ? exfat_entry(&state,e,wanted,out)
                                   : fat_entry(&state,e,wanted,out);
                if (match) return 0;
                done+=32;
            }
        }
        if (nofat) current++;
        else {
            uint32_t next;
            rc=next_cluster(v,current,&next);
            if (rc<0) return rc;
            if (is_eoc(v,next)) return -ENOENT;
            current=next;
        }
    }
    return -EIO;
}
int dnd_fat_count_extents(const DndSectorReader *reader,
                          const char *relative_path,unsigned *extents) {
    if (!reader || !reader->read_sector || !relative_path || !extents)
        return -EINVAL;
    Volume v={0};
    v.reader=reader;
    int rc=boot(&v);
    if (rc<0) return rc;
    const char *p=relative_path;
    while (*p=='/') p++;
    if (!*p) return -EINVAL;
    uint32_t directory=v.root;
    uint64_t directory_size=0;
    bool directory_contiguous=false;
    FileInfo found={0};
    for (unsigned depth=0;depth<64;depth++) {
        char part[DND_NAME];
        unsigned n=0;
        while (*p && *p!='/' && *p!='\\') {
            unsigned char c=(unsigned char)*p++;
            if (!c || c>127 || n>=255) return -ENOTSUP;
            part[n++]=(char)c;
        }
        part[n]=0;
        if (!n || !strcmp(part,".") || !strcmp(part,"..")) return -EINVAL;
        rc=find_file(&v,directory,directory_size,
                     directory_contiguous,part,&found);
        if (rc<0) return rc;
        while (*p=='/' || *p=='\\') p++;
        if (!*p) {
            if (found.directory) return -EISDIR;
            if (!found.length) { *extents=0;return 0; }
            if (found.first<2) return -EIO;
            uint64_t cluster_bytes=(uint64_t)v.spc*DND_SECTOR;
            uint64_t nclusters=(found.length+cluster_bytes-1)/cluster_bytes;
            if (!nclusters || nclusters>v.clusters) return -EIO;
            if (found.contiguous) {
                if ((uint64_t)found.first+nclusters>(uint64_t)v.clusters+2)
                    return -EIO;
                *extents=1;
                return 0;
            }
            uint32_t current=found.first;
            unsigned runs=1;
            for (uint64_t i=1;i<nclusters;i++) {
                uint32_t next;
                rc=next_cluster(&v,current,&next);
                if (rc<0 || is_eoc(&v,next)) return -EIO;
                if (next!=current+1) runs++;
                current=next;
            }
            uint32_t end;
            rc=next_cluster(&v,current,&end);
            if (rc<0 || !is_eoc(&v,end)) return -EIO;
            *extents=runs;
            return 0;
        }
        if (!found.directory || found.first<2) return -ENOTDIR;
        directory=found.first;
        directory_size=found.length;
        directory_contiguous=v.exfat && found.contiguous;
    }
    return -ENAMETOOLONG;
}
