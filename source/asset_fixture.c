#include "dnd_asset_fixture.h"
#ifdef DND_DEBUG_AUTOMATION
/* Debug-only game-owned fixture status. No file contents or proprietary data
 * are transferred; only the selected path and parse result metadata. */
DndFixtureTelemetry dnd_jsystem_fixture __attribute__((used,aligned(32))) = {
    .magic=DND_FIXTURE_MAGIC,.version=DND_FIXTURE_VERSION
};
void dnd_fixture_report(int phase,int source_index,int source_count,
    int asset_index,int asset_count,int passed,int skipped,int failed,
    int kind,int detail,DndString *name) {
    DndFixtureTelemetry *r=&dnd_jsystem_fixture;
    r->phase=(uint32_t)phase;r->source_index=(uint32_t)source_index;
    r->source_count=(uint32_t)source_count;
    r->asset_index=(uint32_t)asset_index;r->asset_count=(uint32_t)asset_count;
    r->passed=(uint32_t)passed;r->skipped=(uint32_t)skipped;
    r->failed=(uint32_t)failed;r->kind=(uint32_t)kind;
    r->detail=(uint32_t)detail;
    unsigned n=0;
    if(name) {
        for(;n<name->length && n<DND_FIXTURE_NAME_MAX-1;n++){
            uint16_t ch=name->chars[n];
            r->name[n]=(unsigned char)(ch>=32 && ch<127 ? ch : '?');
        }
    }
    r->name[n]=0;
    r->name_bytes=n;
    /* Commit last: debugger snapshots always read after a presentation stop. */
    r->serial++;
}
#endif
