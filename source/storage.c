#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include "dnd_card_transaction.h"
#include "dnd_contiguous_write.h"
#include "dnd_file_optimize.h"
#ifdef DND_GAMECUBE_STORAGE
#include <fat.h>
#include "dnd_fst.h"
#endif
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/stat.h>
#include <sys/time.h>
#include <unistd.h>
#include <stdatomic.h>
#include <time.h>
#ifdef DND_GAMECUBE_STORAGE
#include <ogc/lwp_watchdog.h>
#endif
#include "dnd_async.h"
/* Optional when linking storage-only bootstrap/host fixtures. */
#ifdef __APPLE__
#define DND_OPTIONAL __attribute__((weak_import))
#else
#define DND_OPTIONAL __attribute__((weak))
#endif
extern void dnd_async_wait_external(void) DND_OPTIONAL;
extern int dnd_async_native_begin(void) DND_OPTIONAL;
extern void dnd_async_native_end(int) DND_OPTIONAL;
extern uint64_t dnd_buffer_io_begin(int,int,int) DND_OPTIONAL;
extern void dnd_buffer_io_end(int) DND_OPTIONAL;
static atomic_uint asset_io_busy;
static void storage_barrier(void) {
    while(atomic_load_explicit(&asset_io_busy,memory_order_acquire)) {
        if(dnd_async_wait_external)dnd_async_wait_external();
    }
}

#define PATH_CAP 1024
#define HANDLE_CAP 32
static const char *prefixes[7] = {"carda", "cardb", "sd", "dvd", "mca", "mcb", "gcloader"};
static bool mounted[7];
/* Opt-in is explicitly off for each program launch; the application may
 * enable automatic extent reservation/repair before initiating writes. */
static bool auto_defrag_enabled;
static int auto_defrag_min_bytes = 64 * 1024;
/* Process-local counters, incremented only by automatic write optimization.
 * No allocation or filesystem I/O is required to inspect them. */
static int64_t auto_stats[8];
static int auto_last_result=-1; /* -1=none; 0..3=optimizer; 4=reserved; 5=error */
int64_t dnd_storage_auto_defrag_stat(int kind) {
  storage_barrier();
  if(kind==8)return auto_last_result;
  return kind>=0 && kind<8?auto_stats[kind]:0;
}
void dnd_storage_auto_defrag_reset_stats(void) {
  storage_barrier();
  memset(auto_stats,0,sizeof(auto_stats));
  auto_last_result=-1;
}
bool dnd_storage_auto_defrag_enabled(void) {
  storage_barrier(); return auto_defrag_enabled; }
void dnd_storage_auto_defrag_set(bool enabled) {
  storage_barrier(); auto_defrag_enabled = enabled; }
int dnd_storage_auto_defrag_min_size(void) {
  storage_barrier(); return auto_defrag_min_bytes; }
