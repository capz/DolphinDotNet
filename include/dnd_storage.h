#ifndef DND_STORAGE_H
#define DND_STORAGE_H
#include "dnd_managed.h"
#include "dnd_hardware.h" /* Shared native storage/platform intrinsic declarations. */
bool dnd_storage_mount(int device);
bool dnd_storage_mount_disc(int format);
bool dnd_storage_mount_disc_image(DndString *path);
DndArray *dnd_storage_disc_header(DndManagedHeap *heap);
void dnd_storage_unmount(int device);
/* Bootstrap only, after managed executors stop. Closes owned handles and volumes. */
void dnd_storage_shutdown(void);
bool dnd_storage_is_mounted(int device);
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
int dnd_asset_open(DndString *path);
int dnd_asset_read(int token,int64_t offset,int buffer,int destination,int count);
// Native diagnostics used by leak/cleanup regression tests.
int dnd_fs_open_handles(void);
#endif
