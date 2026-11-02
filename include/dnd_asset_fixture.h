#ifndef DND_ASSET_FIXTURE_H
#define DND_ASSET_FIXTURE_H
#include <stdint.h>
#include "dnd_managed.h"
/* Hardware compatibility fixture telemetry, exposed through ordinary RSP 'm'.
 * Stable while dnd_automation_frame_boundary is halted. Values are PPC BE32. */
enum { DND_FIXTURE_MAGIC=0x444e4649u, DND_FIXTURE_VERSION=1,
       DND_FIXTURE_SCANNING=1, DND_FIXTURE_READY=2,
       DND_FIXTURE_SHOWING=3, DND_FIXTURE_DONE=4,
       DND_FIXTURE_UNAVAILABLE=5, DND_FIXTURE_NAME_MAX=192 };
typedef struct {
    volatile uint32_t magic,version,serial,phase;
    volatile uint32_t source_index,source_count,asset_index,asset_count;
    volatile uint32_t passed,skipped,failed,kind,detail,name_bytes;
    volatile unsigned char name[DND_FIXTURE_NAME_MAX];
} DndFixtureTelemetry;
#ifdef DND_DEBUG_AUTOMATION
extern DndFixtureTelemetry dnd_jsystem_fixture;
void dnd_fixture_report(int phase,int source_index,int source_count,
    int asset_index,int asset_count,int passed,int skipped,int failed,
    int kind,int detail,DndString *name);
#endif
#endif
