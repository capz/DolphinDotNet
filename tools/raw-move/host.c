#define _POSIX_C_SOURCE 200809L
#define _XOPEN_SOURCE 700
#include "host.h"
#include <errno.h>
#include <fcntl.h>
#include <inttypes.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/file.h>
#include <sys/xattr.h>
#include <unistd.h>

static int tick(RawHost *h) {return ++h->events==h->fail_after?-EINTR:0;}
static int close_one(int fd) {return fd<0?0:close(fd)<0?-errno:0;}
static int exact(int fd,void *data,size_t bytes,uint64_t offset,int write_it) {
    if(offset>INT64_MAX || bytes>(uint64_t)INT64_MAX-offset)return -EFBIG;
    unsigned char *p=data;
    while(bytes) {
        ssize_t n=write_it?pwrite(fd,p,bytes,(off_t)offset):pread(fd,p,bytes,(off_t)offset);
        if(n<0 && errno==EINTR)continue;
        if(n<=0)return n<0?-errno:-EIO;
        p+=n;offset+=(uint64_t)n;bytes-=(size_t)n;
    }
    return 0;
}
int raw_host_hash(int fd,unsigned char digest[32]) {
    struct stat st;if(fstat(fd,&st)<0)return -errno;
    if(!S_ISREG(st.st_mode) || st.st_size<0)return -EINVAL;
    _Alignas(32) unsigned char buffer[DND_COPY_BUFFER];DndSha256 h;dnd_sha256_init(&h);
    uint64_t at=0;while(at<(uint64_t)st.st_size) {
        size_t n=(uint64_t)st.st_size-at<sizeof(buffer)?(size_t)((uint64_t)st.st_size-at):sizeof(buffer);
        int rc=exact(fd,buffer,n,at,0);if(rc)return rc;dnd_sha256_update(&h,buffer,n);at+=n;
    }
    dnd_sha256_finish(&h,digest);return 0;
}
static int regular(const char *path,int flags,int *fd,struct stat *out) {
    struct stat before;if(lstat(path,&before)<0)return -errno;
    if(!S_ISREG(before.st_mode) || before.st_nlink!=1)return -EINVAL;
    int f=open(path,flags|O_NOFOLLOW);if(f<0)return -errno;
    struct stat after;if(fstat(f,&after)<0) {int rc=-errno;close_one(f);return rc;}
    if(before.st_dev!=after.st_dev || before.st_ino!=after.st_ino || !S_ISREG(after.st_mode)) {
        close_one(f);return -ESTALE;
    }
    *fd=f;*out=after;return 0;
}
static long modified_nsec(const struct stat *s) {
#ifdef __APPLE__
    return s->st_mtimespec.tv_nsec;
#else
    return s->st_mtim.tv_nsec;
#endif
}
static long changed_nsec(const struct stat *s) {
#ifdef __APPLE__
    return s->st_ctimespec.tv_nsec;
#else
    return s->st_ctim.tv_nsec;
#endif
}
static int same_name(const char *path,int fd,const struct stat *expected,int immutable) {
    struct stat named,opened;
    if(lstat(path,&named)<0 || fstat(fd,&opened)<0)return -errno;
    if(named.st_dev!=expected->st_dev || named.st_ino!=expected->st_ino ||
       opened.st_dev!=expected->st_dev || opened.st_ino!=expected->st_ino ||
       opened.st_size!=expected->st_size || !S_ISREG(named.st_mode) || named.st_nlink!=1)return -ESTALE;
    if(immutable && (opened.st_mtime!=expected->st_mtime || opened.st_ctime!=expected->st_ctime ||
       modified_nsec(&opened)!=modified_nsec(expected) || changed_nsec(&opened)!=changed_nsec(expected)))return -ESTALE;
    return 0;
}
static int journal_guard(RawHost *h,unsigned v) {
    struct stat named,opened;
    if(h->journal[v]<0)return -ENODEV;
    if(lstat(h->journal_path[v],&named)<0 || fstat(h->journal[v],&opened)<0)return -ENODEV;
    return !S_ISDIR(named.st_mode) || named.st_dev!=h->journal_stat[v].st_dev ||
        named.st_ino!=h->journal_stat[v].st_ino || opened.st_dev!=named.st_dev ||
        opened.st_ino!=named.st_ino?-ESTALE:0;
}
static uint64_t le64(const unsigned char *p);
static int request_anchor(RawHost *h,unsigned char hash[32],int create) {
#ifdef __APPLE__
    int rc=create?fsetxattr(h->image,"user.dnd_p6_request",hash,32,0,XATTR_CREATE):
        (fgetxattr(h->image,"user.dnd_p6_request",hash,32,0,0)==32?0:-1);
#else
    int rc=create?fsetxattr(h->image,"user.dnd_p6_request",hash,32,XATTR_CREATE):
        (fgetxattr(h->image,"user.dnd_p6_request",hash,32)==32?0:-1);
#endif
    if(rc<0)return -ESTALE;
    return create && fsync(h->image)<0?-errno:0;
}
int raw_host_bind_request(RawHost *h,const DndRawMove *r) {
    unsigned char wire[512];int rc=dnd_raw_request_encode(r,wire);if(rc)return rc;
    DndSha256 hash;dnd_sha256_init(&hash);dnd_sha256_update(&hash,wire,512);
    dnd_sha256_finish(&hash,h->request_sha256);
    return request_anchor(h,h->request_sha256,1);
}
static int host_guard(void *ctx,const DndRawMove *r) {
    RawHost *h=ctx;int rc=same_name(h->image_path,h->image,&h->image_stat,0);if(rc)return rc;
    if((uint64_t)h->image_stat.st_dev!=r->image_id[0] || (uint64_t)h->image_stat.st_ino!=r->image_id[1] ||
       (uint64_t)h->image_stat.st_size/512!=r->sector_count || h->generation!=r->generation)return -ESTALE;
    unsigned char token[8];
#ifdef __APPLE__
    ssize_t token_bytes=fgetxattr(h->image,"user.dnd_p6_identity",token,8,0,0);
#else
    ssize_t token_bytes=fgetxattr(h->image,"user.dnd_p6_identity",token,8);
#endif
    if(token_bytes!=8 || le64(token)!=r->generation)return -ESTALE;
    unsigned char anchor[32],wire[512],binding[32];
    rc=request_anchor(h,anchor,0);if(rc)return rc;
    rc=dnd_raw_request_encode(r,wire);if(rc)return rc;
    DndSha256 hash;dnd_sha256_init(&hash);dnd_sha256_update(&hash,wire,512);dnd_sha256_finish(&hash,binding);
    if(memcmp(anchor,binding,32) || memcmp(anchor,h->request_sha256,32))return -ESTALE;
    if(h->backup<0 || memcmp(h->backup_sha256,r->backup_sha256,32))return -ESTALE;
    rc=same_name(h->backup_path,h->backup,&h->backup_stat,1);if(rc)return rc;
    for(unsigned v=0;v<2;v++) {rc=journal_guard(h,v);if(rc)return rc;}
    return 0;
}
static int host_read(void *ctx,uint64_t lba,void *out) {
    RawHost *h=ctx;if(lba>=(uint64_t)h->image_stat.st_size/512)return -EIO;
    return exact(h->image,out,512,lba*512,0);
}
static int host_write(void *ctx,uint64_t lba,const void *in) {
    RawHost *h=ctx;if(lba>=(uint64_t)h->image_stat.st_size/512)return -EIO;
    int rc=exact(h->image,(void *)in,512,lba*512,1);return rc?rc:tick(h);
}
static int host_sync_image(void *ctx) {
    RawHost *h=ctx;if(fsync(h->image)<0)return -errno;return tick(h);
}
static int host_invalidate(void *ctx) {
    /* pread has no userspace cache; reopening confirms the same file identity. */
    RawHost *h=ctx;int fd=-1;struct stat st;
    int rc=regular(h->image_path,O_RDONLY,&fd,&st);if(rc)return rc;
    if(st.st_dev!=h->image_stat.st_dev || st.st_ino!=h->image_stat.st_ino)rc=-ESTALE;
    int closed=close_one(fd);return rc?rc:closed;
}
static int filename(unsigned kind,uint64_t index,char out[64]) {
    int n;
    if(kind==0 && !index)n=snprintf(out,64,"request.bin");
    else if(kind==1)n=snprintf(out,64,"patch_%016" PRIx64 ".bin",index);
    else if(kind==2 && index<2)n=snprintf(out,64,"checkpoint_%u.bin",(unsigned)index);
    else return -EINVAL;
    return n<0 || n>=64?-EINVAL:0;
}
static int read_at_dir(int dir,const char *name,void *out,size_t n,int partial) {
    int fd=openat(dir,name,O_RDONLY|O_NOFOLLOW);if(fd<0)return -errno;
    struct stat st;int rc=fstat(fd,&st)<0?-errno:0;
    if(!rc && (!S_ISREG(st.st_mode) || st.st_nlink!=1 || st.st_size<0 ||
       (uint64_t)st.st_size>n || (!partial && (uint64_t)st.st_size!=n)))rc=-EILSEQ;
    if(!rc) {memset(out,0,n);if(st.st_size)rc=exact(fd,out,(size_t)st.st_size,0,0);}
    int closed=close_one(fd);return rc?rc:closed;
}
static int write_at_dir(int dir,const char *name,const void *data,size_t n,int replace,int sync_it) {
    int fd=openat(dir,name,O_WRONLY|O_CREAT|O_NOFOLLOW|(replace?0:O_EXCL),0600);
    if(fd<0 && errno==EEXIST && !replace) {
        unsigned char check[DND_RAW_PATCH_BYTES];if(n>sizeof(check))return -EINVAL;
        int rc=read_at_dir(dir,name,check,n,0);
        return rc?rc:memcmp(data,check,n)?-EEXIST:0;
    }
    if(fd<0)return -errno;
    struct stat st;int rc=fstat(fd,&st)<0?-errno:0;
    if(!rc && (!S_ISREG(st.st_mode) || st.st_nlink!=1 || st.st_size<0 || (uint64_t)st.st_size>n))rc=-EINVAL;
    if(!rc)rc=exact(fd,(void *)data,n,0,1);
    if(!rc && sync_it && fsync(fd)<0)rc=-errno;
    int closed=close_one(fd);return rc?rc:closed;
}
static int host_read_record(void *ctx,unsigned v,unsigned kind,uint64_t i,void *out,size_t n) {
    RawHost *h=ctx;if(v>1)return -EINVAL;int rc=journal_guard(h,v);if(rc)return rc;
    char name[64];rc=filename(kind,i,name);if(rc)return rc;
    return read_at_dir(h->journal[v],name,out,n,1);
}
static uint64_t le64(const unsigned char *p) {
    uint64_t v=0;for(unsigned i=0;i<8;i++)v|=(uint64_t)p[i]<<(8*i);return v;
}
static void enc64(unsigned char *p,uint64_t v) {for(unsigned i=0;i<8;i++)p[i]=(unsigned char)(v>>(8*i));}
static int host_write_record(void *ctx,unsigned v,unsigned kind,uint64_t i,const void *data,size_t n) {
    RawHost *h=ctx;if(v>1)return -EINVAL;int rc=journal_guard(h,v);if(rc)return rc;
    char name[64];rc=filename(kind,i,name);if(rc)return rc;
    rc=write_at_dir(h->journal[v],name,data,n,kind==2,1);if(rc)return rc;
    if(kind==1) {
        const unsigned char *wire=data;DndRawPatch patch;
        rc=dnd_raw_patch_decode(wire,i,wire+24,&patch);if(rc)return rc;
        snprintf(name,sizeof(name),"lba_%016" PRIx64 ".idx",patch.lba);
        unsigned char ordinal[8];enc64(ordinal,i);
        rc=write_at_dir(h->journal[v],name,ordinal,8,0,1);if(rc)return rc;
    }
    return tick(h);
}
static int host_sync_journal(void *ctx,unsigned v) {
    RawHost *h=ctx;if(v>1)return -EINVAL;int rc=journal_guard(h,v);if(rc)return rc;
    if(fsync(h->journal[v])<0)return -errno;
    return tick(h);
}
static int host_build_reset(void *ctx) {
    RawHost *h=ctx;close_one(h->index);h->index=-1;close_one(h->build);h->build=-1;h->count=0;
    char name[64];
    for(unsigned attempt=0;attempt<100000;attempt++) {
        snprintf(name,sizeof(name),"build_%08x_%08x",(unsigned)getpid(),attempt);
        if(mkdirat(h->journal[0],name,0700)==0) {
            h->build=openat(h->journal[0],name,O_RDONLY|O_DIRECTORY|O_NOFOLLOW);
            if(h->build<0)return -errno;
            h->index=openat(h->build,"index.bin",O_RDWR|O_CREAT|O_EXCL|O_NOFOLLOW,0600);
            return h->index<0?-errno:0;
        }
        if(errno!=EEXIST)return -errno;
    }
    return -EEXIST;
}
static int host_build_get(void *ctx,uint64_t lba,DndRawPatch *out) {
    RawHost *h=ctx;if(h->build<0)return -EINVAL;
    char name[64];snprintf(name,sizeof(name),"l_%016" PRIx64 ".bin",lba);
    unsigned char wire[2048],binding[32]={0};int rc=read_at_dir(h->build,name,wire,2048,0);if(rc)return rc;
    uint64_t index=le64(wire+8);if(index>=h->count)return -EILSEQ;
    rc=dnd_raw_patch_decode(wire,index,binding,out);return rc?rc:out->lba!=lba?-EILSEQ:0;
}
static int host_build_put(void *ctx,const DndRawPatch *p) {
    RawHost *h=ctx;char name[64];snprintf(name,sizeof(name),"l_%016" PRIx64 ".bin",p->lba);
    unsigned char wire[2048],binding[32]={0};int rc=read_at_dir(h->build,name,wire,2048,0);
    uint64_t index=h->count;
    if(!rc) {
        index=le64(wire+8);DndRawPatch previous;
        rc=dnd_raw_patch_decode(wire,index,binding,&previous);
        if(rc || index>=h->count || previous.lba!=p->lba || memcmp(previous.before,p->before,512))return -EILSEQ;
    } else if(rc!=-ENOENT)return rc;
    int fresh=rc==-ENOENT;
    rc=dnd_raw_patch_encode(p,index,binding,wire);if(rc)return rc;
    rc=write_at_dir(h->build,name,wire,2048,!fresh,0);if(rc)return rc;
    if(fresh) {
        unsigned char encoded[8];enc64(encoded,p->lba);
        rc=exact(h->index,encoded,8,index*8,1);if(rc)return rc;h->count++;
    }
    return 0;
}
static int host_build_count(void *ctx,uint64_t *out) {*out=((RawHost *)ctx)->count;return 0;}
static int host_build_read(void *ctx,uint64_t i,DndRawPatch *out) {
    RawHost *h=ctx;if(i>=h->count)return -EINVAL;
    unsigned char encoded[8];int rc=exact(h->index,encoded,8,i*8,0);if(rc)return rc;
    return host_build_get(ctx,le64(encoded),out);
}
static int host_find(void *ctx,unsigned v,uint64_t lba,uint64_t *out) {
    RawHost *h=ctx;int rc=journal_guard(h,v);if(rc)return rc;
    char name[64];snprintf(name,sizeof(name),"lba_%016" PRIx64 ".idx",lba);
    unsigned char wire[8];rc=read_at_dir(h->journal[v],name,wire,8,0);if(!rc)*out=le64(wire);return rc;
}
static int host_cancelled(void *ctx) {return ((RawHost *)ctx)->cancelled!=0;}
DndRawOps raw_host_ops(RawHost *h) {
    return (DndRawOps){.context=h,.medium_kind=DND_RAW_IMAGE,.guard=host_guard,.read_sector=host_read,
        .write_sector=host_write,.sync_image=host_sync_image,.invalidate=host_invalidate,
        .read_record=host_read_record,.write_record=host_write_record,.sync_journal=host_sync_journal,
        .build_reset=host_build_reset,.build_get=host_build_get,.build_put=host_build_put,
        .build_count=host_build_count,.build_read=host_build_read,.find_patch=host_find,.cancelled=host_cancelled};
}
static int image_token(RawHost *h,int create,int optional) {
    unsigned char token[8];
    if(create) {
        int random_fd=open("/dev/urandom",O_RDONLY);if(random_fd<0)return -errno;
        ssize_t got;do {got=read(random_fd,token,8);}while(got<0 && errno==EINTR);
        int closed=close_one(random_fd);if(got!=8)return -EIO;if(closed)return closed;
        if(!le64(token))return -EIO;
#ifdef __APPLE__
        int rc=fsetxattr(h->image,"user.dnd_p6_identity",token,8,0,XATTR_CREATE);
#else
        int rc=fsetxattr(h->image,"user.dnd_p6_identity",token,8,XATTR_CREATE);
#endif
        if(rc<0)return -errno;
        if(fsync(h->image)<0)return -errno;
    } else {
#ifdef __APPLE__
        ssize_t n=fgetxattr(h->image,"user.dnd_p6_identity",token,8,0,0);
#else
        ssize_t n=fgetxattr(h->image,"user.dnd_p6_identity",token,8);
#endif
        if(n<0) {
            if(optional) {h->generation=0;return 0;}
            return -ESTALE;
        }
        if(n!=8 || !le64(token))return -EILSEQ;
    }
    h->generation=le64(token);return 0;
}
static int sync_parent(const char *path) {
    char parent[RAW_HOST_PATH];strcpy(parent,path);char *slash=strrchr(parent,'/');
    if(!slash)strcpy(parent,".");else if(slash==parent)slash[1]=0;else *slash=0;
    int fd=open(parent,O_RDONLY|O_DIRECTORY);if(fd<0)return -errno;
    int rc=fsync(fd)<0?-errno:0;int closed=close_one(fd);return rc?rc:closed;
}
int raw_host_open(RawHost *h,const char *image,const char *backup,const char *a,const char *b,int create,int readonly) {
    memset(h,0,sizeof(*h));h->image=h->backup=h->build=h->index=h->journal[0]=h->journal[1]=-1;
    if(strlen(image)>=RAW_HOST_PATH || (backup && strlen(backup)>=RAW_HOST_PATH))return -ENAMETOOLONG;
    strcpy(h->image_path,image);int rc=regular(image,readonly?O_RDONLY:O_RDWR,&h->image,&h->image_stat);if(rc)return rc;
    if(h->image_stat.st_size<=0 || h->image_stat.st_size%512)return -EINVAL;
    if(flock(h->image,(readonly?LOCK_SH:LOCK_EX)|LOCK_NB)<0)return -errno;
    if(backup) {
        strcpy(h->backup_path,backup);rc=regular(backup,O_RDONLY,&h->backup,&h->backup_stat);if(rc)return rc;
        if(h->backup_stat.st_dev==h->image_stat.st_dev && h->backup_stat.st_ino==h->image_stat.st_ino)return -EINVAL;
        if(h->backup_stat.st_size!=h->image_stat.st_size)return -ESTALE;
        if(flock(h->backup,LOCK_SH|LOCK_NB)<0)return -errno;
        rc=raw_host_hash(h->backup,h->backup_sha256);if(rc)return rc;
        if(create) {
            unsigned char working[32];rc=raw_host_hash(h->image,working);if(rc)return rc;
            if(memcmp(working,h->backup_sha256,32))return -ESTALE;
        }
    }
    rc=image_token(h,create,readonly && !backup);if(rc)return rc;
    if(backup && !create) {rc=request_anchor(h,h->request_sha256,0);if(rc)return rc;}
    if(!a && !b)return 0;
    const char *roots[2]={a,b};
    for(unsigned v=0;v<2;v++) {
        if(!roots[v] || strlen(roots[v])>=RAW_HOST_PATH)return -ENAMETOOLONG;
        strcpy(h->journal_path[v],roots[v]);
        if(create && mkdir(roots[v],0700)<0)return -errno;
        if(create) {rc=sync_parent(roots[v]);if(rc)return rc;}
        h->journal[v]=open(roots[v],O_RDONLY|O_DIRECTORY|O_NOFOLLOW);if(h->journal[v]<0)return -errno;
        if(fstat(h->journal[v],&h->journal_stat[v])<0)return -errno;
        if(h->journal_stat[v].st_uid!=geteuid() || h->journal_stat[v].st_mode&0077)return -EACCES;
    }
    if(h->journal_stat[0].st_dev==h->journal_stat[1].st_dev && h->journal_stat[0].st_ino==h->journal_stat[1].st_ino)return -EINVAL;
    return 0;
}
void raw_host_close(RawHost *h) {
    close_one(h->index);close_one(h->build);close_one(h->journal[0]);close_one(h->journal[1]);
    close_one(h->backup);close_one(h->image);
}
int raw_host_load_request(RawHost *h,DndRawMove *out) {
    DndRawMove found;int have=0;
    for(unsigned v=0;v<2;v++) {
        unsigned char wire[512];int rc=host_read_record(h,v,0,0,wire,512);if(rc)return rc;
        DndRawMove decoded;rc=dnd_raw_request_decode(wire,&decoded);if(rc)return rc;
        if(have) {
            unsigned char expected[512];rc=dnd_raw_request_encode(&found,expected);if(rc)return rc;
            if(memcmp(expected,wire,512))return -EILSEQ;
        } else found=decoded;
        have=1;
    }
    if(memcmp(found.backup_sha256,h->backup_sha256,32))return -ESTALE;
    unsigned char wire[512],binding[32];int rc=dnd_raw_request_encode(&found,wire);if(rc)return rc;
    DndSha256 hash;dnd_sha256_init(&hash);dnd_sha256_update(&hash,wire,512);dnd_sha256_finish(&hash,binding);
    if(memcmp(binding,h->request_sha256,32))return -ESTALE;
    *out=found;return 0;
}
