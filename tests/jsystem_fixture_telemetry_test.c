#include "dnd_asset_fixture.h"
#include <assert.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>

_Static_assert(sizeof(DndFixtureTelemetry)==248,"Fixture status ABI size");
_Static_assert(offsetof(DndFixtureTelemetry,name)==56,"Fixture name ABI offset");
struct FixedString {
    DndObject obj;
    uint32_t length;
    uint16_t chars[256];
};
int main(void) {
    assert(dnd_jsystem_fixture.magic==DND_FIXTURE_MAGIC);
    assert(dnd_jsystem_fixture.version==1);
    struct FixedString path={0};path.length=7;
    const char *txt="test.arc";
    for(unsigned i=0;i<path.length;i++)path.chars[i]=(uint16_t)txt[i];
    dnd_fixture_report(3,2,4,5,24,3,1,1,9,12,(DndString*)&path);
    assert(dnd_jsystem_fixture.phase==3&&dnd_jsystem_fixture.source_index==2);
    assert(dnd_jsystem_fixture.source_count==4&&dnd_jsystem_fixture.asset_index==5);
    assert(dnd_jsystem_fixture.asset_count==24&&dnd_jsystem_fixture.passed==3);
    assert(dnd_jsystem_fixture.skipped==1&&dnd_jsystem_fixture.failed==1);
    assert(dnd_jsystem_fixture.kind==9&&dnd_jsystem_fixture.detail==12);
    assert(dnd_jsystem_fixture.serial==1&&dnd_jsystem_fixture.name_bytes==7);
    assert(strcmp((const char*)dnd_jsystem_fixture.name,"test.arc")==0);
    path.length=256;for(int i=0;i<256;i++)path.chars[i]='A';
    dnd_fixture_report(4,0,0,0,0,0,0,0,0,0,(DndString*)&path);
    assert(dnd_jsystem_fixture.name_bytes==191);
    assert(dnd_jsystem_fixture.name[191]==0);
    assert(dnd_jsystem_fixture.serial==2);
    puts("JSystem fixture telemetry ABI OK");
    return 0;
}
