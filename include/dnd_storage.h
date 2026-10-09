#ifndef DND_STORAGE_H
#define DND_STORAGE_H
#include "dnd_managed.h"
bool dnd_storage_mount(int device);
void dnd_storage_unmount(int device);
bool dnd_storage_is_mounted(int device);
DndString *dnd_storage_path(DndManagedHeap *, int device, DndString *path);
DndString *dnd_fs_full_path(DndManagedHeap *, DndString *path);
bool dnd_fs_exists(DndString *path, bool directory);
DndArray *dnd_fs_read_all(DndManagedHeap *, DndString *path);
void dnd_fs_write_all(DndString *path, DndArray *data);
void dnd_fs_delete(DndString *path, bool directory);
void dnd_fs_move(DndString *source, DndString *destination);
void dnd_fs_mkdir(DndString *path);
int dnd_fs_open(DndString *path, int mode, int access, int share);
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
// Native diagnostics used by leak/cleanup regression tests.
int dnd_fs_open_handles(void);
#endif
