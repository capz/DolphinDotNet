#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static DndManagedHeap heap;
static int selections, selection_result;
static DndString *text(const char *value) { return dnd_string_from_utf8(&heap,value); }
int FAT_getAttr(const char *path) { (void)path;return 0; }
int FAT_setAttr(const char *path,unsigned attributes) { (void)path;(void)attributes;return 0; }
bool dnd_gc_mount(int device) { return device==3; }
bool dnd_gc_mount_disc(int format) { return format==1||format==2; }
bool dnd_gc_mount_disc_image(const char *path) { (void)path;return true; }
bool dnd_gc_mount_volume(const DndMountConfiguration *request,DndMountConfiguration *effective) {
  *effective=*request;effective->partition_lba=2048;effective->filesystem=1;return true;
}
void dnd_gc_unmount(int device) { (void)device; }
bool dnd_gc_storage_unmount(int device) { (void)device;return true; }
int dnd_gc_storage_presence(int device) { (void)device;return 1; }
void dnd_gc_storage_invalidate(int device) { (void)device; }
int dnd_gc_contiguous_extents(int device,const char *path,unsigned *extents) {
  assert(device==6);assert(strstr(path,".__dndcw_"));*extents=1;return 0;
}
int dnd_gc_select_gcloader_disc_image(const char *path) {
  assert(!strcmp(path,"gcloader:/new.iso"));selections++;return selection_result;
}
static void expect_failure(const char *path,DndExceptionKind kind) {
  DndEhFrame frame;dnd_eh_push(&frame);
  if(!setjmp(frame.environment)) {
    dnd_storage_select_gcloader_disc_image(text(path));assert(!"selection should fail");
  }
  dnd_eh_pop(&frame);assert(dnd_exception_kind()==kind);dnd_exception_clear();
}
static void write_fixture(const char *path,const char *value) {
  FILE *file=fopen(path,"wb");assert(file);size_t size=strlen(value);
  assert(fwrite(value,1,size,file)==size);assert(!fclose(file));
}
static void unchanged(const char *path,const char *value) {
  char bytes[32];FILE *file=fopen(path,"rb");assert(file);
  size_t size=fread(bytes,1,sizeof(bytes),file);assert(!ferror(file)&&!fclose(file));
  assert(size==strlen(value)&&!memcmp(bytes,value,size));
}
int main(void) {
  static unsigned char memory[256*1024];dnd_managed_heap_init(&heap,memory,sizeof(memory));
  char root[]="/tmp/dnd-gcloader-storage-XXXXXX";assert(mkdtemp(root));assert(!chdir(root));
  assert(!mkdir("gcloader:",0700)&&!mkdir("sd:",0700));
  write_fixture("gcloader:/old.iso","old verified disc");
  write_fixture("gcloader:/new.iso","new verified disc");
  expect_failure("gcloader:/new.iso",DND_EXCEPTION_DRIVE_NOT_FOUND);assert(!selections);
  assert(dnd_storage_mount(6)&&dnd_storage_mount(2));
  expect_failure("new.iso",DND_EXCEPTION_ARGUMENT);assert(!selections);
  expect_failure("sd:/new.iso",DND_EXCEPTION_ARGUMENT);assert(!selections);
  assert(dnd_storage_mount_disc(2));
  expect_failure("gcloader:/new.iso",DND_EXCEPTION_IO);assert(!selections);
  dnd_storage_unmount(3);
  int writer=dnd_fs_open(text("sd:/writer.bin"),2,2,0);assert(writer>0);
  expect_failure("gcloader:/new.iso",DND_EXCEPTION_IO);assert(!selections);dnd_fs_close(writer);
  int contiguous=dnd_fs_contiguous_begin(text("gcloader:/pending.bin"),512);assert(contiguous>0);
  expect_failure("gcloader:/new.iso",DND_EXCEPTION_IO);assert(!selections);
  dnd_fs_contiguous_abort(contiguous);
  selection_result=-EIO;
  expect_failure("gcloader:/new.iso",DND_EXCEPTION_IO);assert(selections==1);
  unchanged("gcloader:/old.iso","old verified disc");unchanged("gcloader:/new.iso","new verified disc");
  selection_result=0;dnd_storage_select_gcloader_disc_image(text("gcloader:/folder/../new.iso"));
  assert(!dnd_exception_pending()&&selections==2&&!dnd_storage_is_mounted(3));
  unchanged("gcloader:/old.iso","old verified disc");unchanged("gcloader:/new.iso","new verified disc");
  assert(!dnd_fs_open_handles());dnd_storage_unmount(6);dnd_storage_unmount(2);
  assert(!unlink("gcloader:/old.iso")&&!unlink("gcloader:/new.iso")&&!unlink("sd:/writer.bin"));
  assert(!rmdir("gcloader:")&&!rmdir("sd:")&&!chdir("/")&&!rmdir(root));
  puts("GC Loader selection preconditions, failure retention and success without file mutation passed.");
  return 0;
}
