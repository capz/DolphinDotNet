#include "elf.h"
#include "resident.h"
#include <string.h>

static uint16_t be16(const uint8_t *p) { return (uint16_t)((uint16_t)p[0] << 8 | p[1]); }
static uint32_t be32(const uint8_t *p) { return (uint32_t)p[0]<<24 | (uint32_t)p[1]<<16 | (uint32_t)p[2]<<8 | p[3]; }
static bool overlaps(uintptr_t a, size_t n, uintptr_t b, size_t m) {
    return n && m && (a <= b ? b-a < n : a-b < m);
}
bool dnd_elf_validate(const uint8_t *image, size_t size, uintptr_t stage, size_t stage_size, DndElfPlan *plan) {
    if (!image || !plan || size < 52 || size > DND_LAUNCHER_MAX_IMAGE ||
        !stage_size || stage > UINTPTR_MAX-stage_size) return false;
    memset(plan, 0, sizeof(*plan));
    if (memcmp(image, "\177ELF\1\2\1", 7) || be16(image+16)!=2 || be16(image+18)!=20 ||
        be32(image+20)!=1 || be16(image+40)!=52 || be16(image+42)!=32) return false;
    uint32_t phoff = be32(image+28), entry = be32(image+24);
    unsigned count = be16(image+44);
    if ((entry & 3) || !count || count>64 || phoff<52 || phoff>size || count>(size-phoff)/32) return false;
    bool executable = false;
    for (unsigned i=0; i<count; i++) {
        const uint8_t *ph = image+phoff+i*32;
        uint32_t type=be32(ph), address=be32(ph+8), physical=be32(ph+12);
        uint32_t offset=be32(ph+4), files=be32(ph+16), memory=be32(ph+20), flags=be32(ph+24), align=be32(ph+28);
        if (type==2 || type==3) return false; /* Dynamic linker/relocation unsupported. */
        if (type!=1 || !memory) continue;
        if (plan->count==DND_ELF_SEGMENTS || (physical && physical!=address) || files>memory ||
            offset>size || files>size-offset || address<0x80003100u || address>=DND_RESIDENT_BASE ||
            memory>DND_RESIDENT_BASE-address || (flags & ~7u) ||
            (align>1 && ((align & (align-1)) || (address & (align-1))!=(offset & (align-1)))) ||
            overlaps(address, memory, stage, stage_size)) return false;
        for (unsigned j=0; j<plan->count; j++)
            if (overlaps(address, memory, plan->segments[j].address, plan->segments[j].memory_size)) return false;
        plan->segments[plan->count++] = (DndElfSegment){offset, address, files, memory};
        if ((flags & 1) && files>=4 && entry>=address && entry-address<=files-4) executable=true;
    }
    plan->entry=entry;
    return executable;
}
uint32_t dnd_launcher_crc32(const uint8_t *data, size_t size) {
    uint32_t crc=0xffffffffu;
    for (size_t i=0; i<size; i++) {
        crc^=data[i];
        for (unsigned bit=0; bit<8; bit++) crc=(crc>>1) ^ (0xedb88320u & (0u-(crc&1)));
    }
    return ~crc;
}
