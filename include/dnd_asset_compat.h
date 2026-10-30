#ifndef DND_ASSET_COMPAT_H
#define DND_ASSET_COMPAT_H
#include <stdint.h>
#include "dnd_managed.h"
enum {
    DND_ASSET_MAGIC = 0x444e4143u, /* DNAC */
    DND_ASSET_VERSION = 1,
    DND_ASSET_SCANNING = 1,
    DND_ASSET_READY = 2,
    DND_ASSET_PASS = 3,
    DND_ASSET_UNSUPPORTED = 4,
    DND_ASSET_FAIL = 5,
    DND_ASSET_EMPTY = 6,
    DND_ASSET_FINISHED = 7
};
/* All numeric fields are native big endian on PPC and read while GDB halted.
 * Names contain bounded ASCII/UTF-8 text, never source asset bytes. */
typedef struct {
    volatile uint32_t magic;      /* 0x00 */
    volatile uint32_t version;    /* 0x04 */
    volatile uint32_t serial;     /* 0x08 */
    volatile uint32_t status;     /* 0x0c */
    volatile uint32_t index;      /* 0x10, zero-based, 0xffffffff if none */
    volatile uint32_t total;      /* 0x14, maximum 32 */
    volatile uint32_t kind;       /* 0x18: 1 BTI, 2 JPC, 3 J3D */
    volatile uint32_t scanned;    /* 0x1c, bounded candidate count */
    char name[224];               /* 0x20, always NUL terminated */
} DndAssetCompatRecord;
extern DndAssetCompatRecord dnd_asset_compat_record;
void dnd_asset_compat_report(int status,int index,int total,int kind,
                             int scanned,const DndString *name);
#endif
