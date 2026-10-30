#ifndef DND_ASSET_CORPUS_H
#define DND_ASSET_CORPUS_H
#include "dnd_managed.h"
#include <stdint.h>

/* Fixed-size, versioned memory record. Read only at the debugger's post-present
 * breakpoint; no filesystem, private asset contents or write access exposed. */
enum { DND_CORPUS_MAGIC=0x444e4346u, DND_CORPUS_VERSION=1,
       DND_CORPUS_BTI=1, DND_CORPUS_J3D=2, DND_CORPUS_JPA=3,
       DND_CORPUS_BMG=4, DND_CORPUS_BFN=5, DND_CORPUS_RARC=6,
       DND_CORPUS_OK=1, DND_CORPUS_UNSUPPORTED=2,
       DND_CORPUS_MALFORMED=3, DND_CORPUS_RENDER_ERROR=4 };
typedef struct {
    volatile uint32_t magic,version,serial,kind; /* 0x00 */
    volatile uint32_t result,asset_bytes,gx_bytes,disc_count; /* 0x10 */
    volatile uint32_t entries_scanned,candidates,completed,finished; /* 0x20 */
    volatile uint32_t read_only_reserved[4]; /* 0x30 */
    volatile char path[160]; /* 0x40, ASCII names only, NUL-terminated */
} DndAssetCorpusReport;
#ifdef DND_DEBUG_AUTOMATION
extern DndAssetCorpusReport dnd_asset_corpus_report;
#endif
void dnd_corpus_publish(int kind,int result,int asset_bytes,int gx_bytes,DndString *path);
void dnd_corpus_finish(int discs,int scanned,int candidates,int completed);
#endif
