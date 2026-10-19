#include "dnd_fst.h"
#include <sys/iosupport.h>
#include <ogc/dvd.h>
#include <ogc/dvdlow.h>
#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

bool dnd_gc_mount_disc(int);
bool dnd_gc_mount_disc_image(const char *);
void dnd_gc_unmount_disc(void);
static unsigned char bytes[4096];
static const devoptab_t *registered;
static bool reject_registration, fail_read;
static int cover=DVD_COVER_OPEN, mount_result, mount_calls, iso_mounts, iso_unmounts;
int __io_gcdvd;
static int drive_ready;
void DVD_Init(void) {}
int DVD_CheckDisk(void) { return drive_ready; }
int DVD_LowGetCoverStatus(void) { return cover; }
int DVD_Mount(void) { mount_calls++; return mount_result; }
s32 DVD_ReadAbs(dvdcmdblk *cmd,void *output,s32 count,s64 offset) {
  (void)cmd;
  assert(!((uintptr_t)output%32) && !(offset%2048) && count==2048);
  if(fail_read || offset<0 || (uint64_t)offset+count>sizeof(bytes))return -1;
  memcpy(output,bytes+offset,(size_t)count);return count;
}
bool ISO9660_Mount(const char *name,void *iface) {
  assert(!strcmp(name,"dvd") && iface==&__io_gcdvd);iso_mounts++;return true;
}
void ISO9660_Unmount(const char *name) { assert(!strcmp(name,"dvd"));iso_unmounts++; }
int AddDevice(const devoptab_t *device) {
  assert(!registered && !strcmp(device->name,"dvd"));
  if(reject_registration)return -1;
  registered=device;return 3;
}
int RemoveDevice(const char *name) { assert(!strcmp(name,"dvd:"));registered=NULL;return 0; }
static void put(unsigned at,uint32_t v) {
  for(int i=0;i<4;i++)bytes[at+i]=(unsigned char)(v>>(24-8*i));
}
static void prepare(void) {
  memset(bytes,0,sizeof(bytes));
  memcpy(bytes,"TEST01",6);memcpy(bytes+0x20,"Test disc",10);
  put(0x1c,0xc2339f3d);put(0x424,0x500);put(0x428,72);
  put(0x500,0x01000000);put(0x508,4);
  put(0x50c,0x01000001);put(0x510,0);put(0x514,3);
  put(0x518,5);put(0x51c,0x800);put(0x520,5);
  put(0x524,10);put(0x528,0x900);put(0x52c,3);
  memcpy(bytes+0x530,"\0dir\0data\0top\0",14);
  memcpy(bytes+0x800,"hello",5);memcpy(bytes+0x900,"abc",3);
}
static void access_files(void) {
  assert(registered);
  const devoptab_t *fs=registered;
  struct _reent r={0};struct stat st;
  uint8_t header[0x440];
  assert(dnd_gc_fst_copy_header(header) && !memcmp(header,bytes,sizeof(header)));
  assert(fs->stat_r(&r,"dvd:/",&st)==0 && S_ISDIR(st.st_mode));
  assert(fs->stat_r(&r,"dvd:/dir/data",&st)==0 && S_ISREG(st.st_mode) && st.st_size==5);
  assert(fs->stat_r(&r,"dvd:/missing",&st)==-1 && r._errno==ENOENT);
  void *file=calloc(1,(size_t)fs->structSize);assert(file);
  assert(fs->open_r(&r,file,"dvd:/dir/data",O_WRONLY,0)==-1 && r._errno==EROFS);
  assert(fs->open_r(&r,file,"dvd:/dir/data",O_RDONLY|O_TRUNC,0)==-1 && r._errno==EROFS);
  assert(fs->open_r(&r,file,"dvd:/dir",O_RDONLY,0)==-1 && r._errno==EISDIR);
  assert(fs->open_r(&r,file,"dvd:/dir/data",O_RDONLY,0)!=-1);
  char output[16]={0};
  assert(fs->read_r(&r,file,output,2)==2 && !memcmp(output,"he",2));
  assert(fs->read_r(&r,file,output,sizeof(output))==3 && !memcmp(output,"llo",3));
  assert(fs->read_r(&r,file,output,1)==0);
  assert(fs->seek_r(&r,file,-4,SEEK_CUR)==1);
  assert(fs->read_r(&r,file,output,1)==1 && output[0]=='e');
  assert(fs->seek_r(&r,file,-1,SEEK_END)==4);
  assert(fs->read_r(&r,file,output,1)==1 && output[0]=='o');
  assert(fs->seek_r(&r,file,-1,SEEK_SET)==-1 && r._errno==EINVAL);
  assert(fs->seek_r(&r,file,0,12345)==-1 && r._errno==EINVAL);
  assert(fs->fstat_r(&r,file,&st)==0 && st.st_size==5);
  assert(fs->close_r(&r,file)==0);free(file);
  DIR_ITER iterator={calloc(1,(size_t)fs->dirStateSize)};assert(iterator.dirStruct);
  char name[256];
  assert(fs->diropen_r(&r,&iterator,"dvd:/")==&iterator);
  assert(fs->dirnext_r(&r,&iterator,name,&st)==0 && !strcmp(name,"dir") && S_ISDIR(st.st_mode));
  assert(fs->dirnext_r(&r,&iterator,name,&st)==0 && !strcmp(name,"top") && st.st_size==3);
  assert(fs->dirnext_r(&r,&iterator,name,&st)==-1 && r._errno==ENOENT);
  assert(fs->dirreset_r(&r,&iterator)==0);
  assert(fs->dirnext_r(&r,&iterator,name,&st)==0 && !strcmp(name,"dir"));
  assert(fs->diropen_r(&r,&iterator,"dvd:/dir")==&iterator);
  assert(fs->dirnext_r(&r,&iterator,name,&st)==0 && !strcmp(name,"data"));
  assert(fs->dirnext_r(&r,&iterator,name,&st)==-1);
  assert(!fs->diropen_r(&r,&iterator,"dvd:/top") && r._errno==ENOTDIR);
  assert(!fs->diropen_r(&r,&iterator,"dvd:/missing") && r._errno==ENOENT);
  assert(fs->dirclose_r(&r,&iterator)==0);free(iterator.dirStruct);
}
int main(void) {
  prepare();
  /* DOL-only Dolphin starts with no disc. Never enter a blocking DVD_Mount. */
  assert(!dnd_gc_mount_disc(2) && mount_calls==0 && !registered);
  cover=DVD_COVER_RESET;
  assert(!dnd_gc_mount_disc(2) && mount_calls==0);
  cover=DVD_COVER_CLOSED;mount_result=-1;
  assert(!dnd_gc_mount_disc(2) && mount_calls==1 && !registered);
  mount_result=0;
  assert(dnd_gc_mount_disc(2));access_files();
  fail_read=true;
  struct _reent r={0};void *file=calloc(1,(size_t)registered->structSize);assert(file);
  assert(registered->open_r(&r,file,"dvd:/top",O_RDONLY,0)!=-1);
  char output[3];assert(registered->read_r(&r,file,output,3)==-1 && r._errno==EIO);
  free(file);fail_read=false;dnd_gc_unmount_disc();
  assert(!registered && !dnd_gc_fst_copy_header((uint8_t[0x440]){0}));
  drive_ready=1;int previous_mounts=mount_calls;
  assert(dnd_gc_mount_disc(2));access_files();
  assert(mount_calls==previous_mounts); /* Refresh reuses the ready drive. */
  dnd_gc_unmount_disc();
  cover=DVD_COVER_OPEN;assert(!dnd_gc_mount_disc(2));
  cover=DVD_COVER_CLOSED;
  assert(dnd_gc_mount_disc(2));assert(mount_calls==previous_mounts+1);
  dnd_gc_unmount_disc();previous_mounts=mount_calls;
  drive_ready=0; /* A changed disc/drive error must take the mount path again. */
  assert(dnd_gc_mount_disc(2));assert(mount_calls==previous_mounts+1);
  dnd_gc_unmount_disc();
  assert(dnd_gc_mount_disc(1) && iso_mounts==1);
  dnd_gc_unmount_disc();assert(iso_unmounts==1);
  char path[]="/tmp/dnd-mount-XXXXXX";int fd=mkstemp(path);assert(fd>=0);
  assert(write(fd,bytes,sizeof(bytes))==sizeof(bytes));close(fd);
  reject_registration=true;
  assert(!dnd_gc_mount_disc_image(path) && !registered);
  reject_registration=false;
  for(int i=0;i<20;i++) {
    assert(dnd_gc_mount_disc_image(path));access_files();
    assert(!dnd_gc_mount_disc_image(path)); /* No replacement of a live volume. */
    dnd_gc_unmount_disc();assert(!registered);
  }
  /* Failure must not retain a device/file/table, and a valid remount still works. */
  assert(!dnd_gc_mount_disc_image("/nonexistent/dnd-image.iso"));
  fd=open(path,O_WRONLY|O_TRUNC);assert(fd>=0);assert(write(fd,"bad",3)==3);close(fd);
  assert(!dnd_gc_mount_disc_image(path) && !registered);
  assert(dnd_gc_mount_disc(2));access_files();dnd_gc_unmount_disc();
  unlink(path);
  puts("Mounted ISO/disc devoptab reads, seeks, listings, errors, cleanup and no-media startup passed.");
}