void dnd_storage_auto_defrag_set_min_size(int minimum) {
  storage_barrier();
  if(minimum<0) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Invalid minimum file size.");return; }
  auto_defrag_min_bytes=minimum;
}
/* Keep backing mounted while its ISO is presented through dvd:/ */
static int iso_source_device = -1;
static unsigned card_users[2];
static char roots[7][PATH_CAP];
static char cwd[PATH_CAP] = "sd:/";
typedef struct {
  int token, fd, access, share, device;
  bool dirty;
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
/* One outstanding staged write per writable FAT/exFAT volume. */
static DndContiguousWriter contiguous_writers[7];
static int contiguous_tokens[7], next_contiguous_token = 1;
static void contiguous_root(int d,char path[PATH_CAP]) {
#ifdef DND_GAMECUBE_STORAGE
  snprintf(path,PATH_CAP,"%s:/",prefixes[d]);
#else
  strcpy(path,roots[d]);
#endif
}

#ifdef DND_GAMECUBE_STORAGE
bool dnd_gc_mount(int device);
bool dnd_gc_mount_disc(int format);
bool dnd_gc_mount_disc_image(const char *path);
void dnd_gc_unmount(int device);
bool dnd_gc_card_mount(int device, const char *game, const char *company);
int dnd_gc_card_metadata(int device, const char *name, bool metadata,
                         bool banner, bool icon);
int dnd_gc_card_read(int device, const char *name, void *data, int capacity);
int dnd_gc_card_write_save(int device,const char *name,const void *data,int length,bool metadata,bool banner,bool icon);
int dnd_gc_card_recover(int device);
int dnd_gc_card_delete(int device, const char *name);
int dnd_gc_card_entries(int device, char names[127][33]);
int dnd_gc_contiguous_extents(int device,const char *path,unsigned *extents);
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
  for (int i = 0; i < 7; i++)
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
  if (device == 4 || device == 5) {
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
  if (d < 0 || d >= 7) {
    fail(EINVAL, "Invalid device.");
    return false;
  }
  return true;
}
bool dnd_storage_is_mounted(int d) {
  storage_barrier(); return valid_device(d) && mounted[d]; }
bool dnd_storage_mount(int d) {
  storage_barrier();
  if (!valid_device(d))
    return false;
  if (d == 4 || d == 5) {
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
  if (mounted[d] && d != 3) {
    char root[PATH_CAP];
    contiguous_root(d,root);
    int recovery=dnd_contiguous_recover(root);
    if(!recovery)recovery=dnd_optimize_recover(root);
    if(recovery<0) {
#ifdef DND_GAMECUBE_STORAGE
      dnd_gc_unmount(d);
#endif
      mounted[d]=false;
      fail(-recovery,"An interrupted contiguous write needs recovery.");
    }
  }
  return mounted[d];
}
bool dnd_storage_mount_disc(int format) {
  storage_barrier();
  if(format!=1&&format!=2){fail(EINVAL,"Invalid disc format.");return false;}
  if(mounted[3]){fail(EBUSY,"Unmount the disc before selecting a format.");return false;}
#ifdef DND_GAMECUBE_STORAGE
  mounted[3]=dnd_gc_mount_disc(format);return mounted[3];
#else
  if(format==2){fail(ENOSYS,"Use the portable FST image harness on a host.");return false;}
  return dnd_storage_mount(3);
#endif
}
bool dnd_storage_mount_disc_image(DndString *path) {
  storage_barrier();
  char canonical[PATH_CAP], native[PATH_CAP];
  if (!path) {
    fail(EINVAL, "ISO image path is null.");
    return false;
  }
  /* The source is a fully-qualified path on an already-mounted volume. */
  char raw[PATH_CAP];
  if (!utf8(path, raw)) return false;
  if (!strchr(raw, ':') || raw[0] == '/' || raw[0] == '\\') {
    fail(EINVAL, "Specify a full mounted-volume ISO path.");
    return false;
  }
  if (mounted[3]) {
    fail(EBUSY, "Unmount dvd:/ before mounting an image.");
    return false;
  }
  int source = resolve(path, canonical, native);
  if (source < 0) return false;
  if (source == 3 || source == 4 || source == 5) {
    fail(EINVAL, "ISO source must be a mounted filesystem, not dvd or a memory card.");
    return false;
  }
#ifdef DND_GAMECUBE_STORAGE
  if (!dnd_gc_mount_disc_image(native)) return false;
  mounted[3] = true;
  iso_source_device = source;
  return true;
#else
  fail(ENOSYS, "Mounted GameCube image volumes require a GameCube target.");
  return false;
#endif
}
/* Same immutable 0x440 boot header for a native disc or an ISO backed dvd:/.
 * The FST parser snapshots these bytes when mounting the volume. */
DndArray *dnd_storage_disc_header(DndManagedHeap *heap) {
  storage_barrier();
  if (!mounted[3]) {
    fail(ENODEV, "Mount a GameCube disc at dvd:/ first.");
    return NULL;
  }
#ifdef DND_GAMECUBE_STORAGE
  uint8_t raw[0x440];
  if (!dnd_gc_fst_copy_header(raw)) {
    fail(ENOSYS, "Disc identity is available for GameCube FST mounts only.");
    return NULL;
  }
  DndArray *result = dnd_managed_array_new_typed(
      heap, sizeof(raw), 1, &DND_TYPE_BYTE, false);
  if (!result) return NULL;
  memcpy(result->data, raw, sizeof(raw));
  return result;
#else
  (void)heap;
  fail(ENOSYS, "Original GameCube disc identity requires the GameCube target.");
  return NULL;
#endif
}
int dnd_fs_open_handles(void) {
  storage_barrier();
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
  storage_barrier();
  if (!valid_device(d) || !mounted[d])
    return;
  if(contiguous_tokens[d]) {
    fail(EBUSY,"Volume has an unfinished contiguous file write.");
    return;
  }
  if (d == iso_source_device && mounted[3]) {
    fail(EBUSY, "Unmount the ISO-backed dvd volume before its source.");
    return;
  }
  if ((d == 4 || d == 5) && card_users[d - 4] > 1) {
    card_users[d - 4]--;
    return;
  }
  if (d == 4 || d == 5)
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
  if (d == 3) iso_source_device = -1;
}
void dnd_storage_shutdown(void) {
  storage_barrier();
  for(int d=0;d<7;d++) {
    if(contiguous_tokens[d]) {
      dnd_contiguous_abort(&contiguous_writers[d]);
      contiguous_tokens[d]=0;
    }
  }
  for (int i = 0; i < HANDLE_CAP; i++) {
    if (files[i].token) dnd_fs_close(files[i].token);
    if (directories[i].token) dnd_fs_dir_close(directories[i].token);
  }
  /* The ISO-backed dvd volume owns a file on another mounted device. */
  dnd_storage_unmount(3);
  card_users[0] = card_users[1] = 0;
  for (int d = 0; d < 7; d++) dnd_storage_unmount(d);
}
DndString *dnd_fs_full_path(DndManagedHeap *heap, DndString *path) {
  storage_barrier();
  char raw[PATH_CAP], out[PATH_CAP];
  if (!utf8(path, raw) || normalize(raw, out) < 0)
    return NULL;
  return dnd_string_from_utf8(heap, out);
}
bool dnd_fs_path_utf8_native(DndString *path, char *output, unsigned capacity) {
  storage_barrier();
  char raw[PATH_CAP], canonical[PATH_CAP];
  if (!output || !utf8(path, raw) || normalize(raw, canonical) < 0)
    return false;
  size_t length = strlen(canonical);
  if (length >= capacity) {
    fail(ENAMETOOLONG, "Path too long.");
    return false;
  }
  memcpy(output, canonical, length + 1);
  return true;
}
int dnd_fs_open_utf8_native(const char *path, int mode, int access, int share) {
  storage_barrier();
  if (!path) {
    fail(EINVAL, "Null native asset path.");
    return 0;
  }
  struct {
    DndObject object;
    uint32_t length;
    uint16_t chars[PATH_CAP];
  } text = {0};
  text.object.type = &DND_TYPE_STRING;
  const unsigned char *p = (const unsigned char *)path;
  while (*p) {
    uint32_t c = *p++;
    unsigned continuation = 0;
    uint32_t minimum = 0;
    if (c >= 0xc2 && c <= 0xdf) {
      c &= 31;
      continuation = 1;
      minimum = 0x80;
    } else if (c >= 0xe0 && c <= 0xef) {
      c &= 15;
      continuation = 2;
      minimum = 0x800;
    } else if (c >= 0xf0 && c <= 0xf4) {
      c &= 7;
      continuation = 3;
      minimum = 0x10000;
    } else if (c >= 0x80)
      goto invalid;
    for (unsigned i = 0; i < continuation; i++) {
      if ((*p & 0xc0) != 0x80)
        goto invalid;
      c = (c << 6) | (*p++ & 63);
    }
    if (c < minimum || c > 0x10ffff || (c >= 0xd800 && c <= 0xdfff))
      goto invalid;
    if (text.length + 2 >= PATH_CAP)
      goto invalid;
    if (c > 0xffff) {
      c -= 0x10000;
      text.chars[text.length++] = (uint16_t)(0xd800 + (c >> 10));
      text.chars[text.length++] = (uint16_t)(0xdc00 + (c & 1023));
    } else
      text.chars[text.length++] = (uint16_t)c;
  }
  return dnd_fs_open((DndString *)&text, mode, access, share);
invalid:
  fail(EINVAL, "Invalid UTF-8 native asset path.");
  return 0;
}
DndString *dnd_storage_path(DndManagedHeap *heap, int d, DndString *path) {
  storage_barrier();
  if (!valid_device(d))
    return NULL;
  if (d == 4 || d == 5) {
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
  storage_barrier();
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
  storage_barrier();
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
  int stat_error = fstat(fd, &st);
  if (stat_error != 0 || !S_ISREG(st.st_mode)) {
    int e = stat_error ? errno : EACCES;
    close(fd);
    fail(e ? e : EACCES, "Not a regular file.");
    return 0;
  }
  files[slot] =
      (FileHandle){.token = next_token++,
                   .fd = fd,
                   .access = access,
                   .share = share,
                   .device = d,
                   .dirty = mode == 2 || mode == 5,
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
  if (offset < 0 || count < 0) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_OUT_OF_RANGE,
                        "Negative buffer range.");
    return false;
  }
  if (a->element_size != 1 || a->elements_are_references ||
      (uint32_t)offset > a->length ||
      (uint32_t)count > a->length - (uint32_t)offset) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Invalid buffer range.");
    return false;
  }
  return true;
}
int dnd_fs_read(int token, DndArray *data, int offset, int count) {
  storage_barrier();
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
/* Native asset streaming uses the same mounted paths, sharing and aligned I/O. */
int64_t dnd_fs_length_native(int token) {
  storage_barrier();
  for (int i=0;i<HANDLE_CAP;i++) if(files[i].token==token && token>0) {
    struct stat st;return fstat(files[i].fd,&st)==0?(int64_t)st.st_size:-1;
  }
  return -1;
}
int dnd_fs_read_native(int token, void *data, int count) {
  storage_barrier();
  FileHandle *h = handle(token);
  if (!h) return -1;
  if (!data || count < 0 || !(h->access & 1)) { fail(EINVAL, "Invalid native read."); return -1; }
  ssize_t n;
  do { n = aligned_read(h->fd, data, (size_t)count); } while (n < 0 && errno == EINTR);
  if (n < 0) { fail(errno, "Read failed."); return -1; }
  return (int)n;
}
void dnd_fs_write(int token, DndArray *data, int offset, int count) {
  storage_barrier();
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
    h->dirty = true;
  }
}
int64_t dnd_fs_seek(int token, int64_t offset, int origin) {
  storage_barrier();
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
  storage_barrier();
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
  storage_barrier();
  FileHandle *h = handle(token);
  if (!h)
    return;
  if (!(h->access & 2)) {
    fail(ENOSYS, "Stream is not writable.");
    return;
  }
  if (length < 0) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_OUT_OF_RANGE,
                        "Negative stream length.");
    return;
  }
  if ((int64_t)(off_t)length != length ||
      (h->append_origin >= 0 && length < h->append_origin)) {
    fail(EINVAL, "Invalid stream length.");
    return;
  }
  if (ftruncate(h->fd, (off_t)length)) {
    fail(errno, "Truncate failed.");
    return;
  }
  h->dirty = true;
  off_t current = lseek(h->fd, 0, SEEK_CUR);
  if (current < 0 ||
      (current > (off_t)length && lseek(h->fd, (off_t)length, SEEK_SET) < 0))
    fail(errno, "Position update failed.");
}
void dnd_fs_flush(int token) {
  storage_barrier();
  FileHandle *h = handle(token);
  if (h && (h->access & 2) && fsync(h->fd))
    fail(errno, "Flush failed.");
}

