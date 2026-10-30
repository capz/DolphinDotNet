#include "dnd_asset_compat.h"
#include <assert.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
_Static_assert(sizeof(DndAssetCompatRecord)==256,"Asset RSP mailbox ABI changed.");
_Static_assert(offsetof(DndAssetCompatRecord,name)==32,"Asset identity offset changed.");
static struct { DndObject object; uint32_t length; uint16_t chars[260]; } name;
int main(void) {
    assert(dnd_asset_compat_record.magic==DND_ASSET_MAGIC);
    assert(dnd_asset_compat_record.version==1);
    name.length=5;
    for(unsigned i=0;i<5;i++)name.chars[i]=(uint16_t)("A/B\xE9x"[i]);
    dnd_asset_compat_report(DND_ASSET_READY,-1,2,0,5,(const DndString*)&name);
    assert(dnd_asset_compat_record.status==DND_ASSET_READY);
    assert(dnd_asset_compat_record.index==0xffffffffu);
    assert(dnd_asset_compat_record.total==2 && dnd_asset_compat_record.scanned==5);
    assert(dnd_asset_compat_record.serial==1);
    assert(strcmp(dnd_asset_compat_record.name,"A/B?x")==0);
    dnd_asset_compat_report(DND_ASSET_PASS,0,2,1,100,(const DndString*)&name);
    assert(dnd_asset_compat_record.serial==2 && dnd_asset_compat_record.kind==1);
    dnd_asset_compat_report(DND_ASSET_PASS,2,2,1,100,(const DndString*)&name);
    assert(dnd_asset_compat_record.serial==2); /* Invalid index cannot publish. */
    name.length=260;
    for(unsigned i=0;i<260;i++)name.chars[i]='z';
    dnd_asset_compat_report(DND_ASSET_FAIL,1,2,2,100,(const DndString*)&name);
    assert(dnd_asset_compat_record.name[223]==0);
    assert(dnd_asset_compat_record.serial==3);
    puts("asset compatibility result ABI OK");
    return 0;
}
