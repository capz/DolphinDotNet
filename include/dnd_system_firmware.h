#ifndef DND_SYSTEM_FIRMWARE_H
#define DND_SYSTEM_FIRMWARE_H
#include "dnd_managed.h"
/* Reads at most 4096 bytes through static aligned 1024-byte DMA pages.
 * Returns count, -1 unavailable/busy, -2 timeout,
 * -3 invalid range. No caller memory is retained. */
int dnd_firmware_read(DndArray *destination,int offset,int count);
int dnd_firmware_read_raw(void *destination,unsigned offset,unsigned count);
#endif
