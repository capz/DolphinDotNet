#include "dnd_raw_move.h"
#include <assert.h>
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>

int main(void) {
    DndRawMove r={.operation={7,11},.image_id={3,5},.sector_count=100000,
        .entry_lba=50,.file_bytes=1459978240,.cluster_count=44555,
        .destination=50000,.original_first=4,.filesystem=DND_DEFRAG_FAT32,
        .bytes_per_cluster=32768};
    memset(r.metadata_sha256,1,32);memset(r.file_sha256,2,32);
    memset(r.window_sha256,3,32);memset(r.boot_sha256,4,32);memset(r.backup_sha256,5,32);
    unsigned char wire[2048],hash[32];DndRawMove decoded;
    assert(dnd_raw_request_encode(&r,wire)==0 && dnd_raw_request_decode(wire,&decoded)==0);
    assert(decoded.file_bytes==1459978240 && decoded.destination==50000);
    for(unsigned i=0;i<512;i++) {
        wire[i]^=1;assert(dnd_raw_request_decode(wire,&decoded)==-EILSEQ);wire[i]^=1;
    }
    DndSha256 sha;dnd_sha256_init(&sha);dnd_sha256_update(&sha,wire,512);dnd_sha256_finish(&sha,hash);
    DndRawPatch patch={.lba=12345},p;
    for(unsigned i=0;i<512;i++) {
        patch.before[i]=(unsigned char)i;patch.applied[i]=(unsigned char)(i+1);
        patch.cleaned[i]=(unsigned char)(i+2);
    }
    assert(dnd_raw_patch_encode(&patch,9,hash,wire)==0);
    assert(dnd_raw_patch_decode(wire,9,hash,&p)==0 && p.lba==12345);
    assert(dnd_raw_patch_decode(wire,10,hash,&p)==-EILSEQ);
    for(unsigned i=0;i<2048;i++) {
        wire[i]^=1;assert(dnd_raw_patch_decode(wire,9,hash,&p)==-EILSEQ);wire[i]^=1;
    }
    hash[0]^=1;assert(dnd_raw_patch_decode(wire,9,hash,&p)==-EILSEQ);hash[0]^=1;
    DndRawCheckpoint c={.sequence=4,.patch_count=19,.state=DND_RAW_METADATA_APPLIED},d;
    memcpy(c.request_sha256,hash,32);memset(c.patches_sha256,0x88,32);
    assert(dnd_raw_checkpoint_encode(&c,wire)==0 && dnd_raw_checkpoint_decode(wire,&d)==0);
    assert(d.state==DND_RAW_METADATA_APPLIED && d.patch_count==19);
    for(unsigned i=0;i<512;i++) {
        wire[i]^=1;assert(dnd_raw_checkpoint_decode(wire,&d)==-EILSEQ);wire[i]^=1;
    }
    r.file_bytes=(uint64_t)INT32_MAX+1;
    assert(dnd_raw_request_encode(&r,wire)==-EINVAL);
    r.file_bytes=1459978240;r.logical_first=44555;
    assert(dnd_raw_request_encode(&r,wire)==-EINVAL);
    r.logical_first=0;r.file_flags=DND_DEFRAG_PINNED;
    assert(dnd_raw_request_encode(&r,wire)==-EINVAL);
    r.file_flags=0;r.destination=UINT32_MAX;
    assert(dnd_raw_request_encode(&r,wire)==-EINVAL);
    puts("P6 canonical request, sector images, binding, corruption and range codecs passed");
    return 0;
}
