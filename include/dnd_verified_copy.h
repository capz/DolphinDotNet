#ifndef DND_VERIFIED_COPY_H
#define DND_VERIFIED_COPY_H
#include <stddef.h>
#include <stdint.h>

/* Portable bounded streaming primitive. It never publishes/deletes files.
 * The owner opens a create-new internal stage and holds source/device locks.
 * Exact I/O callbacks return 0 or -errno; no partial-success convention.
 * read_stage must read a reopened/invalidate-cached descriptor after sync.
 * checkpoint must durably persist the whole record before returning success.
 * Prefix records are checksummed by the enclosing journal, not C struct bytes.
 */
#define DND_COPY_VERSION 1u
#define DND_COPY_BUFFER 32768u
typedef struct {
    uint32_t state[8];
    uint64_t bytes;
    size_t used;
    unsigned char block[64];
} DndSha256;
void dnd_sha256_init(DndSha256 *s);
void dnd_sha256_update(DndSha256 *s,const void *data,size_t bytes);
void dnd_sha256_finish(const DndSha256 *s,unsigned char out[32]);

typedef struct {
    uint32_t version;
    uint64_t length, durable_bytes, source_cookie[2];
    unsigned char prefix_sha256[32];
} DndCopyCheckpoint;
typedef struct {
    uint64_t bytes_read,bytes_written,durable_bytes,verified_bytes;
    unsigned char sha256[32];
} DndCopyResult;
typedef struct {
    void *context;
    int (*read_source)(void *,uint64_t,void *,size_t);
    int (*write_stage)(void *,uint64_t,const void *,size_t);
    int (*read_stage)(void *,uint64_t,void *,size_t);
    int (*sync_stage)(void *);
    int (*reopen_stage)(void *);
    int (*checkpoint)(void *,const DndCopyCheckpoint *);
    /* Revalidate stable device/source identity, size, locks and media state. */
    int (*revalidate)(void *);
    int (*cancelled)(void *);
    /* Informational only. It is never a durability checkpoint. */
    void (*progress)(void *,const DndCopyResult *);
} DndCopyOps;

/* Resume checks both source and staged prefixes against the durable SHA-256
 * before writing. Final verification rereads all staged bytes and revalidates
 * the source. On error/cancellation, preserve the journal/stage for recovery.
 * interval is a positive multiple of BUFFER; scratch is >= BUFFER, aligned 32.
 * result counters report this invocation (including prefix revalidation).
 * All lengths and offsets are checked against GameCube's INT32_MAX limit. */
int dnd_verified_copy(DndCopyOps ops,const DndCopyCheckpoint *resume,
                       uint64_t checkpoint_interval,void *scratch,size_t capacity,
                       DndCopyResult *result);
#endif