/* Fast path: the existing libdvm allocator tries f_expand when size is
 * known; after a write, only fragmented files are copied. Do not perform
 * a full-volume scan or touch unrelated files. */
#define DND_AUTO_OPT_MIN_BYTES auto_defrag_min_bytes
#ifdef DND_GAMECUBE_STORAGE
static int auto_count_extents(void *context,const char *path,unsigned *extents) {
  return dnd_gc_contiguous_extents((int)(intptr_t)context,path,extents);
}
static int auto_copy_metadata(void *context,const char *original,
                              const char *stage,const struct stat *st) {
  (void)context;
  int attrs=FAT_getAttr(original);
  if(attrs<0)return -errno;
  struct timeval timestamps[2]={{st->st_atime,0},{st->st_mtime,0}};
  if(utimes(stage,timestamps)<0)return -errno;
  if(FAT_setAttr(stage,(unsigned)attrs)<0)return -errno;
  return 0;
}
#endif
static void auto_clean_file(const char *canonical,int device) {
  if(!auto_defrag_enabled)return;
#ifdef DND_GAMECUBE_STORAGE
  if(device<0 || device==3 || device==4 || device==5 ||
     !mounted[device] || contiguous_tokens[device] ||
     busy(canonical) || (device==iso_source_device && mounted[3]))
    return;
  /* Never optimize DolphinDotNet's own transaction artifacts. */
  const char *filename=strrchr(canonical,'/');
  if(filename && !strncmp(filename+1,".__dnd",6))return;
  struct stat st;
  if(stat(canonical,&st) || !S_ISREG(st.st_mode) ||
     st.st_size<DND_AUTO_OPT_MIN_BYTES)return;
  char root[PATH_CAP];
  contiguous_root(device,root);
  DndOptimizeOps ops={(void *)(intptr_t)device,auto_count_extents,NULL,
                       auto_copy_metadata};
  auto_stats[0]++;
  int rc=dnd_optimize_file(root,canonical,&ops);
  auto_last_result=rc<0?5:rc;
  if(rc==DND_OPT_REPAIRED) {auto_stats[2]++;auto_stats[7]+=(int64_t)st.st_size;}
  else if(rc==DND_OPT_ALREADY_CONTIGUOUS)auto_stats[3]++;
  else if(rc==DND_OPT_NO_CONTIGUOUS_SPACE)auto_stats[4]++;
  else if(rc==DND_OPT_UNSUPPORTED)auto_stats[5]++;
  else if(rc<0)auto_stats[6]++;
  if(rc<0) {
    /* A failed optimization is not allowed to fail an already completed
     * application write. Repair our own incomplete rename first, though. */
    int recovery=dnd_optimize_recover(root);
    if(recovery<0)fail(-recovery,
        "Automatic fragmentation repair needs recovery on the next mount.");
  }
#else
  (void)canonical;(void)device;
#endif
}
/* Manual inspection and repair never depend on the global opt-in policy.
 * They must not run while another managed handle still references the file:
 * a rename would leave that handle bound to the old allocation. */
