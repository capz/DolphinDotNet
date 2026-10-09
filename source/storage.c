#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/stat.h>
#include <unistd.h>

#define PATH_CAP 1024
#define HANDLE_CAP 32
static const char *prefixes[6] = {"carda", "cardb", "sd", "dvd", "mca", "mcb"};
static bool mounted[6];
static unsigned card_users[2];
static char roots[6][PATH_CAP];
static char cwd[PATH_CAP] = "sd:/";
typedef struct {
  int token, fd, access, share;
  int64_t append_origin;
  char path[PATH_CAP];
} FileHandle;
typedef struct {
  int token;
  DIR *dir;
  char path[PATH_CAP];
} DirectoryHandle;
static FileHandle files[HANDLE_CAP];
static DirectoryHandle directories[HANDLE_CAP];
static int next_token = 1;
#ifdef DND_GAMECUBE_STORAGE
bool dnd_gc_mount(int device);
void dnd_gc_unmount(int device);
bool dnd_gc_card_mount(int device, const char *game, const char *company);
int dnd_gc_card_metadata(int device, const char *name, bool metadata,
                         bool banner, bool icon);
int dnd_gc_card_read(int device, const char *name, void *data, int capacity);
int dnd_gc_card_write(int device, const char *name, const void *data,
                      int length);
