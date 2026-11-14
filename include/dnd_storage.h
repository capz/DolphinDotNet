#ifndef DND_STORAGE_H
#define DND_STORAGE_H
#include "dnd_managed.h"
#include "dnd_card_manager.h"
#include "dnd_hardware.h" /* Shared native storage/platform intrinsic declarations. */
bool dnd_storage_mount(int device);
bool dnd_storage_mount_disc(int format);
bool dnd_storage_mount_disc_image(DndString *path);
/* Physical GC Loader selection, distinct from the file-backed dvd:/ mount.
 * Returns only after physical readback verification; failure throws. */
void dnd_storage_select_gcloader_disc_image(DndString *path);
DndArray *dnd_storage_disc_header(DndManagedHeap *heap);
void dnd_storage_unmount(int device);
/* Bootstrap only, after managed executors stop. Closes owned handles and volumes. */
void dnd_storage_shutdown(void);
bool dnd_storage_is_mounted(int device);
/* Mount registry: session-local; native memory cards are deliberately excluded.
 * Configurations contain effective settings after a successful mount. -1 means
 * automatic partition selection in a request. All paths are UTF-8. */
enum { DND_MOUNT_BLOCK_VOLUME=1, DND_MOUNT_DISC=2, DND_MOUNT_DISC_IMAGE=3 };
enum { DND_MOUNT_FS_AUTOMATIC=0, DND_MOUNT_FS_FAT=1, DND_MOUNT_FS_EXFAT=2 };
enum { DND_MOUNT_NEVER_MOUNTED=0, DND_MOUNT_MOUNTED=1,
       DND_MOUNT_UNMOUNTED=2, DND_MOUNT_UNAVAILABLE=3 };
enum { DND_MOUNT_SUCCESS=0, DND_MOUNT_NO_PREVIOUS_CONFIGURATION=1,
       DND_MOUNT_MEDIA_UNAVAILABLE=2, DND_MOUNT_BUSY=3,
       DND_MOUNT_CONFIGURATION_CONFLICT=4, DND_MOUNT_UNSUPPORTED=5,
       DND_MOUNT_INVALID_CONFIGURATION=6, DND_MOUNT_IO_ERROR=7 };
enum { DND_MOUNT_CHANGE_MOUNTED=1, DND_MOUNT_CHANGE_UNMOUNTED=2,
       DND_MOUNT_CHANGE_REMOVED=3, DND_MOUNT_CHANGE_HISTORY_LOST=4 };
typedef struct {
  int device, kind, disc_format, filesystem; /* kind: 1 volume, 2 disc, 3 image */
  int64_t partition_lba;
  int cache_pages, sectors_per_page, source_device;
  int64_t source_partition_lba;
  int source_filesystem, source_cache_pages, source_sectors_per_page;
  char image_path[1024];
} DndMountConfiguration;
typedef struct {
  DndMountConfiguration configuration;
  char root_path[16];
  int state, presence, last_error; /* state: 0 never, 1 mounted, 2 unmounted, 3 unavailable */
  int64_t mount_id, last_mount_sequence, change_sequence, source_mount_id;
} DndMountInfo;
typedef struct { DndMountInfo info; int reason; } DndMountChange;
bool dnd_storage_get_mount_info(int device, DndMountInfo *info);
int dnd_storage_last_mount_device(void); /* -1 before the first successful mount */
bool dnd_storage_mount_valid(int device, int64_t mount_id);
/* Result: 0 success, 1 no history, 2 unavailable, 3 busy, 4 conflict,
 * 5 unsupported, 6 invalid configuration, 7 I/O failure. Never throws. */
int dnd_storage_mount_configuration(const DndMountConfiguration *configuration);
int dnd_storage_remount(int device);
void dnd_storage_poll(void);
/* Bounded queue: reason 1 mounted, 2 unmounted, 3 removed, 4 history lost.
 * History-lost records have device -1; resynchronize with get_mount_info. */
