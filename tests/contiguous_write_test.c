#define _POSIX_C_SOURCE 200809L
#include "dnd_contiguous_write.h"
#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

typedef struct { int extents, interrupt_at, probes; } Faults;
static int count(void *ctx,const char *path,unsigned *extents) {
  Faults *f=ctx;(void)path;
  f->probes++;
  *extents=(unsigned)f->extents;
  return 0;
}
static int checkpoint(void *ctx,int point) {
  Faults *f=ctx;
  return f->interrupt_at==point?-EINTR:0;
}
static int present(const char *path) {
  struct stat s;
  return stat(path,&s)==0;
}
static void file_data(const char *path,const unsigned char *bytes,size_t size) {
  int fd=open(path,O_RDONLY);
  assert(fd>=0);
  size_t done=0;
  unsigned char chunk[2048];
  while(done<size) {
    size_t n=size-done<sizeof(chunk)?size-done:sizeof(chunk);
    assert(read(fd,chunk,n)==(ssize_t)n);
    assert(!memcmp(chunk,bytes+done,n));
    done+=n;
  }
  assert(read(fd,chunk,1)==0);
  assert(close(fd)==0);
}
int main(void) {
  char root[]="/tmp/dnd-contig-XXXXXX";
  assert(mkdtemp(root));
  char path[1024],journal[1024];
  snprintf(path,sizeof(path),"%s/download.bin",root);
  snprintf(journal,sizeof(journal),"%s/.__dndcw.jrn",root);
  static unsigned char payload[135169];
  for(unsigned i=0;i<sizeof(payload);i++)payload[i]=(unsigned char)(i*31u+7u);
  Faults faults={1,0,0};
  DndContiguousOps ops={&faults,count,checkpoint};
  DndContiguousWriter writer={0};

  assert(dnd_contiguous_begin(&writer,root,path,sizeof(payload),ops)==0);
  assert(writer.active && present(writer.staged) && present(journal));
  assert(!present(path) && faults.probes==1);
  assert(dnd_contiguous_write(&writer,payload,8191)==0);
  assert(dnd_contiguous_complete(&writer)==-EINVAL);
  assert(dnd_contiguous_write(&writer,payload+8191,sizeof(payload)-8191)==0);
  assert(dnd_contiguous_complete(&writer)==0);
  assert(!writer.active && !present(journal) && present(path));
  file_data(path,payload,sizeof(payload));
  assert(dnd_contiguous_begin(&writer,root,path,3,ops)==-EEXIST);
  file_data(path,payload,sizeof(payload));
  assert(unlink(path)==0);

  faults.extents=3;
  assert(dnd_contiguous_begin(&writer,root,path,sizeof(payload),ops)==-ENOSPC);
  assert(!present(path) && !present(journal));
  faults.extents=1;

  assert(dnd_contiguous_begin(&writer,root,path,4,ops)==0);
  assert(dnd_contiguous_write(&writer,payload,5)==-EFBIG);
  assert(dnd_contiguous_abort(&writer)==0);
  assert(!present(path) && !present(journal));

  /* Data readback corruption prevents publication and retains no orphan. */
  assert(dnd_contiguous_begin(&writer,root,path,4,ops)==0);
  assert(dnd_contiguous_write(&writer,payload,4)==0);
  int fd=open(writer.staged,O_RDWR);
  assert(fd>=0);
  unsigned char corrupt=0xee;
  assert(pwrite(fd,&corrupt,1,0)==1);
  assert(close(fd)==0);
  assert(dnd_contiguous_complete(&writer)==-EIO);
  assert(!present(path) && !present(journal));

  /* Simulate power loss at each durable transition, then remount/recover. */
  const int checkpoints[]={DND_CW_AFTER_RESERVE,DND_CW_BEFORE_PUBLISH,
                            DND_CW_AFTER_PUBLISH};
  for(unsigned i=0;i<sizeof(checkpoints)/sizeof(checkpoints[0]);i++) {
    faults.interrupt_at=checkpoints[i];
    int rc=dnd_contiguous_begin(&writer,root,path,sizeof(payload),ops);
    if(checkpoints[i]==DND_CW_AFTER_RESERVE) {
      assert(rc==-EINTR);
    } else {
      assert(rc==0);
      assert(dnd_contiguous_write(&writer,payload,sizeof(payload))==0);
      assert(dnd_contiguous_complete(&writer)==-EINTR);
    }
    assert(!writer.active && present(journal));
    assert(dnd_contiguous_recover(root)==0);
    assert(!present(journal));
    if(checkpoints[i]==DND_CW_AFTER_PUBLISH) {
      file_data(path,payload,sizeof(payload));
      assert(unlink(path)==0);
    } else assert(!present(path));
  }
  faults.interrupt_at=0;
  /* Reject an untrusted journal without destroying user files. */
  fd=open(journal,O_CREAT|O_EXCL|O_WRONLY,0600);
  assert(fd>=0);
  assert(write(fd,"broken",6)==6);
  assert(close(fd)==0);
  assert(dnd_contiguous_recover(root)<0);
  assert(present(journal) && !present(path));
  assert(unlink(journal)==0);
  assert(rmdir(root)==0);
  puts("contiguous write reservation, verification and recovery passed");
  return 0;
}