int dnd_fs_defrag_analyze(DndString *path) {
  storage_barrier();
  char canonical[PATH_CAP],native[PATH_CAP];
  int d=resolve(path,canonical,native);
  if(d<0)return -1;
  if(d==3 || d==4 || d==5) {fail(ENOSYS,"Extent analysis requires FAT32/exFAT SD storage.");return -1;}
  if(busy(canonical) || contiguous_tokens[d]) {fail(EBUSY,"File is open or a contiguous write is active.");return -1;}
#ifdef DND_GAMECUBE_STORAGE
  unsigned extents=0;
  int rc=dnd_gc_contiguous_extents(d,native,&extents);
  if(rc<0) {fail(rc==-ENOTSUP?ENOSYS:-rc,"Cannot inspect FAT allocation.");return -1;}
  if(extents>INT_MAX) {fail(EFBIG,"Too many extents.");return -1;}
  return (int)extents;
#else
  fail(ENOSYS,"FAT extent inspection requires GameCube hardware.");
  return -1;
#endif
}
int dnd_fs_defrag_optimize(DndString *path) {
  storage_barrier();
  char canonical[PATH_CAP],native[PATH_CAP],root[PATH_CAP];
  int d=resolve(path,canonical,native);
  if(d<0)return -1;
  if(d==3 || d==4 || d==5) {fail(EROFS,"Optimization requires writable SD storage.");return -1;}
  if(busy(canonical) || contiguous_tokens[d] ||
     (d==iso_source_device && mounted[3])) {
    fail(EBUSY,"File is in use or the SD volume backs a mounted DVD.");
    return -1;
  }
#ifdef DND_GAMECUBE_STORAGE
  contiguous_root(d,root);
  DndOptimizeOps ops={(void *)(intptr_t)d,auto_count_extents,NULL,
                      auto_copy_metadata};
  int rc=dnd_optimize_file(root,native,&ops);
  if(rc<0) {fail(rc==-ENOTSUP?ENOSYS:-rc,"Cannot optimize file.");return -1;}
  return rc;
#else
  (void)root;
  fail(ENOSYS,"FAT optimization requires GameCube hardware.");
  return -1;
#endif
}
static int auto_contiguous_write(DndString *path,DndArray *data) {
  if(!auto_defrag_enabled)return 0;
#ifdef DND_GAMECUBE_STORAGE
  if(data->length<DND_AUTO_OPT_MIN_BYTES)return 0;
  char canonical[PATH_CAP],native[PATH_CAP],root[PATH_CAP];
  int device=resolve(path,canonical,native);
  if(device<0)return -1;
  if(device==3 || device==4 || device==5 || contiguous_tokens[device] ||
     busy(canonical) || (device==iso_source_device && mounted[3]))
    return 0;
  struct stat st;
  if(!stat(native,&st) || errno!=ENOENT)return 0;
  contiguous_root(device,root);
  DndContiguousWriter writer={0};
  DndContiguousOps ops={(void *)(intptr_t)device,auto_count_extents,NULL};
  auto_stats[0]++;
  int rc=dnd_contiguous_begin(&writer,root,native,data->length,ops);
  if(rc==-ENOSPC) {auto_stats[4]++;auto_last_result=DND_OPT_NO_CONTIGUOUS_SPACE;}
  else if(rc==-ENOTSUP) {auto_stats[5]++;auto_last_result=DND_OPT_UNSUPPORTED;}
  else if(rc<0) {auto_stats[6]++;auto_last_result=5;}
  /* Ordinary writes remain available when no contiguous run fits or the
   * filesystem cannot report extents. Never overwrite existing contents. */
  if(rc==-ENOSPC || rc==-ENOTSUP)return 0;
  if(rc<0){fail(-rc,"Cannot stage an automatically contiguous file.");return -1;}
  rc=dnd_contiguous_write(&writer,data->data,data->length);
  if(!rc)rc=dnd_contiguous_complete(&writer);
  if(rc<0) {
    auto_stats[6]++;
    auto_last_result=5;
    if(writer.active)dnd_contiguous_abort(&writer);
    fail(-rc,"Automatically contiguous file write failed.");
    return -1;
  }
  auto_stats[1]++;
  auto_stats[7]+=(int64_t)data->length;
  auto_last_result=4;
  return 1;
#else
  (void)path;(void)data;
  return 0;
#endif
}

