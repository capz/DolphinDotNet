#include "dnd_asset_corpus.h"
#include <assert.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>

_Static_assert(sizeof(DndAssetCorpusReport)==224,"Incorrect debugger report bytes.");
_Static_assert(offsetof(DndAssetCorpusReport,serial)==8,"Serial offset.");
_Static_assert(offsetof(DndAssetCorpusReport,finished)==44,"Finish offset.");
_Static_assert(offsetof(DndAssetCorpusReport,path)==64,"ASCII label offset.");

int main(void) {
    assert(dnd_asset_corpus_report.magic==DND_CORPUS_MAGIC);
    assert(dnd_asset_corpus_report.version==1);
    struct {
        DndObject object;
        uint32_t length;
        uint16_t chars[8];
    } name={0};
    name.length=8;
    const uint16_t value[8]={'G','A','M','E','/','X',0x3001,'Z'};
    memcpy(name.chars,value,sizeof value);
    dnd_corpus_publish(1,DND_CORPUS_OK,16384,65536,(DndString*)&name);
    assert(dnd_asset_corpus_report.serial==1);
    assert(dnd_asset_corpus_report.kind==1 && dnd_asset_corpus_report.result==1);
    assert(dnd_asset_corpus_report.asset_bytes==16384 &&
           dnd_asset_corpus_report.gx_bytes==65536);
    assert(strcmp((const char*)dnd_asset_corpus_report.path,"GAME/X?Z")==0);
    dnd_corpus_finish(2,1024,8,1);
    assert(dnd_asset_corpus_report.disc_count==2 &&
           dnd_asset_corpus_report.entries_scanned==1024 &&
           dnd_asset_corpus_report.candidates==8 &&
           dnd_asset_corpus_report.completed==1 &&
           dnd_asset_corpus_report.finished==1);
    assert(dnd_asset_corpus_report.serial==1);
    puts("asset corpus debugger ABI OK");
    return 0;
}