int dnd_gc_card_delete(int device, const char *name);
int dnd_gc_card_entries(int device, char names[127][33]);
#endif
static void fail(int error, const char *message) {
  DndExceptionKind kind = DND_EXCEPTION_IO;
  if (error == ENOENT)
    kind = DND_EXCEPTION_FILE_NOT_FOUND;
  else if (error == ENOTDIR)
    kind = DND_EXCEPTION_DIRECTORY_NOT_FOUND;
  else if (error == EACCES || error == EPERM || error == EROFS)
    kind = DND_EXCEPTION_UNAUTHORIZED;
  else if (error == ENOMEM)
    kind = DND_EXCEPTION_OUT_OF_MEMORY;
  else if (error == ENODEV)
    kind = DND_EXCEPTION_DRIVE_NOT_FOUND;
  else if (error == ENAMETOOLONG)
    kind = DND_EXCEPTION_PATH_TOO_LONG;
  else if (error == EINVAL)
    kind = DND_EXCEPTION_ARGUMENT;
  else if (error == ENOSYS)
    kind = DND_EXCEPTION_NOT_SUPPORTED;
  dnd_exception_throw(kind, message);
}
static bool utf8(DndString *s, char out[PATH_CAP]) {
  if (!s) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL, "Path is null.");
    return false;
  }
  size_t n = 0;
  for (uint32_t i = 0; i < s->length; i++) {
    uint32_t c = s->chars[i];
    if (!c) {
      fail(EINVAL, "Embedded NUL.");
      return false;
    }
    if (c >= 0xd800 && c <= 0xdbff) {
      if (++i >= s->length || s->chars[i] < 0xdc00 || s->chars[i] > 0xdfff) {
        fail(EINVAL, "Invalid UTF-16.");
        return false;
      }
      c = 0x10000 + ((c - 0xd800) << 10) + (s->chars[i] - 0xdc00);
    } else if (c >= 0xdc00 && c <= 0xdfff) {
      fail(EINVAL, "Invalid UTF-16.");
      return false;
    }
    unsigned bytes = c < 128 ? 1 : c < 2048 ? 2 : c < 65536 ? 3 : 4;
    if (n + bytes >= PATH_CAP) {
      fail(ENAMETOOLONG, "Path is too long.");
      return false;
    }
    if (bytes == 1)
      out[n++] = (char)c;
    else {
      out[n++] = (char)((bytes == 2   ? 0xc0
                         : bytes == 3 ? 0xe0
                                      : 0xf0) |
                        (c >> (6 * (bytes - 1))));
      for (unsigned j = bytes - 1; j > 0; j--)
        out[n++] = (char)(0x80 | ((c >> (6 * (j - 1))) & 63));
    }
  }
  out[n] = 0;
  return true;
}
static int device_name(const char *s, size_t len) {
  for (int i = 0; i < 6; i++)
    if (strlen(prefixes[i]) == len && !strncmp(s, prefixes[i], len))
      return i;
  return -1;
}
static int normalize(const char *input, char out[PATH_CAP]) {
  char combined[PATH_CAP];
  size_t len = strlen(input);
  if (!len) {
    fail(EINVAL, "Empty path.");
    return -1;
  }
  const char *colon = strchr(input, ':');
  int device;
  if (colon) {
    device = device_name(input, (size_t)(colon - input));
    if (device < 0 || (colon[1] != '/' && colon[1] != '\\')) {
      fail(EINVAL, "Invalid volume path.");
      return -1;
    }
    if (len >= PATH_CAP) {
      fail(ENAMETOOLONG, "Path too long.");
      return -1;
    }
    memcpy(combined, input, len + 1);
  } else {
    const char *base = cwd;
    size_t b = strlen(base);
    if (input[0] == '/' || input[0] == '\\') {
      b = (size_t)(strchr(base, ':') - base) + 1;
    }
    if (b + len + 2 > PATH_CAP) {
      fail(ENAMETOOLONG, "Path too long.");
      return -1;
    }
    memcpy(combined, base, b);
    size_t at = b;
    if (input[0] != '/' && input[0] != '\\' && combined[at - 1] != '/')
      combined[at++] = '/';
    memcpy(combined + at, input, len + 1);
    device = device_name(base, (size_t)(strchr(base, ':') - base));
  }
  size_t root = strlen(prefixes[device]) + 2;
  snprintf(out, PATH_CAP, "%s:/", prefixes[device]);
  size_t n = root;
  size_t positions[64];
  int depth = 0;
  const char *p = strchr(combined, ':') + 1;
  while (*p) {
    while (*p == '/' || *p == '\\')
      p++;
    const char *start = p;
    while (*p && *p != '/' && *p != '\\')
      p++;
    size_t l = (size_t)(p - start);
    if (!l || (l == 1 && start[0] == '.'))
      continue;
    if (l == 2 && start[0] == '.' && start[1] == '.') {
      if (!depth) {
        fail(EINVAL, "Path escapes volume root.");
        return -1;
      }
      n = positions[--depth];
      out[n] = 0;
      continue;
    }
    if (l > 255 || depth == 64 || memchr(start, ':', l)) {
      fail(EINVAL, "Invalid path component.");
      return -1;
    }
    positions[depth++] = n;
    if (n > root)
      out[n++] = '/';
    if (n + l >= PATH_CAP) {
      fail(ENAMETOOLONG, "Path too long.");
      return -1;
    }
    memcpy(out + n, start, l);
    n += l;
    out[n] = 0;
  }
  return device;
}
static int resolve(DndString *path, char canonical[PATH_CAP],
                   char native[PATH_CAP]) {
  char raw[PATH_CAP];
  if (!utf8(path, raw))
    return -1;
  int device = normalize(raw, canonical);
  if (device < 0)
    return -1;
  if (device >= 4) {
    fail(ENOSYS, "Memory cards require the save API.");
    return -1;
  }
  if (!mounted[device]) {
    fail(ENODEV, "Volume is not mounted.");
    return -1;
  }
#ifdef DND_GAMECUBE_STORAGE
  strcpy(native, canonical);
#else
  const char *tail = strchr(canonical, ':') + 1;
  int n = snprintf(native, PATH_CAP, "%s%s", roots[device], tail);
  if (n < 0 || n >= PATH_CAP) {
    fail(ENAMETOOLONG, "Path too long.");
    return -1;
  }
#endif
  return device;
}
static bool valid_device(int d) {
  if (d < 0 || d >= 6) {
    fail(EINVAL, "Invalid device.");
    return false;
  }
  return true;
}
bool dnd_storage_is_mounted(int d) { return valid_device(d) && mounted[d]; }
bool dnd_storage_mount(int d) {
  if (!valid_device(d))
    return false;
  if (d >= 4) {
    fail(ENOSYS, "Use MemoryCard.Mount with a save identity.");
    return false;
  }
  if (mounted[d])
    return true;
#ifdef DND_GAMECUBE_STORAGE
  mounted[d] = dnd_gc_mount(d);
#else
  const char *root = getenv("DND_STORAGE_ROOT");
  if (!root)
    root = ".";
  int n = snprintf(roots[d], PATH_CAP, "%s/%s", root, prefixes[d]);
  if (n < 0 || n >= PATH_CAP) {
    fail(ENAMETOOLONG, "Host root too long.");
    return false;
  }
  struct stat st;
  mounted[d] = stat(roots[d], &st) == 0 && S_ISDIR(st.st_mode);
#endif
  return mounted[d];
}
int dnd_fs_open_handles(void) {
  int n = 0;
  for (int i = 0; i < HANDLE_CAP; i++) {
    n += files[i].token != 0;
    n += directories[i].token != 0;
  }
  return n;
}
static bool busy(const char *path) {
  for (int i = 0; i < HANDLE_CAP; i++)
    if (files[i].token && !strcasecmp(files[i].path, path))
      return true;
  return false;
}
void dnd_storage_unmount(int d) {
  if (!valid_device(d) || !mounted[d])
    return;
  if (d >= 4 && card_users[d - 4] > 1) {
    card_users[d - 4]--;
    return;
  }
  if (d >= 4)
    card_users[d - 4] = 0;
  for (int i = 0; i < HANDLE_CAP; i++)
    if ((files[i].token &&
         !strncmp(files[i].path, prefixes[d], strlen(prefixes[d]))) ||
        (directories[i].token &&
         !strncmp(directories[i].path, prefixes[d], strlen(prefixes[d])))) {
      fail(EBUSY, "Volume has open handles.");
      return;
    }
#ifdef DND_GAMECUBE_STORAGE
  dnd_gc_unmount(d);
#endif
  mounted[d] = false;
}
DndString *dnd_fs_full_path(DndManagedHeap *heap, DndString *path) {
  char raw[PATH_CAP], out[PATH_CAP];
  if (!utf8(path, raw) || normalize(raw, out) < 0)
    return NULL;
  return dnd_string_from_utf8(heap, out);
}
DndString *dnd_storage_path(DndManagedHeap *heap, int d, DndString *path) {
  if (!valid_device(d))
    return NULL;
  if (d >= 4) {
    fail(ENOSYS, "Memory cards do not have directories.");
    return NULL;
  }
  char raw[PATH_CAP], combined[PATH_CAP], out[PATH_CAP];
  if (!utf8(path, raw))
    return NULL;
  if (strchr(raw, ':') || raw[0] == '/' || raw[0] == '\\') {
    fail(EINVAL, "Device paths must be relative.");
    return NULL;
  }
  int n = snprintf(combined, PATH_CAP, "%s:/%s", prefixes[d], raw);
  if (n < 0 || n >= PATH_CAP) {
    fail(ENAMETOOLONG, "Path too long.");
    return NULL;
  }
  if (normalize(combined, out) < 0)
    return NULL;
  return dnd_string_from_utf8(heap, out);
}
bool dnd_fs_exists(DndString *path, bool directory) {
  // Exists never raises, including invalid UTF-16, missing volumes and invalid
  // paths.
  DndEhFrame frame;
  dnd_eh_push(&frame);
  if (setjmp(frame.environment) != 0) {
    dnd_eh_pop(&frame);
    dnd_exception_clear();
    return false;
  }
  char canonical[PATH_CAP], native[PATH_CAP];
  struct stat st;
  int d = resolve(path, canonical, native);
  bool exists = d >= 0 && stat(native, &st) == 0 &&
                (directory ? S_ISDIR(st.st_mode) : S_ISREG(st.st_mode));
  dnd_eh_pop(&frame);
  if (dnd_exception_pending())
    dnd_exception_clear();
  return exists;
}
static FileHandle *handle(int token) {
  for (int i = 0; i < HANDLE_CAP; i++)
    if (token && files[i].token == token)
      return &files[i];
  dnd_exception_throw(DND_EXCEPTION_OBJECT_DISPOSED, "File handle is closed.");
  return NULL;
}
static int path_error(int error, const char *path) {
  if (error != ENOENT)
    return error;
  char parent[PATH_CAP];
  strcpy(parent, path);
  char *slash = strrchr(parent, '/');
  if (!slash)
    return error;
  if (slash == strchr(parent, ':') + 1)
    slash[1] = 0;
  else
    *slash = 0;
  struct stat st;
  return stat(parent, &st) != 0 || !S_ISDIR(st.st_mode) ? ENOTDIR : ENOENT;
}
static ssize_t aligned_read(int fd, void *data, size_t count) {
  _Alignas(32) unsigned char buffer[4096];
  if (count > sizeof(buffer))
    count = sizeof(buffer);
  ssize_t rc = read(fd, buffer, count);
  if (rc > 0)
    memcpy(data, buffer, (size_t)rc);
  return rc;
}
static ssize_t aligned_write(int fd, const void *data, size_t count) {
  _Alignas(32) unsigned char buffer[4096];
  if (count > sizeof(buffer))
    count = sizeof(buffer);
  memcpy(buffer, data, count);
  return write(fd, buffer, count);
}
int dnd_fs_open(DndString *path, int mode, int access, int share) {
  char canonical[PATH_CAP], native[PATH_CAP];
  int d = resolve(path, canonical, native);
  if (d < 0)
    return 0;
  if (mode < 1 || mode > 6 || access < 1 || access > 3 || share < 0 ||
      share > 3 || (mode == 6 && access != 2) ||
      ((mode == 1 || mode == 2 || mode == 5) && access == 1)) {
    fail(EINVAL, "Invalid file mode/access/share.");
    return 0;
  }
  if (d == 3 && access != 1) {
    fail(EROFS, "DVD filesystem is read-only.");
    return 0;
  }
  int slot = -1;
  for (int i = 0; i < HANDLE_CAP; i++) {
    if (!files[i].token && slot < 0)
      slot = i;
    else if (files[i].token && !strcasecmp(files[i].path, canonical) &&
             ((access & ~files[i].share) || (files[i].access & ~share))) {
      fail(EBUSY, "Sharing violation.");
      return 0;
    }
  }
  if (slot < 0 || next_token == INT_MAX) {
    fail(EMFILE, "Too many handles.");
    return 0;
  }
  int flags = access == 1 ? O_RDONLY : access == 2 ? O_WRONLY : O_RDWR;
  if (mode == 1)
    flags |= O_CREAT | O_EXCL;
  else if (mode == 2)
    flags |= O_CREAT | O_TRUNC;
  else if (mode == 4)
    flags |= O_CREAT;
  else if (mode == 5)
    flags |= O_TRUNC;
  else if (mode == 6)
    flags |= O_CREAT | O_APPEND;
  struct stat st;
  if (mode == 5 && stat(native, &st) != 0) {
    fail(path_error(errno, native), "Truncate requires an existing file.");
    return 0;
  }
  int fd = open(native, flags, 0666);
  if (fd < 0) {
    fail(path_error(errno, native), "Cannot open file.");
    return 0;
  }
  if (fstat(fd, &st) != 0 || !S_ISREG(st.st_mode)) {
    int e = errno;
    close(fd);
    fail(e ? e : EACCES, "Not a regular file.");
    return 0;
  }
  files[slot] =
      (FileHandle){.token = next_token++,
                   .fd = fd,
                   .access = access,
                   .share = share,
                   .append_origin = mode == 6 ? (int64_t)st.st_size : -1};
  strcpy(files[slot].path, canonical);
  if (mode == 6 && lseek(fd, 0, SEEK_END) < 0) {
    int e = errno;
    files[slot].token = 0;
    close(fd);
    fail(e, "Cannot seek append stream.");
    return 0;
  }
  return files[slot].token;
}
static bool range(DndArray *a, int offset, int count) {
  if (!a) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL, "Buffer is null.");
    return false;
  }
  if (a->element_size != 1 || a->elements_are_references || offset < 0 ||
      count < 0 || (uint32_t)offset > a->length ||
      (uint32_t)count > a->length - (uint32_t)offset) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_OUT_OF_RANGE,
                        "Invalid buffer range.");
    return false;
  }
  return true;
}
int dnd_fs_read(int token, DndArray *data, int offset, int count) {
  FileHandle *h = handle(token);
  if (!h || !range(data, offset, count))
    return 0;
  if (!(h->access & 1)) {
    fail(ENOSYS, "Stream is not readable.");
    return 0;
  }
  ssize_t n;
  do {
    n = aligned_read(h->fd, data->data + offset, (size_t)count);
  } while (n < 0 && errno == EINTR);
  if (n < 0) {
    fail(errno, "Read failed.");
    return 0;
  }
  return (int)n;
}
void dnd_fs_write(int token, DndArray *data, int offset, int count) {
  FileHandle *h = handle(token);
  if (!h || !range(data, offset, count))
    return;
  if (!(h->access & 2)) {
    fail(ENOSYS, "Stream is not writable.");
    return;
  }
  int done = 0;
  while (done < count) {
    ssize_t n = aligned_write(h->fd, data->data + offset + done,
                              (size_t)(count - done));
    if (n < 0 && errno == EINTR)
      continue;
    if (n <= 0) {
      fail(n < 0 ? errno : EIO, "Write failed.");
      return;
    }
    done += (int)n;
  }
}
int64_t dnd_fs_seek(int token, int64_t offset, int origin) {
  FileHandle *h = handle(token);
  if (!h)
    return 0;
  if (origin < 0 || origin > 2) {
    fail(EINVAL, "Invalid seek origin.");
    return 0;
  }
  off_t converted = (off_t)offset;
  if ((int64_t)converted != offset) {
    fail(EINVAL, "Offset exceeds native range.");
    return 0;
  }
  int64_t base = origin == 0   ? 0
                 : origin == 1 ? (int64_t)lseek(h->fd, 0, SEEK_CUR)
                               : dnd_fs_length(token);
  if (base < 0 || (offset > 0 && base > INT64_MAX - offset) ||
      (offset < 0 && offset < -base) ||
      (h->append_origin >= 0 && base + offset < h->append_origin)) {
    fail(EINVAL, "Seek outside supported range.");
    return 0;
  }
  if ((int64_t)(off_t)(base + offset) != base + offset) {
    fail(EINVAL, "Position exceeds native range.");
    return 0;
  }
  off_t position = lseek(h->fd, converted,
                         origin == 0   ? SEEK_SET
                         : origin == 1 ? SEEK_CUR
                                       : SEEK_END);
  if (position < 0) {
    fail(errno, "Seek failed.");
    return 0;
  }
  return (int64_t)position;
}
int64_t dnd_fs_length(int token) {
  FileHandle *h = handle(token);
  if (!h)
    return 0;
  struct stat st;
  if (fstat(h->fd, &st)) {
    fail(errno, "Length query failed.");
    return 0;
  }
  return (int64_t)st.st_size;
}
void dnd_fs_set_length(int token, int64_t length) {
  FileHandle *h = handle(token);
  if (!h)
    return;
  if (!(h->access & 2) || length < 0 || (int64_t)(off_t)length != length ||
      (h->append_origin >= 0 && length < h->append_origin)) {
    fail(EINVAL, "Invalid stream length.");
    return;
  }
  if (ftruncate(h->fd, (off_t)length))
    fail(errno, "Truncate failed.");
}
void dnd_fs_flush(int token) {
  FileHandle *h = handle(token);
  if (h && (h->access & 2) && fsync(h->fd))
    fail(errno, "Flush failed.");
}
void dnd_fs_close(int token) {
  FileHandle *h = handle(token);
  if (!h)
    return;
  int fd = h->fd;
  h->token = 0;
  if (close(fd))
    fail(errno, "Close failed.");
}
DndArray *dnd_fs_read_all(DndManagedHeap *heap, DndString *path) {
  char canonical[PATH_CAP], native[PATH_CAP];
  if (resolve(path, canonical, native) < 0)
    return NULL;
  struct stat st;
  if (stat(native, &st)) {
    fail(errno, "Cannot stat file.");
    return NULL;
  }
  if (!S_ISREG(st.st_mode)) {
    fail(EACCES, "Not a regular file.");
    return NULL;
  }
  if (st.st_size < 0 || (uint64_t)st.st_size > INT_MAX ||
      (uint64_t)st.st_size > heap->capacity) {
    fail(ENOMEM, "File exceeds managed heap limit.");
    return NULL;
  }
  DndArray *result = dnd_managed_array_new_typed(heap, (uint32_t)st.st_size, 1,
                                                 &DND_TYPE_BYTE, false);
  if (!result)
    return NULL;
  DndObject **slots[] = {(DndObject **)&result};
  DndGcFrame frame;
  dnd_gc_frame_push(&frame, slots, 1);
  int token = dnd_fs_open(path, 3, 1, 1);
  if (!token) {
    dnd_gc_frame_pop(&frame);
    return NULL;
  }
  int done = 0, e = 0;
  FileHandle *h = handle(token);
  while (done < (int)result->length) {
    ssize_t n = aligned_read(h->fd, result->data + done,
                             result->length - (uint32_t)done);
    if (n < 0 && errno == EINTR)
      continue;
    if (n < 0) {
      e = errno;
      break;
    }
    if (!n)
      break;
    done += (int)n;
  }
  uint8_t extra;
  ssize_t more;
  do {
    more = aligned_read(h->fd, &extra, 1);
  } while (more < 0 && errno == EINTR);
  if (more != 0 && !e)
    e = more < 0 ? errno : EIO;
  int fd = h->fd;
  h->token = 0;
  if (close(fd) && !e)
    e = errno;
  result->length = (uint32_t)done;
  dnd_gc_frame_pop(&frame);
  if (e) {
    fail(e, "File changed or read failed.");
    return NULL;
  }
  return result;
}
void dnd_fs_write_all(DndString *path, DndArray *data) {
  if (!data) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL, "Data is null.");
    return;
  }
  if (!range(data, 0, (int)data->length))
    return;
  int token = dnd_fs_open(path, 2, 2, 0);
  if (!token)
    return;
  FileHandle *h = handle(token);
  int e = 0;
  size_t done = 0;
  while (done < data->length) {
    ssize_t n = aligned_write(h->fd, data->data + done, data->length - done);
    if (n < 0 && errno == EINTR)
      continue;
    if (n <= 0) {
      e = n < 0 ? errno : EIO;
      break;
    }
    done += (size_t)n;
  }
  int fd = h->fd;
  h->token = 0;
  if (close(fd) && !e)
    e = errno;
  if (e)
    fail(e, "File write/close failed.");
}
void dnd_fs_delete(DndString *path, bool directory) {
  char canonical[PATH_CAP], native[PATH_CAP];
  int d = resolve(path, canonical, native);
  if (d < 0)
    return;
  if (d == 3) {
    fail(EROFS, "DVD is read-only.");
    return;
  }
  if (strchr(canonical, ':')[2] == 0) {
    fail(EACCES, "Cannot delete a volume root.");
    return;
  }
  if (busy(canonical)) {
    fail(EBUSY, "File is open.");
    return;
  }
  int rc = directory ? rmdir(native) : unlink(native);
  if (rc && !(errno == ENOENT && !directory))
    fail(errno, "Delete failed.");
}
void dnd_fs_move(DndString *source, DndString *destination) {
  char a[PATH_CAP], an[PATH_CAP], b[PATH_CAP], bn[PATH_CAP];
  int ad = resolve(source, a, an), bd = resolve(destination, b, bn);
  if (ad < 0 || bd < 0)
    return;
  if (ad == 3 || bd == 3) {
    fail(EROFS, "DVD is read-only.");
    return;
  }
  if (ad != bd) {
    fail(ENOSYS, "Cross-volume move is not implemented.");
    return;
  }
  if (busy(a) || busy(b)) {
    fail(EBUSY, "File is open.");
    return;
  }
  struct stat st;
  if (stat(bn, &st) == 0) {
    fail(EEXIST, "Destination exists.");
    return;
  }
  if (errno != ENOENT) {
    fail(errno, "Cannot query destination.");
    return;
  }
  if (rename(an, bn))
    fail(errno, "Move failed.");
}
void dnd_fs_mkdir(DndString *path) {
  char canonical[PATH_CAP], native[PATH_CAP];
  int d = resolve(path, canonical, native);
  if (d < 0)
    return;
  if (d == 3) {
    fail(EROFS, "DVD is read-only.");
    return;
  }
  size_t start;
#ifdef DND_GAMECUBE_STORAGE
  start = strlen(prefixes[d]) + 2;
#else
  start = strlen(roots[d]) + 1;
#endif
  size_t length = strlen(native);
  for (size_t i = start; i <= length; i++)
    if (native[i] == '/' || !native[i]) {
      char c = native[i];
      native[i] = 0;
      struct stat st;
      if (stat(native, &st) == 0) {
        if (!S_ISDIR(st.st_mode)) {
          fail(ENOTDIR, "A parent is a file.");
          return;
        }
      } else if (errno != ENOENT || mkdir(native, 0777)) {
        fail(errno, "Create directory failed.");
        return;
      }
      native[i] = c;
    }
}
int dnd_fs_dir_open(DndString *path) {
  char canonical[PATH_CAP], native[PATH_CAP];
  if (resolve(path, canonical, native) < 0)
    return 0;
  int slot = -1;
  for (int i = 0; i < HANDLE_CAP; i++)
    if (!directories[i].token) {
      slot = i;
      break;
    }
  if (slot < 0 || next_token == INT_MAX) {
    fail(EMFILE, "Too many directory handles.");
    return 0;
  }
  DIR *dir = opendir(native);
  if (!dir) {
    fail(errno == ENOENT ? ENOTDIR : errno, "Cannot open directory.");
    return 0;
  }
  directories[slot].token = next_token++;
  directories[slot].dir = dir;
  strcpy(directories[slot].path, canonical);
  return directories[slot].token;
}
static DirectoryHandle *dir_handle(int token) {
  for (int i = 0; i < HANDLE_CAP; i++)
    if (token && directories[i].token == token)
      return &directories[i];
  dnd_exception_throw(DND_EXCEPTION_OBJECT_DISPOSED,
                      "Directory handle is closed.");
  return NULL;
}
DndString *dnd_fs_dir_next(DndManagedHeap *heap, int token, int kind) {
  DirectoryHandle *h = dir_handle(token);
  if (!h)
    return NULL;
  for (;;) {
    errno = 0;
    struct dirent *entry = readdir(h->dir);
    if (!entry) {
      if (errno)
        fail(errno, "Directory enumeration failed.");
      return NULL;
    }
    if (!strcmp(entry->d_name, ".") || !strcmp(entry->d_name, ".."))
      continue;
    char canonical[PATH_CAP], native[PATH_CAP];
    int n =
        snprintf(canonical, PATH_CAP, "%s%s%s", h->path,
                 h->path[strlen(h->path) - 1] == '/' ? "" : "/", entry->d_name);
    if (n < 0 || n >= PATH_CAP) {
      fail(ENAMETOOLONG, "Entry path too long.");
      return NULL;
    }
#ifdef DND_GAMECUBE_STORAGE
    strcpy(native, canonical);
#else
    int d =
        device_name(canonical, (size_t)(strchr(canonical, ':') - canonical));
    n = snprintf(native, PATH_CAP, "%s%s", roots[d],
                 strchr(canonical, ':') + 1);
    if (n < 0 || n >= PATH_CAP) {
      fail(ENAMETOOLONG, "Entry path too long.");
      return NULL;
    }
#endif
    struct stat st;
    if (stat(native, &st)) {
      fail(errno, "Entry disappeared.");
      return NULL;
    }
    if ((kind == 1 && !S_ISREG(st.st_mode)) ||
        (kind == 2 && !S_ISDIR(st.st_mode)))
      continue;
    return dnd_string_from_utf8(heap, canonical);
  }
}
void dnd_fs_dir_close(int token) {
  DirectoryHandle *h = dir_handle(token);
  if (!h)
    return;
  DIR *dir = h->dir;
  h->token = 0;
  if (closedir(dir))
    fail(errno, "Directory close failed.");
}
DndString *dnd_fs_getcwd(DndManagedHeap *heap) {
  return dnd_string_from_utf8(heap, cwd);
}
void dnd_fs_setcwd(DndString *path) {
  char canonical[PATH_CAP], native[PATH_CAP];
  if (resolve(path, canonical, native) < 0)
    return;
  struct stat st;
  if (stat(native, &st) || !S_ISDIR(st.st_mode)) {
    fail(ENOTDIR, "Directory unavailable.");
    return;
  }
  strcpy(cwd, canonical);
}

