#ifndef DND_RAW_MOVE_INTERNAL_H
#define DND_RAW_MOVE_INTERNAL_H
#include "dnd_raw_move.h"

typedef struct {
    DndRawOps io;
    DndSectorReader reader;
    DndRawMove request;
    DndDefragFile file,parent;
    uint64_t fat_lba,heap_lba,end,fat_cached,bitmap_bytes,valid_bytes;
    uint32_t clusters,spc,fat_sectors,root,fats,active,bitmap_first;
    int exfat,mirrored;
    _Alignas(32) unsigned char fat_cache[512];
} DndRawFs;
int dnd_raw_fs_open(DndRawOps,const DndSectorReader *,const DndRawMove *,
                      DndDefragSnapshot *,int original,int clean,DndRawFs *,
                      unsigned char metadata_hash[32],unsigned char boot_hash[32]);
int dnd_raw_fs_hash(DndRawFs *,const DndRawMove *,void *,DndRawResult *,
                      unsigned char file_hash[32],unsigned char window_hash[32]);
int dnd_raw_fs_build(DndRawFs *,const DndRawMove *);
int dnd_raw_fs_copy(DndRawFs *,const DndRawMove *,void *,DndRawResult *);
int dnd_raw_fs_destination(DndRawFs *,const DndRawMove *,void *,DndRawResult *);
int dnd_raw_fs_next(DndRawFs *,uint32_t,uint32_t *);
int dnd_raw_fs_cluster(DndRawFs *,uint64_t,uint32_t *);
#endif
