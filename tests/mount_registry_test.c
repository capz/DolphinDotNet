#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
static void expect_unmount_busy(int d) {
  DndEhFrame frame;dnd_eh_push(&frame);
  if(!setjmp(frame.environment)){dnd_storage_unmount(d);assert(!"unmount should be busy");}
  dnd_eh_pop(&frame);assert(dnd_exception_kind()==DND_EXCEPTION_IO);dnd_exception_clear();
}
static DndManagedHeap heap;
static DndString *text(const char *s){return dnd_string_from_utf8(&heap,s);}
#ifdef DND_GAMECUBE_STORAGE
int FAT_getAttr(const char *path){(void)path;return 0;}
int FAT_setAttr(const char *path,unsigned attributes){(void)path;(void)attributes;return 0;}
int dnd_gc_contiguous_extents(int d,const char *path,unsigned *extents){(void)d;(void)path;(void)extents;return -ENOTSUP;}
static int physical_presence[7]={1,1,1,1,1,1,1};
static int backend_calls, backend_unmounts[7];
static bool fail_image, fail_volume;
static bool fail_unmount;
bool dnd_gc_mount(int d){assert(d==3);backend_calls++;return true;}
bool dnd_gc_mount_disc(int format){assert(format==1||format==2);backend_calls++;return true;}
bool dnd_gc_mount_disc_image(const char *path){assert(strstr(path,"game.iso"));backend_calls++;return !fail_image;}
bool dnd_gc_mount_volume(const DndMountConfiguration *request,DndMountConfiguration *effective) {
  backend_calls++;if(fail_volume)return false;
  *effective=*request;
  if(effective->partition_lba<0)effective->partition_lba=2048;
  if(!effective->filesystem)effective->filesystem=1;
  return true;
}
void dnd_gc_unmount(int d){backend_unmounts[d]++;}
bool dnd_gc_storage_unmount(int d){if(fail_unmount)return false;dnd_gc_unmount(d);return true;}
int dnd_gc_storage_presence(int d){return physical_presence[d];}
void dnd_gc_storage_invalidate(int d){(void)d;}
#endif
int main(void) {
  static unsigned char memory[256*1024];dnd_managed_heap_init(&heap,memory,sizeof(memory));
  char root[]="/tmp/dnd-registry-XXXXXX",path[1024],removed[1024];assert(mkdtemp(root));
  assert(!setenv("DND_STORAGE_ROOT",root,1));
#ifdef DND_GAMECUBE_STORAGE
  assert(!chdir(root));assert(!mkdir("sd:",0700));strcpy(path,"sd:");strcpy(removed,"removed:");
#else
  snprintf(path,sizeof(path),"%s/sd",root);snprintf(removed,sizeof(removed),"%s/removed",root);assert(!mkdir(path,0700));
#endif
  DndMountInfo first,current;DndMountChange event;
  assert(dnd_storage_last_mount_device()==-1);
  assert(!dnd_storage_get_mount_info(4,&first)&&!dnd_storage_get_mount_info(5,&first));
  assert(dnd_storage_get_mount_info(2,&first)&&first.state==0&&first.configuration.kind==0);
  assert(dnd_storage_remount(2)==1);
  assert(dnd_storage_mount(2));assert(dnd_storage_get_mount_info(2,&first));
  assert(first.state==1&&first.configuration.kind==1&&first.mount_id>0&&first.last_mount_sequence>0);
  assert(first.configuration.cache_pages==4&&first.configuration.sectors_per_page==8);
  assert(dnd_storage_last_mount_device()==2);
#ifdef DND_GAMECUBE_STORAGE
  fail_unmount=true;expect_unmount_busy(2);fail_unmount=false;
  assert(dnd_storage_mount_valid(2,first.mount_id));
#endif
  assert(dnd_storage_mount(2));assert(dnd_storage_get_mount_info(2,&current));
  assert(current.mount_id==first.mount_id&&current.change_sequence==first.change_sequence);
  assert(dnd_storage_next_mount_change(&event)&&event.reason==1&&event.info.last_mount_sequence==first.last_mount_sequence);
  assert(!dnd_storage_next_mount_change(&event));
  DndMountConfiguration configuration=first.configuration;configuration.cache_pages=8;
  assert(dnd_storage_mount_configuration(&configuration)==4);
  DndString *file=text("entry.bin");int handle=dnd_storage_open_mounted(2,first.mount_id,file,2,2,0);assert(handle>0);
  expect_unmount_busy(2);assert(dnd_storage_mount_valid(2,first.mount_id));dnd_fs_close(handle);
  dnd_storage_unmount(2);assert(dnd_storage_get_mount_info(2,&current)&&current.state==2);
  assert(current.configuration.kind==1&&current.last_mount_sequence==first.last_mount_sequence);
  assert(dnd_storage_last_mount_device()==2&&!dnd_storage_mount_valid(2,first.mount_id));
  assert(!dnd_storage_remount(2));assert(dnd_storage_get_mount_info(2,&current));
  assert(current.mount_id!=first.mount_id&&current.last_mount_sequence>first.last_mount_sequence);
  DndEhFrame frame;dnd_eh_push(&frame);
  if(!setjmp(frame.environment)){dnd_storage_open_mounted(2,first.mount_id,file,3,1,1);assert(!"stale generation reopened");}
  dnd_eh_pop(&frame);assert(dnd_exception_kind()==DND_EXCEPTION_DRIVE_NOT_FOUND);dnd_exception_clear();
  handle=dnd_fs_open_utf8_native("sd:/entry.bin",3,1,1);assert(handle>0);
  int directory=dnd_fs_dir_open(text("sd:/"));assert(directory>0);
#ifdef DND_GAMECUBE_STORAGE
  physical_presence[2]=0;
#else
  assert(!rename(path,removed));
#endif
  dnd_storage_poll();assert(dnd_storage_get_mount_info(2,&current)&&current.state==3);
  assert(current.configuration.kind==1&&!dnd_storage_is_mounted(2));
  dnd_eh_push(&frame);
  if(!setjmp(frame.environment)){dnd_fs_length(handle);assert(!"removed file handle remained usable");}
  dnd_eh_pop(&frame);assert(dnd_exception_kind()==DND_EXCEPTION_DRIVE_NOT_FOUND);dnd_exception_clear();
  dnd_fs_close(handle);dnd_fs_dir_close(directory);assert(!dnd_exception_pending()&&!dnd_fs_open_handles());
#ifdef DND_GAMECUBE_STORAGE
  physical_presence[2]=1;
#else
  assert(!rename(removed,path));
#endif
  assert(!dnd_storage_remount(2));
#ifdef DND_GAMECUBE_STORAGE
  /* Legacy optical defaults are different and both must be remembered. */
  assert(dnd_storage_mount(3));dnd_storage_get_mount_info(3,&current);assert(current.configuration.disc_format==1);
  dnd_storage_unmount(3);assert(dnd_storage_mount_disc(2));dnd_storage_get_mount_info(3,&current);
  assert(current.configuration.disc_format==2&&current.configuration.kind==2);dnd_storage_unmount(3);
  assert(dnd_storage_mount_disc_image(text("sd:/folder/../game.iso")));
  dnd_storage_get_mount_info(3,&current);configuration=current.configuration;
  assert(configuration.kind==3&&!strcmp(configuration.image_path,"sd:/game.iso"));
  assert(configuration.source_device==2&&configuration.source_partition_lba==2048&&configuration.source_filesystem==1);
  expect_unmount_busy(2);dnd_storage_unmount(3);dnd_storage_unmount(2);
  int calls=backend_calls;assert(!dnd_storage_remount(3)&&backend_calls==calls+2);
  assert(dnd_storage_is_mounted(2)&&dnd_storage_is_mounted(3));
  physical_presence[2]=0;dnd_storage_poll();
  assert(!dnd_storage_is_mounted(2)&&!dnd_storage_is_mounted(3));
  dnd_storage_get_mount_info(3,&current);assert(current.state==3&&!strcmp(current.configuration.image_path,"sd:/game.iso"));
  physical_presence[2]=1;assert(!dnd_storage_remount(3));dnd_storage_unmount(3);dnd_storage_unmount(2);
  fail_image=true;assert(dnd_storage_remount(3)==2);assert(!dnd_storage_is_mounted(2));
  dnd_storage_get_mount_info(3,&current);assert(current.configuration.kind==3);fail_image=false;
  fail_volume=true;assert(dnd_storage_remount(3)==2);fail_volume=false;
  assert(!dnd_storage_remount(2));
  configuration.cache_pages=8; /* A conflicting source is never replaced. */
  DndMountConfiguration other=first.configuration;other.cache_pages=8;
  dnd_storage_unmount(2);assert(!dnd_storage_mount_configuration(&other));
  assert(dnd_storage_mount_configuration(&configuration)==4&&dnd_storage_is_mounted(2));
  dnd_storage_unmount(2);assert(!dnd_storage_remount(2));
#endif
  /* Overflow is observable and drops older queued deltas after resync. */
  for(int i=0;i<20;i++){dnd_storage_unmount(2);assert(!dnd_storage_remount(2));}
  assert(dnd_storage_next_mount_change(&event)&&event.reason==4&&event.info.configuration.device==-1);
  assert(!dnd_storage_next_mount_change(&event));
  dnd_fs_delete(text("sd:/entry.bin"),false);dnd_storage_unmount(2);
  assert(!rmdir(path));
#ifdef DND_GAMECUBE_STORAGE
  assert(!chdir("/"));
#endif
  assert(!rmdir(root));puts("Mount registry, retained configuration, generations, events and dependency lifecycle passed.");
}
