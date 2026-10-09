#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

ssize_t __real_read(int fd, void *data, size_t count);
ssize_t __real_write(int fd, const void *data, size_t count);
int __real_close(int fd);
static volatile int read_error, write_error, close_error;
ssize_t __wrap_read(int fd, void *data, size_t count) {
  if (read_error) {
    errno = read_error;
    read_error = 0;
    return -1;
  }
  return __real_read(fd, data, count > 7 ? 7 : count);
}
ssize_t __wrap_write(int fd, const void *data, size_t count) {
  if (write_error) {
    errno = write_error;
    write_error = 0;
    return -1;
  }
  return __real_write(fd, data, count > 5 ? 5 : count);
}
int __wrap_close(int fd) {
  int rc = __real_close(fd);
  if (close_error) {
    errno = close_error;
    close_error = 0;
    return -1;
  }
  return rc;
}
int main(void) {
  char root[] = "/tmp/dnd-storage-fault-XXXXXX", native[1024];
  assert(mkdtemp(root));
  assert(setenv("DND_STORAGE_ROOT", root, 1) == 0);
  snprintf(native, sizeof(native), "%s/sd", root);
  assert(mkdir(native, 0700) == 0);
  static unsigned char memory[128 * 1024];
  DndManagedHeap heap;
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  dnd_gc_set_stress(true);
  DndObject *path = NULL, *bytes = NULL, *result = NULL;
  DndObject **slots[] = {&path, &bytes, &result};
  DndGcFrame frame;
  dnd_gc_frame_push(&frame, slots, 3);
  path = (DndObject *)dnd_string_from_utf8(&heap, "sd:/fault.bin");
  bytes = (DndObject *)dnd_managed_array_new_typed(&heap, 71, 1, &DND_TYPE_BYTE,
                                                   false);
  memset(((DndArray *)bytes)->data, 42, 71);
  assert(dnd_storage_mount(2));
  write_error = EINTR;
  dnd_fs_write_all((DndString *)path, (DndArray *)bytes);
  assert(!dnd_exception_pending() && dnd_fs_open_handles() == 0);
  read_error = EINTR;
  result = (DndObject *)dnd_fs_read_all(&heap, (DndString *)path);
  assert(!dnd_exception_pending() && ((DndArray *)result)->length == 71);
  for (unsigned i = 0; i < 71; i++)
    assert(((DndArray *)result)->data[i] == 42);
  read_error = EIO;
  dnd_fs_read_all(&heap, (DndString *)path);
  assert(dnd_exception_kind() == DND_EXCEPTION_IO &&
         dnd_fs_open_handles() == 0);
  dnd_exception_clear();
  write_error = ENOSPC;
  dnd_fs_write_all((DndString *)path, (DndArray *)bytes);
  assert(dnd_exception_kind() == DND_EXCEPTION_IO &&
         dnd_fs_open_handles() == 0);
  dnd_exception_clear();
  write_error = ENOSPC;
  close_error = EACCES;
  dnd_fs_write_all((DndString *)path, (DndArray *)bytes);
  assert(dnd_exception_kind() == DND_EXCEPTION_IO &&
         dnd_fs_open_handles() == 0);
  dnd_exception_clear();
  close_error = EIO;
  dnd_fs_write_all((DndString *)path, (DndArray *)bytes);
  assert(dnd_exception_kind() == DND_EXCEPTION_IO &&
         dnd_fs_open_handles() == 0);
  dnd_exception_clear();
  dnd_exception_throw(DND_EXCEPTION_IO, "Original read error");
  DndObject *original = dnd_exception_object();
  dnd_exception_begin_catch();
  dnd_exception_throw(DND_EXCEPTION_UNAUTHORIZED, "Cleanup error");
  assert(original->type == &DND_TYPE_IO_EXCEPTION);
  dnd_exception_clear();
  dnd_exception_throw_object(original);
  assert(dnd_exception_matches(&DND_TYPE_IO_EXCEPTION));
  dnd_exception_clear();
  dnd_fs_delete((DndString *)path, false);
  dnd_storage_unmount(2);
  dnd_gc_frame_pop(&frame);
  snprintf(native, sizeof(native), "%s/sd", root);
  assert(rmdir(native) == 0 && rmdir(root) == 0);
  puts("storage short-I/O and failure cleanup passed");
}