bool dnd_storage_next_mount_change(DndMountChange *change);
/* Private managed wire bridge: versioned little-endian Int64 fields + UTF-8 path. */
DndArray *dnd_storage_mount_snapshot(DndManagedHeap *, int device);
DndArray *dnd_storage_mount_change(DndManagedHeap *);
bool dnd_storage_mount_handler_equal(DndObject *first,DndObject *second);
int dnd_storage_mount_configured(int device,int kind,int format,DndString *image,
                                int64_t partition,int filesystem,int pages,int sectors,
                                int source,int64_t source_partition,int source_fs,
                                int source_pages,int source_sectors);
int dnd_storage_open_mounted(int device,int64_t mount_id,DndString *relative,
                            int mode,int access,int share);
/* Automatic contiguous allocation and selective post-write repair default
 * to off. Explicit CreateContiguousFile is not gated by this policy. */
void dnd_storage_auto_defrag_set(bool enabled);
bool dnd_storage_auto_defrag_enabled(void);
int dnd_storage_auto_defrag_min_size(void);
int64_t dnd_storage_auto_defrag_stat(int kind);
void dnd_storage_auto_defrag_reset_stats(void);
void dnd_storage_auto_defrag_set_min_size(int minimum);
int dnd_fs_defrag_analyze(DndString *path);
int dnd_fs_defrag_optimize(DndString *path);
DndString *dnd_storage_path(DndManagedHeap *, int device, DndString *path);
DndString *dnd_fs_full_path(DndManagedHeap *, DndString *path);
bool dnd_fs_exists(DndString *path, bool directory);
DndArray *dnd_fs_read_all(DndManagedHeap *, DndString *path);
void dnd_fs_write_all(DndString *path, DndArray *data);
void dnd_fs_delete(DndString *path, bool directory);
void dnd_fs_move(DndString *source, DndString *destination);
int dnd_fs_contiguous_begin(DndString *path,int64_t expected_size);
void dnd_fs_contiguous_write(int token,DndArray *data,int offset,int count);
void dnd_fs_contiguous_complete(int token);
int dnd_fs_contiguous_complete_step(int token);
void dnd_fs_contiguous_abort(int token);
void dnd_fs_mkdir(DndString *path);
int dnd_fs_open(DndString *path, int mode, int access, int share);
/* Native asset readers use the same mounted-volume normalization and handles. */
bool dnd_fs_path_utf8_native(DndString *path, char *output, unsigned capacity);
int dnd_fs_open_utf8_native(const char *path, int mode, int access, int share);
int64_t dnd_fs_length_native(int handle);
int dnd_fs_read_native(int handle, void *data, int count);
int dnd_fs_read(int handle, DndArray *data, int offset, int count);
void dnd_fs_write(int handle, DndArray *data, int offset, int count);
int64_t dnd_fs_seek(int handle, int64_t offset, int origin);
int64_t dnd_fs_length(int handle);
void dnd_fs_set_length(int handle, int64_t length);
void dnd_fs_flush(int handle);
void dnd_fs_close(int handle);
int dnd_fs_dir_open(DndString *path);
DndString *dnd_fs_dir_next(DndManagedHeap *, int handle, int kind);
void dnd_fs_dir_close(int handle);
DndString *dnd_fs_getcwd(DndManagedHeap *);
void dnd_fs_setcwd(DndString *path);
bool dnd_card_mount(int device, DndString *game, DndString *company);
DndArray *dnd_card_read(DndManagedHeap *, int device, DndString *name);
void dnd_card_write(int device, DndString *name, DndArray *data);
int dnd_card_length(int device, DndString *name);
void dnd_card_write_save(int device, DndString *name, DndArray *data,
                         DndString *title, DndString *comment, DndArray *banner,
                         DndArray *icon);
void dnd_card_delete(int device, DndString *name);
DndArray *dnd_card_entries(DndManagedHeap *, int device);
void dnd_asset_read_quantum(int bytes);
void dnd_asset_reset_stats(void);
void dnd_asset_snapshot(DndArray *data);
int64_t dnd_asset_stat(int kind);
int dnd_asset_open(DndString *path);
int dnd_asset_read(int token,int64_t offset,int buffer,int destination,int count);
// Native diagnostics used by leak/cleanup regression tests.
int dnd_fs_open_handles(void);
#endif