static char card_game[2][5], card_company[2][3];
static bool card_arguments(int d, DndString *name, char out[PATH_CAP]) {
  if (!valid_device(d))
    return false;
  if (d < 4) {
    fail(EINVAL, "Not a memory card slot.");
    return false;
  }
  if (!mounted[d]) {
    fail(ENODEV, "Memory card is not mounted.");
    return false;
  }
  if (!utf8(name, out))
    return false;
  size_t n = strlen(out);
  if (!n || n > 32) {
    fail(EINVAL, "Save name must contain 1 to 32 ASCII characters.");
    return false;
  }
  for (size_t i = 0; i < n; i++)
    if (!((out[i] >= 'a' && out[i] <= 'z') ||
          (out[i] >= 'A' && out[i] <= 'Z') ||
          (out[i] >= '0' && out[i] <= '9') || out[i] == '_' || out[i] == '-')) {
      fail(EINVAL, "Invalid save name.");
      return false;
    }
  return true;
}
bool dnd_card_mount(int d, DndString *game, DndString *company) {
  if (!valid_device(d))
    return false;
  if (d < 4) {
    fail(EINVAL, "Not a memory card slot.");
    return false;
  }
  char g[PATH_CAP], c[PATH_CAP];
  if (!utf8(game, g) || !utf8(company, c))
    return false;
  if (strlen(g) != 4 || strlen(c) != 2) {
    fail(EINVAL,
         "Expected four-character game and two-character company codes.");
    return false;
  }
  for (int i = 0; i < 4; i++)
    if (!((g[i] >= 'A' && g[i] <= 'Z') || (g[i] >= '0' && g[i] <= '9'))) {
      fail(EINVAL, "Invalid game code.");
      return false;
    }
  for (int i = 0; i < 2; i++)
    if (!((c[i] >= 'A' && c[i] <= 'Z') || (c[i] >= '0' && c[i] <= '9'))) {
      fail(EINVAL, "Invalid company code.");
      return false;
    }
  int slot = d - 4;
  if (mounted[d]) {
    if (strcmp(g, card_game[slot]) || strcmp(c, card_company[slot])) {
      fail(EBUSY, "Card already mounted for a different identity.");
      return false;
    }
    card_users[slot]++;
    return true;
  }
#ifdef DND_GAMECUBE_STORAGE
  mounted[d] = dnd_gc_card_mount(d, g, c);
#else
  const char *root = getenv("DND_STORAGE_ROOT");
  if (!root)
    root = ".";
  int n = snprintf(roots[d], PATH_CAP, "%s/%s", root, prefixes[d]);
  if (n < 0 || n >= PATH_CAP) {
    fail(ENAMETOOLONG, "Host root too long.");
    return false;
  }
  struct stat st;
  mounted[d] = stat(roots[d], &st) == 0 && S_ISDIR(st.st_mode);
#endif
  if (mounted[d]) {
    strcpy(card_game[slot], g);
    strcpy(card_company[slot], c);
    card_users[slot] = 1;
  }
  return mounted[d];
}
#ifndef DND_GAMECUBE_STORAGE
static bool card_host_path(int d, const char *name, char out[PATH_CAP]) {
  int n = snprintf(out, PATH_CAP, "%s/%s%s_%s", roots[d], card_game[d - 4],
                   card_company[d - 4], name);
  if (n < 0 || n >= PATH_CAP) {
    fail(ENAMETOOLONG, "Save path too long.");
    return false;
  }
  return true;
}
static int card_backend_read(int d, const char *name, void *data,
                             int capacity) {
  char path[PATH_CAP];
  if (!card_host_path(d, name, path))
    return -EINVAL;
  int fd = open(path, O_RDONLY);
  if (fd < 0)
    return -errno;
  struct stat st;
  if (fstat(fd, &st) || st.st_size > INT_MAX) {
    int e = errno ? errno : EIO;
    close(fd);
    return -e;
  }
  if (!data) {
    int n = (int)st.st_size;
    close(fd);
    return n;
  }
  int length = capacity < (int)st.st_size ? capacity : (int)st.st_size,
      done = 0, error = 0;
  while (done < length) {
    ssize_t n = read(fd, (char *)data + done, (size_t)(length - done));
    if (n < 0 && errno == EINTR)
      continue;
    if (n <= 0) {
      error = n < 0 ? errno : EIO;
      break;
    }
    done += (int)n;
  }
  if (close(fd) && !error)
    error = errno;
  return error ? -error : done;
}
static int card_backend_write(int d, const char *name, const void *data,
                              int length) {
  char path[PATH_CAP];
  if (!card_host_path(d, name, path))
    return -EINVAL;
  int fd = open(path, O_CREAT | O_TRUNC | O_WRONLY, 0666);
  if (fd < 0)
    return -errno;
  int done = 0, error = 0;
  while (done < length) {
    ssize_t n = write(fd, (const char *)data + done, (size_t)(length - done));
    if (n < 0 && errno == EINTR)
      continue;
    if (n <= 0) {
      error = n < 0 ? errno : EIO;
      break;
    }
    done += (int)n;
  }
  if (fsync(fd) && !error)
    error = errno;
  if (close(fd) && !error)
    error = errno;
  return error ? -error : 0;
}
#else
#define card_backend_read dnd_gc_card_read
#define card_backend_write dnd_gc_card_write
#endif
static uint32_t read_be32(const uint8_t *p) {
  return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) |
         ((uint32_t)p[2] << 8) | p[3];
}
static void write_be32(uint8_t *p, uint32_t n) {
  p[0] = (uint8_t)(n >> 24);
  p[1] = (uint8_t)(n >> 16);
  p[2] = (uint8_t)(n >> 8);
  p[3] = (uint8_t)n;
}
static bool save_header(int d, const char *n, uint32_t *length,
                        uint32_t *offset) {
  uint8_t header[32];
  int rc = card_backend_read(d, n, header, 32);
  if (rc != 32 || memcmp(header, "DNDSAVE1", 8)) {
    fail(rc < 0 ? -rc : EIO, "Invalid save container.");
    return false;
  }
  *length = read_be32(header + 8);
  *offset = read_be32(header + 12);
  if (!*offset)
    *offset = 32;
  int physical = card_backend_read(d, n, NULL, 0);
  if (physical < 32 || *offset < 32 || *offset > (uint32_t)physical ||
      *length > (uint32_t)physical - *offset || *length > INT_MAX) {
    fail(physical < 0 ? -physical : EIO, "Invalid save size.");
    return false;
  }
  return true;
}
int dnd_card_length(int d, DndString *name) {
  char n[PATH_CAP];
  uint32_t length, offset;
  if (!card_arguments(d, name, n) || !save_header(d, n, &length, &offset))
    return 0;
  return (int)length;
}
DndArray *dnd_card_read(DndManagedHeap *heap, int d, DndString *name) {
  char n[PATH_CAP];
  uint32_t length, offset;
  if (!card_arguments(d, name, n) || !save_header(d, n, &length, &offset))
    return NULL;
  if ((uint64_t)length + offset > heap->capacity ||
      (uint64_t)length + offset > INT_MAX) {
    fail(ENOMEM, "Save exceeds managed heap.");
    return NULL;
  }
  DndArray *result = dnd_managed_array_new_typed(heap, length + offset, 1,
                                                 &DND_TYPE_BYTE, false);
  if (!result)
    return NULL;
  int rc = card_backend_read(d, n, result->data, (int)(length + offset));
  if (rc != (int)(length + offset)) {
    fail(rc < 0 ? -rc : EIO, "Save is truncated.");
    return NULL;
  }
  memmove(result->data, result->data + offset, length);
  result->length = length;
  return result;
}
static void write_save(int d, DndString *name, DndArray *data, DndString *title,
                       DndString *comment, DndArray *banner, DndArray *icon,
                       bool metadata) {
  char n[PATH_CAP], t[PATH_CAP], c[PATH_CAP];
  if (!card_arguments(d, name, n) ||
      !range(data, 0, data ? (int)data->length : 0))
    return;
  size_t offset = 32;
  if (metadata) {
    if (!utf8(title, t) || !utf8(comment, c))
      return;
    if (strlen(t) > 31 || strlen(c) > 31 ||
        (banner && (banner->element_size != 1 || banner->length != 6144)) ||
        (icon && (icon->element_size != 1 || icon->length != 2048))) {
      fail(EINVAL,
           "Save text must fit 31 UTF-8 bytes; images must be tiled RGB5A3.");
      return;
    }
    offset += 64 + (banner ? 6144 : 0) + (icon ? 2048 : 0);
  }
  if (data->length > INT_MAX - 8192 - offset) {
    fail(ENOMEM, "Save is too large.");
    return;
  }
  size_t length = (data->length + offset + 8191) & ~(size_t)8191;
  uint8_t *container = calloc(1, length);
  if (!container) {
    fail(ENOMEM, "Cannot allocate save buffer.");
    return;
  }
  memcpy(container, "DNDSAVE1", 8);
  write_be32(container + 8, data->length);
  write_be32(container + 12, (uint32_t)offset);
  if (metadata) {
    memcpy(container + 32, t, strlen(t));
    memcpy(container + 64, c, strlen(c));
    size_t image = 96;
    if (banner) {
      memcpy(container + image, banner->data, 6144);
      image += 6144;
    }
    if (icon)
      memcpy(container + image, icon->data, 2048);
  }
  memcpy(container + offset, data->data, data->length);
  int rc = card_backend_write(d, n, container, (int)length);
  free(container);
#ifdef DND_GAMECUBE_STORAGE
  if (rc >= 0)
    rc = dnd_gc_card_metadata(d, n, metadata, banner != NULL, icon != NULL);
#endif
  if (rc < 0)
    fail(-rc, "Memory card write failed.");
}
void dnd_card_write(int d, DndString *name, DndArray *data) {
  write_save(d, name, data, NULL, NULL, NULL, NULL, false);
}
void dnd_card_write_save(int d, DndString *name, DndArray *data,
                         DndString *title, DndString *comment, DndArray *banner,
                         DndArray *icon) {
  write_save(d, name, data, title, comment, banner, icon, true);
}
void dnd_card_delete(int d, DndString *name) {
  char n[PATH_CAP];
  if (!card_arguments(d, name, n))
    return;
#ifdef DND_GAMECUBE_STORAGE
  int rc = dnd_gc_card_delete(d, n);
  if (rc < 0 && rc != -ENOENT)
    fail(-rc, "Save delete failed.");
#else
  char path[PATH_CAP];
  if (!card_host_path(d, n, path))
    return;
  if (unlink(path) && errno != ENOENT)
    fail(errno, "Save delete failed.");
#endif
}
DndArray *dnd_card_entries(DndManagedHeap *heap, int d) {
  if (!valid_device(d) || d < 4 || !mounted[d]) {
    fail(ENODEV, "Memory card is not mounted.");
    return NULL;
  }
  char names[127][33];
  int count = 0, error = 0;
#ifdef DND_GAMECUBE_STORAGE
  count = dnd_gc_card_entries(d, names);
  if (count < 0) {
    fail(-count, "Cannot enumerate saves.");
    return NULL;
  }
#else
  DIR *dir = opendir(roots[d]);
  if (!dir) {
    fail(errno, "Cannot enumerate saves.");
    return NULL;
  }
  char identity[8];
  snprintf(identity, sizeof(identity), "%s%s_", card_game[d - 4],
           card_company[d - 4]);
  for (;;) {
    errno = 0;
    struct dirent *entry = readdir(dir);
    if (!entry) {
      if (errno)
        error = errno;
      break;
    }
    if (strncmp(entry->d_name, identity, 7))
      continue;
    size_t len = strlen(entry->d_name + 7);
    if (len > 32 || !len)
      continue;
    if (count == 127) {
      error = EIO;
      break;
    }
    memcpy(names[count++], entry->d_name + 7, len + 1);
  }
  if (closedir(dir) && !error)
    error = errno;
  if (error) {
    fail(error, "Cannot enumerate saves.");
    return NULL;
  }
#endif
  (void)error;
  DndArray *result = dnd_managed_array_new_typed(
      heap, (uint32_t)count, sizeof(DndObject *), &DND_TYPE_STRING, true);
  if (!result)
    return NULL;
  DndObject **slots[] = {(DndObject **)&result};
  DndGcFrame roots_frame;
  dnd_gc_frame_push(&roots_frame, slots, 1);
  for (int i = 0; i < count; i++) {
    DndString *name = dnd_string_from_utf8(heap, names[i]);
    if (!name) {
      dnd_gc_frame_pop(&roots_frame);
      return NULL;
    }
    dnd_array_store_ref(result, (uint32_t)i, (DndObject *)name);
  }
  dnd_gc_frame_pop(&roots_frame);
  return result;
}
