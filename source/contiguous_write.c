#define _POSIX_C_SOURCE 200809L
#include "dnd_contiguous_write.h"
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#define JOURNAL_NAME ".__dndcw.jrn"
#define JOURNAL_MAGIC "DNDCTG01"
#define BUFFER_SIZE 16384
typedef struct {
  char magic[8];
  uint64_t expected;
  char target[DND_CW_PATH_CAP], staged[DND_CW_PATH_CAP];
  uint32_t hash;
} Journal;
static uint32_t hash32(const void *source,size_t size) {
  const unsigned char *bytes=source;
  uint32_t h=2166136261u;
  for (size_t i=0;i<size;i++) h=(h^bytes[i])*16777619u;
  return h;
}
static uint64_t hash64(uint64_t h,const unsigned char *data,size_t count) {
  for(size_t i=0;i<count;i++) h=(h^data[i])*UINT64_C(1099511628211);
  return h;
}
static int build_path(char *output,const char *root,const char *name) {
  size_t r=strlen(root),n=strlen(name);
  if (!r || r+n+(root[r-1]=='/'?0u:1u)>=DND_CW_PATH_CAP)
    return -ENAMETOOLONG;
  memcpy(output,root,r);
  size_t at=r;
  if(root[r-1]!='/')output[at++]='/';
  memcpy(output+at,name,n+1);
  return 0;
}
static bool below_root(const char *root,const char *path) {
  size_t n=strlen(root);
  if (!n || strncmp(root,path,n))return false;
  if (root[n-1]!='/') {
    if(path[n]!='/')return false;
    n++;
  }
  return path[n]!=0 && strlen(path)<DND_CW_PATH_CAP;
}
static int exists(const char *p) {
  struct stat s;
  return stat(p,&s)==0?1:errno==ENOENT?0:-errno;
}
static int close_sync(int fd) {
  int rc=fsync(fd)<0?-errno:0;
  if(close(fd)<0 && !rc)rc=-errno;
  return rc;
}
static int write_all(int fd,const void *data,size_t len) {
  const unsigned char *bytes=data;
  while(len) {
    ssize_t n=write(fd,bytes,len);
    if(n<0 && errno==EINTR)continue;
    if(n<=0)return n<0?-errno:-EIO;
    bytes+=(size_t)n;
    len-=(size_t)n;
  }
  return 0;
}
static int journal_path(char out[DND_CW_PATH_CAP],const char *root) {
  return build_path(out,root,JOURNAL_NAME);
}
static int delete_journal(const char *root) {
  char path[DND_CW_PATH_CAP];
  int rc=journal_path(path,root);
  if(rc)return rc;
  if(unlink(path)<0 && errno!=ENOENT)return -errno;
  return 0;
}
static int load_journal(const char *root,Journal *j) {
  char path[DND_CW_PATH_CAP];
  int rc=journal_path(path,root);
  if(rc)return rc;
  int fd=open(path,O_RDONLY);
  if(fd<0)return errno==ENOENT?0:-errno;
  unsigned char *p=(unsigned char *)j;
  size_t done=0;
  while(done<sizeof(*j)) {
    ssize_t n=read(fd,p+done,sizeof(*j)-done);
    if(n<0 && errno==EINTR)continue;
    if(n<=0){rc=n<0?-errno:-EIO;break;}
    done+=(size_t)n;
  }
  if(!rc) {
    unsigned char extra;
    ssize_t n=read(fd,&extra,1);
    if(n!=0)rc=n<0?-errno:-EINVAL;
  }
  if(close(fd)<0 && !rc)rc=-errno;
  if(rc)return rc;
  if(memcmp(j->magic,JOURNAL_MAGIC,8) ||
     !memchr(j->target,0,DND_CW_PATH_CAP) ||
     !memchr(j->staged,0,DND_CW_PATH_CAP) ||
     !below_root(root,j->target) || !below_root(root,j->staged) ||
     !strcmp(j->target,j->staged) ||
     j->expected>INT32_MAX ||
     hash32(j,offsetof(Journal,hash))!=j->hash)
    return -EINVAL;
  return 1;
}
static int save_journal(const DndContiguousWriter *w) {
  Journal j={0};
  memcpy(j.magic,JOURNAL_MAGIC,8);
  j.expected=w->expected;
  strcpy(j.target,w->target);
  strcpy(j.staged,w->staged);
  j.hash=hash32(&j,offsetof(Journal,hash));
  char path[DND_CW_PATH_CAP];
  int rc=journal_path(path,w->root);
  if(rc)return rc;
  int fd=open(path,O_WRONLY|O_CREAT|O_EXCL,0600);
  if(fd<0)return -errno;
  rc=write_all(fd,&j,sizeof(j));
  if(!rc)rc=close_sync(fd);
  else close(fd);
  if(rc) {
    /* Torn journal is not trusted. Remove only if we created it ourselves. */
    unlink(path);
  }
  return rc;
}
int dnd_contiguous_recover(const char *root) {
  if(!root)return -EINVAL;
  Journal journal={0};
  int rc=load_journal(root,&journal);
  if(rc<=0)return rc;
  /* A published target is intentionally never removed during recovery.
   * The journal names only our staging path, so cleanup cannot overwrite a
   * user file. On media failure, leave the journal intact to retry later. */
  if(unlink(journal.staged)<0 && errno!=ENOENT)return -errno;
  return delete_journal(root);
}
static void reset(DndContiguousWriter *w) {
  w->fd=-1;w->active=false;
}
int dnd_contiguous_abort(DndContiguousWriter *w) {
  if(!w||!w->active)return -EINVAL;
  int rc=0;
  if(w->fd>=0 && close(w->fd)<0)rc=-errno;
  reset(w);
  if(unlink(w->staged)<0 && errno!=ENOENT && !rc)rc=-errno;
  if(!rc)rc=delete_journal(w->root);
  return rc;
}
static int interrupted(DndContiguousWriter *w,int checkpoint) {
  if(!w->ops.checkpoint)return 0;
  int rc=w->ops.checkpoint(w->ops.context,checkpoint);
  if(rc<0) {
    if(w->fd>=0)close(w->fd);
    reset(w); /* Like a crash, leave the durable journal for mount recovery. */
  }
  return rc;
}
int dnd_contiguous_begin(DndContiguousWriter *w,const char *root,
                         const char *target,uint64_t length,DndContiguousOps ops) {
  if(!w||!root||!target||!ops.count_extents ||
     length>INT32_MAX || !below_root(root,target))
    return -EINVAL;
  if(w->active)return -EBUSY;
  memset(w,0,sizeof(*w));
  w->fd=-1;
  w->ops=ops;
  w->expected=length;
  strcpy(w->root,root);
  strcpy(w->target,target);
  int rc=dnd_contiguous_recover(root);
  if(rc<0)return rc;
  int occupied=exists(target);
  if(occupied<0)return occupied;
  if(occupied)return -EEXIST; /* No destructive replacement. */
  char filename[48];
  snprintf(filename,sizeof(filename),".__dndcw_%08x.part",
           hash32(target,strlen(target)));
  rc=build_path(w->staged,root,filename);
  if(rc)return rc;
  occupied=exists(w->staged);
  if(occupied<0)return occupied;
  if(occupied)return -EEXIST; /* Never remove an unrelated temporary file. */
  rc=save_journal(w);
  if(rc)return rc;
  w->fd=open(w->staged,O_WRONLY|O_CREAT|O_EXCL,0600);
  if(w->fd<0){rc=-errno;delete_journal(root);return rc;}
  w->active=true;
  if(ftruncate(w->fd,(off_t)length)<0) {
    rc=-errno;dnd_contiguous_abort(w);return rc;
  }
  /* libdvm's ftruncate tries FatFs f_expand() but does not check whether the
   * contiguous request succeeded. Verify physical extents before data I/O. */
  if(fsync(w->fd)<0) {rc=-errno;dnd_contiguous_abort(w);return rc;}
  unsigned extents=0;
  rc=ops.count_extents(ops.context,w->staged,&extents);
  if(rc<0 || extents!=(length?1u:0u)) {
    dnd_contiguous_abort(w);
    return rc<0?rc:-ENOSPC;
  }
  if(lseek(w->fd,0,SEEK_SET)<0) {
    rc=-errno;dnd_contiguous_abort(w);return rc;
  }
  w->checksum=UINT64_C(14695981039346656037);
  return interrupted(w,DND_CW_AFTER_RESERVE);
}
int dnd_contiguous_write(DndContiguousWriter *w,const void *data,size_t length) {
  if(!w||!w->active||w->fd<0||(!data&&length))return -EINVAL;
  if(length>w->expected-w->written)return -EFBIG;
  const unsigned char *src=data;
  _Alignas(32) unsigned char buffer[BUFFER_SIZE];
  while(length) {
    size_t n=length<sizeof(buffer)?length:sizeof(buffer);
    memcpy(buffer,src,n);
    int rc=write_all(w->fd,buffer,n);
    if(rc<0)return rc;
    w->checksum=hash64(w->checksum,buffer,n);
    w->written+=n;
    src+=n;length-=n;
  }
  return 0;
}
static int verify(const DndContiguousWriter *w) {
  int fd=open(w->staged,O_RDONLY);
  if(fd<0)return -errno;
  _Alignas(32) unsigned char data[BUFFER_SIZE];
  uint64_t digest=UINT64_C(14695981039346656037),len=0;
  int rc=0;
  for(;;) {
    ssize_t n=read(fd,data,sizeof(data));
    if(n<0 && errno==EINTR)continue;
    if(n<0){rc=-errno;break;}
    if(n==0)break;
    digest=hash64(digest,data,(size_t)n);
    len+=(uint64_t)n;
  }
  if(close(fd)<0&&!rc)rc=-errno;
  if(rc)return rc;
  return len==w->expected && digest==w->checksum?0:-EIO;
}
int dnd_contiguous_complete(DndContiguousWriter *w) {
  if(!w||!w->active||w->fd<0)return -EINVAL;
  if(w->written!=w->expected)return -EINVAL;
  int rc=close_sync(w->fd);
  w->fd=-1;
  if(rc<0){dnd_contiguous_abort(w);return rc;}
  rc=verify(w);
  if(rc<0){dnd_contiguous_abort(w);return rc;}
  unsigned extents=0;
  rc=w->ops.count_extents(w->ops.context,w->staged,&extents);
  if(rc<0 || extents!=(w->expected?1u:0u)) {
    dnd_contiguous_abort(w);
    return rc<0?rc:-ENOSPC;
  }
  int taken=exists(w->target);
  if(taken<0 || taken) {
    dnd_contiguous_abort(w);
    return taken<0?taken:-EEXIST;
  }
  rc=interrupted(w,DND_CW_BEFORE_PUBLISH);
  if(rc<0)return rc;
  if(rename(w->staged,w->target)<0){
    rc=-errno;dnd_contiguous_abort(w);return rc;
  }
  /* A rename does not rewrite the data or allocation chain. */
  rc=interrupted(w,DND_CW_AFTER_PUBLISH);
  if(rc<0)return rc;
  reset(w);
  return delete_journal(w->root);
}
