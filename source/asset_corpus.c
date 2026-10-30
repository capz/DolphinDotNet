#include "dnd_asset_corpus.h"
#include <string.h>

#ifdef DND_DEBUG_AUTOMATION
DndAssetCorpusReport dnd_asset_corpus_report __attribute__((used,aligned(32))) = {
    .magic=DND_CORPUS_MAGIC,.version=DND_CORPUS_VERSION
};
static void copy_path(DndString *source) {
    volatile char *dest=dnd_asset_corpus_report.path;
    memset((void *)dest,0,sizeof dnd_asset_corpus_report.path);
    if(!source)return;
    int length=source->length;
    if(length<0)return;
    if(length>159)length=159;
    for(int i=0;i<length;i++) {
        uint16_t c=source->chars[i];
        /* Avoid control characters, ill-formed encoding and host path escapes.
         * The fixture remains usable with non-ASCII source filenames. */
        dest[i]=(char)(c>=32 && c<=126 ? c : '?');
    }
}
void dnd_corpus_publish(int kind,int result,int asset_bytes,int gx_bytes,DndString *path) {
    DndAssetCorpusReport *r=&dnd_asset_corpus_report;
    r->kind=(uint32_t)kind; r->result=(uint32_t)result;
    r->asset_bytes=asset_bytes<0?0:(uint32_t)asset_bytes;
    r->gx_bytes=gx_bytes<0?0:(uint32_t)gx_bytes;
    copy_path(path);
    r->serial++; /* commit last; debugger sees atomic snapshot at stop */
}
void dnd_corpus_finish(int discs,int scanned,int candidates,int completed) {
    DndAssetCorpusReport *r=&dnd_asset_corpus_report;
    r->disc_count=discs<0?0:(uint32_t)discs;
    r->entries_scanned=scanned<0?0:(uint32_t)scanned;
    r->candidates=candidates<0?0:(uint32_t)candidates;
    r->completed=completed<0?0:(uint32_t)completed;
    r->finished=1;
}
#else
void dnd_corpus_publish(int kind,int result,int asset_bytes,int gx_bytes,DndString *path) {
    (void)kind;(void)result;(void)asset_bytes;(void)gx_bytes;(void)path;
}
void dnd_corpus_finish(int discs,int scanned,int candidates,int completed) {
    (void)discs;(void)scanned;(void)candidates;(void)completed;
}
#endif
