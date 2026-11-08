#include "dnd_raw_move.h"
#include <errno.h>
#include <limits.h>
#include <string.h>

static void put32(unsigned char *p,uint32_t x) {
    for(unsigned i=0;i<4;i++)p[i]=(unsigned char)(x>>(8*i));
}
static void put64(unsigned char *p,uint64_t x) {
    for(unsigned i=0;i<8;i++)p[i]=(unsigned char)(x>>(8*i));
}
static uint32_t get32(const unsigned char *p) {
    uint32_t x=0;for(unsigned i=0;i<4;i++)x|=(uint32_t)p[i]<<(8*i);return x;
}
static uint64_t get64(const unsigned char *p) {
    uint64_t x=0;for(unsigned i=0;i<8;i++)x|=(uint64_t)p[i]<<(8*i);return x;
}
static void checksum(const unsigned char *p,size_t n,unsigned char out[32]) {
    DndSha256 sha;dnd_sha256_init(&sha);dnd_sha256_update(&sha,p,n);
    dnd_sha256_finish(&sha,out);
}
static int nonzero(const unsigned char *p,size_t n) {
    unsigned char v=0;while(n--)v|=*p++;return v!=0;
}
static int request_valid(const DndRawMove *r) {
    if(!r || !(r->operation[0]|r->operation[1]) || !(r->image_id[0]|r->image_id[1]) ||
       !r->sector_count || r->partition_lba>=r->sector_count ||
       r->entry_lba<r->partition_lba || r->entry_lba>=r->sector_count ||
       r->entry_offset>480 || r->entry_offset%32 || !r->file_bytes || r->file_bytes>INT32_MAX ||
       !r->cluster_count || r->destination<2 || r->original_first<2 ||
       (r->file_flags&(DND_DEFRAG_PINNED|DND_DEFRAG_DIRECTORY|DND_DEFRAG_NAME_UNSUPPORTED)) ||
       (r->file_flags&~DND_DEFRAG_NO_FAT_CHAIN) ||
       (r->filesystem!=DND_DEFRAG_FAT32 && r->filesystem!=DND_DEFRAG_EXFAT) ||
       r->bytes_per_cluster<512 || r->bytes_per_cluster>16777216 ||
       (r->bytes_per_cluster&(r->bytes_per_cluster-1)))return 0;
    uint64_t n=r->file_bytes/r->bytes_per_cluster+(r->file_bytes%r->bytes_per_cluster!=0);
    return r->logical_first<n && r->cluster_count<=n-r->logical_first &&
           r->cluster_count<=UINT32_MAX-r->destination &&
           nonzero(r->metadata_sha256,32) && nonzero(r->file_sha256,32) &&
           nonzero(r->window_sha256,32) && nonzero(r->boot_sha256,32);
}
int dnd_raw_request_encode(const DndRawMove *r,unsigned char out[512]) {
    if(!out || !request_valid(r))return -EINVAL;
    memset(out,0,512);memcpy(out,"DNDRAW01",8);put32(out+8,1);
    put32(out+12,r->filesystem);put64(out+16,r->operation[0]);put64(out+24,r->operation[1]);
    put64(out+32,r->image_id[0]);put64(out+40,r->image_id[1]);
    put64(out+48,r->partition_lba);put64(out+56,r->sector_count);put64(out+64,r->generation);
    put64(out+72,r->entry_lba);put64(out+80,r->file_bytes);put64(out+88,r->logical_first);
    put64(out+96,r->cluster_count);put32(out+104,r->destination);put32(out+108,r->original_first);
    put32(out+112,r->file_flags);put32(out+116,r->bytes_per_cluster);
    put32(out+120,r->entry_offset);put32(out+124,r->attributes);
    memcpy(out+128,r->metadata_sha256,32);memcpy(out+160,r->file_sha256,32);
    memcpy(out+192,r->window_sha256,32);memcpy(out+224,r->boot_sha256,32);
    memcpy(out+256,r->backup_sha256,32);checksum(out,480,out+480);return 0;
}
int dnd_raw_request_decode(const unsigned char in[512],DndRawMove *out) {
    if(!in || !out)return -EINVAL;
    unsigned char sum[32];checksum(in,480,sum);
    if(memcmp(in,"DNDRAW01",8) || get32(in+8)!=1 || memcmp(sum,in+480,32))return -EILSEQ;
    DndRawMove r={0};r.filesystem=get32(in+12);r.operation[0]=get64(in+16);r.operation[1]=get64(in+24);
    r.image_id[0]=get64(in+32);r.image_id[1]=get64(in+40);r.partition_lba=get64(in+48);
    r.sector_count=get64(in+56);r.generation=get64(in+64);r.entry_lba=get64(in+72);
    r.file_bytes=get64(in+80);r.logical_first=get64(in+88);r.cluster_count=get64(in+96);
    r.destination=get32(in+104);r.original_first=get32(in+108);r.file_flags=get32(in+112);
    r.bytes_per_cluster=get32(in+116);
    if(get32(in+120)>UINT16_MAX || get32(in+124)>UINT16_MAX)return -EILSEQ;
    r.entry_offset=(uint16_t)get32(in+120);r.attributes=(uint16_t)get32(in+124);
    memcpy(r.metadata_sha256,in+128,32);memcpy(r.file_sha256,in+160,32);
    memcpy(r.window_sha256,in+192,32);memcpy(r.boot_sha256,in+224,32);memcpy(r.backup_sha256,in+256,32);
    unsigned char canonical[512];
    if(dnd_raw_request_encode(&r,canonical) || memcmp(in,canonical,512))return -EILSEQ;
    *out=r;return 0;
}
int dnd_raw_patch_encode(const DndRawPatch *p,uint64_t index,const unsigned char binding[32],
                           unsigned char out[2048]) {
    if(!p || !binding || !out)return -EINVAL;
    memset(out,0,2048);memcpy(out,"DNDRPAT1",8);put64(out+8,index);put64(out+16,p->lba);
    memcpy(out+24,binding,32);memcpy(out+512,p->before,512);
    memcpy(out+1024,p->applied,512);memcpy(out+1536,p->cleaned,512);
    /* The hash field is still zero: bind header and all three images. */
    checksum(out,2048,out+480);return 0;
}
int dnd_raw_patch_decode(const unsigned char in[2048],uint64_t index,
                           const unsigned char binding[32],DndRawPatch *out) {
    if(!in || !binding || !out)return -EINVAL;
    unsigned char expected[2048];memcpy(expected,in,2048);memset(expected+480,0,32);
    unsigned char sum[32];checksum(expected,2048,sum);
    if(memcmp(in,"DNDRPAT1",8) || get64(in+8)!=index || memcmp(in+24,binding,32) ||
       memcmp(sum,in+480,32))return -EILSEQ;
    DndRawPatch p={0};p.lba=get64(in+16);
    memcpy(p.before,in+512,512);memcpy(p.applied,in+1024,512);memcpy(p.cleaned,in+1536,512);
    if(dnd_raw_patch_encode(&p,index,binding,expected) || memcmp(expected,in,2048))return -EILSEQ;
    *out=p;return 0;
}
int dnd_raw_checkpoint_encode(const DndRawCheckpoint *r,unsigned char out[512]) {
    if(!r || !out || !r->sequence || !r->patch_count ||
       r->state<DND_RAW_PREPARED || r->state>DND_RAW_ROLLED_BACK ||
       !nonzero(r->request_sha256,32) || !nonzero(r->patches_sha256,32))return -EINVAL;
    memset(out,0,512);memcpy(out,"DNDRCK01",8);put64(out+8,r->sequence);
    put64(out+16,r->patch_count);put32(out+24,r->state);
    memcpy(out+32,r->request_sha256,32);memcpy(out+64,r->patches_sha256,32);
    checksum(out,480,out+480);return 0;
}
int dnd_raw_checkpoint_decode(const unsigned char in[512],DndRawCheckpoint *out) {
    if(!in || !out)return -EINVAL;
    unsigned char sum[32];checksum(in,480,sum);
    if(memcmp(in,"DNDRCK01",8) || memcmp(sum,in+480,32))return -EILSEQ;
    DndRawCheckpoint r={0};r.sequence=get64(in+8);r.patch_count=get64(in+16);r.state=get32(in+24);
    memcpy(r.request_sha256,in+32,32);memcpy(r.patches_sha256,in+64,32);
    unsigned char canonical[512];
    if(dnd_raw_checkpoint_encode(&r,canonical) || memcmp(canonical,in,512))return -EILSEQ;
    *out=r;return 0;
}