void dnd_fs_close(int token) {
  storage_barrier();
  FileHandle *h = handle(token);
  if (!h)
    return;
  int fd=h->fd,device=h->device;
  bool dirty=h->dirty;
  char canonical[PATH_CAP];
  strcpy(canonical,h->path);
  h->token=0;
  if(close(fd)) {
    fail(errno,"Close failed.");
    return;
  }
  if(dirty && !dnd_exception_pending())auto_clean_file(canonical,device);
}
DndArray *dnd_fs_read_all(DndManagedHeap *heap, DndString *path) {
  storage_barrier();
  char canonical[PATH_CAP], native[PATH_CAP];
  if (resolve(path, canonical, native) < 0)
    return NULL;
  struct stat st;
  if (stat(native, &st)) {
    fail(path_error(errno, native), "Cannot stat file.");
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
  storage_barrier();
  if (!data) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL, "Data is null.");
    return;
  }
  if (!range(data, 0, (int)data->length))
    return;
  int optimized=auto_contiguous_write(path,data);
  if(optimized!=0)return;
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
  int fd=h->fd,device=h->device;
  char canonical[PATH_CAP];
  strcpy(canonical,h->path);
  h->token=0;
  if(close(fd) && !e)e=errno;
  if(e)fail(e,"File write/close failed.");
  else auto_clean_file(canonical,device);
}
void dnd_fs_delete(DndString *path, bool directory) {
  storage_barrier();
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
  storage_barrier();
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

static int contiguous_count(void *context,const char *path,unsigned *extents) {
#ifdef DND_GAMECUBE_STORAGE
  return dnd_gc_contiguous_extents((int)(intptr_t)context,path,extents);
#else
  (void)context;(void)path;(void)extents;
  return -ENOTSUP; /* Host filesystem extents are not GameCube FAT extents. */
#endif
}
static int contiguous_find(int token) {
  for(int d=0;d<7;d++)if(token>0 && contiguous_tokens[d]==token)return d;
  fail(EBADF,"Invalid contiguous file writer.");
  return -1;
}
int dnd_fs_contiguous_begin(DndString *path,int64_t length) {
  storage_barrier();
  char canonical[PATH_CAP],native[PATH_CAP],root[PATH_CAP];
  int d=resolve(path,canonical,native);
  if(d<0)return 0;
  if(d==3 || d==4 || d==5) {
    fail(EROFS,"Contiguous files require writable SD storage.");
    return 0;
  }
  if(length<0 || length>INT32_MAX) {
    fail(EFBIG,"Contiguous file size exceeds native GameCube limits.");
    return 0;
  }
  if(contiguous_tokens[d] || busy(canonical) ||
     (d==iso_source_device && mounted[3])) {
    fail(EBUSY,"Volume has open writers or backs the mounted DVD.");
    return 0;
  }
  contiguous_root(d,root);
  DndContiguousOps ops={(void *)(intptr_t)d,contiguous_count,NULL};
  int rc=dnd_contiguous_begin(&contiguous_writers[d],root,native,
                              (uint64_t)length,ops);
  if(rc<0) {
    fail(rc==-ENOTSUP?ENOSYS:-rc,"Contiguous reservation failed.");
    return 0;
  }
  int token=next_contiguous_token++;
  if(token<=0) {dnd_contiguous_abort(&contiguous_writers[d]);fail(EIO,"Writer token exhausted.");return 0;}
  contiguous_tokens[d]=token;
  return token;
}
void dnd_fs_contiguous_write(int token,DndArray *data,int offset,int count) {
  storage_barrier();
  int d=contiguous_find(token);
  if(d<0 || !range(data,offset,count))return;
  int rc=dnd_contiguous_write(&contiguous_writers[d],data->data+offset,(size_t)count);
  if(rc<0)fail(-rc,"Contiguous file write failed.");
}
void dnd_fs_contiguous_complete(int token) {
  storage_barrier();
  int d=contiguous_find(token);
  if(d<0)return;
  int rc=dnd_contiguous_complete(&contiguous_writers[d]);
  if(!contiguous_writers[d].active)contiguous_tokens[d]=0;
  if(rc<0)fail(-rc,"Contiguous file publication failed.");
}
void dnd_fs_contiguous_abort(int token) {
  storage_barrier();
  for(int d=0;d<7;d++)if(token>0 && contiguous_tokens[d]==token) {
    int rc=dnd_contiguous_abort(&contiguous_writers[d]);
    contiguous_tokens[d]=0;
    if(rc<0)fail(-rc,"Contiguous file cleanup failed.");
    return;
  }
}

void dnd_fs_mkdir(DndString *path) {
  storage_barrier();
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
  storage_barrier();
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
  storage_barrier();
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
#ifndef DND_GAMECUBE_STORAGE
    if (lstat(native, &st) == 0 && S_ISLNK(st.st_mode)) {
      fail(ENOSYS, "Symbolic links are unsupported.");
      return NULL;
    }
#endif
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
  storage_barrier();
  DirectoryHandle *h = dir_handle(token);
  if (!h)
    return;
  DIR *dir = h->dir;
  h->token = 0;
  if (closedir(dir))
    fail(errno, "Directory close failed.");
}
DndString *dnd_fs_getcwd(DndManagedHeap *heap) {
  storage_barrier();
  return dnd_string_from_utf8(heap, cwd);
}
void dnd_fs_setcwd(DndString *path) {
  storage_barrier();
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
  if (d != 4 && d != 5) {
    fail(EINVAL, "Not a memory card slot.");
    return false;
  }
  if (!mounted[d]) {
    fail(ENODEV, "Memory card is not mounted.");
    return false;
  }
#ifdef DND_GAMECUBE_STORAGE
  int recovery=dnd_gc_card_recover(d);if(recovery<0){fail(-recovery,"Memory card recovery failed.");return false;}
#endif
  if (!utf8(name, out))
    return false;
  size_t n = strlen(out);
  if (!n || n > 32 || !strcmp(out,DND_CARD_JOURNAL)) {
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
  storage_barrier();
  if (!valid_device(d))
    return false;
  if (d != 4 && d != 5) {
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
  storage_barrier();
  char n[PATH_CAP];
  uint32_t length, offset;
  if (!card_arguments(d, name, n) || !save_header(d, n, &length, &offset))
    return 0;
  return (int)length;
}
DndArray *dnd_card_read(DndManagedHeap *heap, int d, DndString *name) {
  storage_barrier();
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
#ifdef DND_GAMECUBE_STORAGE
  int rc=dnd_gc_card_write_save(d,n,container,(int)length,metadata,banner!=NULL,icon!=NULL);
#else
  int rc=card_backend_write(d,n,container,(int)length);
#endif
  free(container);
  if (rc < 0)
    fail(-rc, "Memory card write failed.");
}
void dnd_card_write(int d, DndString *name, DndArray *data) {
  storage_barrier();
  write_save(d, name, data, NULL, NULL, NULL, NULL, false);
}
void dnd_card_write_save(int d, DndString *name, DndArray *data,
                         DndString *title, DndString *comment, DndArray *banner,
                         DndArray *icon) {
  storage_barrier();
  write_save(d, name, data, title, comment, banner, icon, true);
}
void dnd_card_delete(int d, DndString *name) {
  storage_barrier();
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
  storage_barrier();
  if (!valid_device(d) || (d != 4 && d != 5) || !mounted[d]) {
    fail(ENODEV, "Memory card is not mounted.");
    return NULL;
  }
  char names[127][33];
  int count = 0, error = 0;
#ifdef DND_GAMECUBE_STORAGE
  int recovery=dnd_gc_card_recover(d);if(recovery<0){fail(-recovery,"Memory card recovery failed.");return NULL;}
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

/* One bounded logical read; only OS/backend I/O and native memory while detached.
 * All other storage entry points wait before touching shared backend state.
 * The managed AssetStream owns the file until completion; close/unmount wait too. */
static int asset_read_quantum=32768;
static uint64_t asset_stats[7];
static uint64_t asset_now_us(void) {
#ifdef DND_GAMECUBE_STORAGE
    return ticks_to_microsecs(gettime());
#else
    struct timespec now;clock_gettime(CLOCK_MONOTONIC,&now);
    return (uint64_t)now.tv_sec*1000000+(uint64_t)now.tv_nsec/1000;
#endif
}
void dnd_asset_read_quantum(int bytes) {
    if(bytes!=0&&bytes!=16384&&bytes!=32768&&bytes!=65536){fail(EINVAL,"Invalid asset read quantum.");return;}
    asset_read_quantum=bytes;
}
void dnd_asset_reset_stats(void){memset(asset_stats,0,sizeof(asset_stats));}
void dnd_asset_snapshot(DndArray *data) {
    if(!data||data->length<7||data->element_size!=8||data->elements_are_references){fail(EINVAL,"Read statistics require seven Int64 slots.");return;}
    memcpy(data->data,asset_stats,sizeof(asset_stats));
}
int64_t dnd_asset_stat(int kind){return kind>=0&&kind<7?(int64_t)asset_stats[kind]:0;}
int dnd_asset_open(DndString *path) { return dnd_fs_open(path,3,1,1); }
int dnd_asset_read(int token,int64_t offset,int buffer,int destination,int count) {
  storage_barrier();
  FileHandle *h=handle(token);
  if(!h)return -1;
  if(offset<0||count<0||!(h->access&1)||(int64_t)(off_t)offset!=offset||
     !dnd_buffer_io_begin||!dnd_buffer_io_end) {fail(EINVAL,"Invalid asset read.");return -1;}
  uint64_t address=dnd_buffer_io_begin(buffer,destination,count);
  if(!address){fail(EBUSY,"Asset destination is busy or invalid.");return -1;}
  int fd=h->fd,done=0,error=0,quantum=asset_read_quantum;
  uint64_t calls=0,bounced=0,direct=0,before=asset_now_us();
  atomic_store_explicit(&asset_io_busy,1,memory_order_release);
  int context=dnd_async_native_begin?dnd_async_native_begin():-1;
  if(lseek(fd,(off_t)offset,SEEK_SET)<0)error=errno;
  while(!error&&done<count) {
    unsigned char *target=(unsigned char*)(uintptr_t)address+done;
    size_t remaining=(size_t)(count-done),amount=remaining;
    bool aligned=quantum&&((uintptr_t)target&31)==0&&remaining>=32;
    if(aligned){amount&=~(size_t)31;if(amount>(size_t)quantum)amount=(size_t)quantum;}
    else if(quantum&&((uintptr_t)target&31)&&amount>32-((uintptr_t)target&31))amount=32-((uintptr_t)target&31);
    calls++;
    ssize_t n=aligned?read(fd,target,amount):aligned_read(fd,target,amount);
    if(n<0){if(errno==EINTR)continue;error=errno;break;}
    if(!n)break;
    if(aligned)direct+=(uint64_t)n;else bounced+=(uint64_t)n;
    done+=(int)n;
  }
  uint64_t elapsed=asset_now_us()-before;
  atomic_store_explicit(&asset_io_busy,0,memory_order_release);
  if(dnd_async_native_end)dnd_async_native_end(context);
  dnd_buffer_io_end(buffer);
  asset_stats[0]+=calls;asset_stats[1]+=(uint64_t)done;asset_stats[2]+=bounced;
  asset_stats[3]+=direct;asset_stats[4]++;asset_stats[5]+=elapsed;
  if(elapsed>asset_stats[6])asset_stats[6]=elapsed;
  if(error){fail(error,"Native asset read failed.");return -1;}
  return done;
}
