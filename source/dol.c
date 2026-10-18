#include "dnd_dol.h"
#include "dnd_storage.h"
#include <limits.h>
#include <stdlib.h>
#include <string.h>

#define MEM_START 0x80003100u
#define MEM_END 0x81800000u
#define SLOT_COUNT 8
#define STUB_SIZE 512
typedef struct {
    DndDolPlan plan;
    _Alignas(32) uint8_t stub[STUB_SIZE];
    uint8_t image[];
} Stage;
static struct {
    int token, file;
    bool ready, queued;
    void *allocation;
    Stage *stage;
    size_t length, position;
} slots[SLOT_COUNT];
static int next_token = 1, pending;

static uint32_t be32(const uint8_t *p) {
    return (uint32_t)p[0]<<24 | (uint32_t)p[1]<<16 | (uint32_t)p[2]<<8 | p[3];
}
static bool memory_range(uint32_t address, uint32_t size) {
    return address >= MEM_START && address < MEM_END && size <= MEM_END-address;
}
static bool overlaps(uintptr_t a, size_t n, uintptr_t b, size_t m) {
    if (!n || !m) return false;
    return a <= b ? b-a < n : a-b < m;
}
bool dnd_dol_validate(const uint8_t header[256], size_t length, DndDolPlan *plan) {
    if (!header || !plan || length < 256 || length > DND_DOL_MAX_SIZE) return false;
    memset(plan, 0, sizeof(*plan));
    plan->entry = be32(header+0xe0);
    plan->bss_address = be32(header+0xd8);
    plan->bss_size = be32(header+0xdc);
    if ((plan->entry & 3) || (plan->bss_size && !memory_range(plan->bss_address, plan->bss_size))) return false;
    bool executable = false;
    for (unsigned i=0; i<DND_DOL_SECTIONS; i++) {
        DndDolSection *section = &plan->sections[i];
        section->offset = be32(header+i*4);
        section->address = be32(header+0x48+i*4);
        section->size = be32(header+0x90+i*4);
        if (!section->size) continue;
        if (section->offset < 256 || section->offset > length || section->size > length-section->offset ||
            (section->address & 3) || !memory_range(section->address, section->size)) return false;
        for (unsigned j=0; j<i; j++) {
            DndDolSection *other = &plan->sections[j];
            if (overlaps(section->address, section->size, other->address, other->size) ||
                overlaps(section->offset, section->size, other->offset, other->size)) return false;
        }
        plan->count++;
        if (i<7 && plan->entry >= section->address && plan->entry-section->address < section->size) executable = true;
    }
    return executable;
}
bool dnd_dol_stage_safe(const DndDolPlan *plan, uintptr_t start, size_t size) {
    if (!plan || !size || start > UINTPTR_MAX-size) return false;
    if (overlaps(start, size, plan->bss_address, plan->bss_size)) return false;
    for (unsigned i=0; i<DND_DOL_SECTIONS; i++)
        if (overlaps(start, size, plan->sections[i].address, plan->sections[i].size)) return false;
    return true;
}
static int locate(int token) {
    for (int i=0; i<SLOT_COUNT; i++) if (token>0 && slots[i].token==token) return i;
    return -1;
}
static void release(int i) {
    int file = slots[i].file;
    void *allocation = slots[i].allocation;
    memset(&slots[i], 0, sizeof(slots[i]));
    free(allocation);
    if (file>0) dnd_fs_close(file);
}
/* Results: -1 malformed, -2 memory, -3 overlap, -4 I/O, -5 capacity,
 * -6 stale handle, -7 already requested, -8 unsupported platform. */
static int create(const uint8_t *header, size_t length) {
    if (!dnd_dol_platform_supported()) return -8;
    DndDolPlan plan;
    if (!dnd_dol_validate(header, length, &plan)) return -1;
    int i;
    for (i=0; i<SLOT_COUNT && slots[i].token; i++) { }
    if (i==SLOT_COUNT || next_token==INT_MAX) return -5;
    size_t size = sizeof(Stage)+length+31;
    void *allocation = malloc(size);
    if (!allocation) return -2;
    Stage *stage = (Stage *)(((uintptr_t)allocation+31)&~(uintptr_t)31);
    if (!dnd_dol_stage_safe(&plan, (uintptr_t)allocation, size)) { free(allocation); return -3; }
    size_t stub_size = dnd_dol_platform_stub_size();
    if (!stub_size || stub_size>STUB_SIZE) { free(allocation); return -8; }
    stage->plan = plan;
    dnd_dol_platform_stub_copy(stage->stub);
    memcpy(stage->image, header, 256);
    slots[i].token = next_token++;
    slots[i].stage = stage;
    slots[i].allocation = allocation;
    slots[i].length = length;
    slots[i].position = 256;
    return i;
}
int dnd_dol_prepare(DndArray *image) {
    if (!image || image->element_size!=1 || image->length<256) return -1;
    int i = create(image->data, image->length);
    if (i<0) return i;
    memcpy(slots[i].stage->image, image->data, image->length);
    slots[i].position = slots[i].length;
    slots[i].ready = true;
    return slots[i].token;
}
int dnd_dol_begin(DndString *path) {
    if (!dnd_dol_platform_supported()) return -8;
    char native[1024];
    if (!dnd_fs_path_utf8_native(path, native, sizeof(native))) return -4;
    int file = dnd_fs_open_utf8_native(native, 3, 1, 1); /* Open, Read, Share.Read */
    if (file<=0) return -4;
    DndEhFrame guard;
    dnd_eh_push(&guard);
    if (setjmp(guard.environment)) {
        dnd_eh_pop(&guard);
        dnd_fs_close(file);
        dnd_exception_rethrow();
        return -4;
    }
    int64_t length = dnd_fs_length_native(file);
    uint8_t header[256];
    if (length<256 || length>DND_DOL_MAX_SIZE) { dnd_eh_pop(&guard); dnd_fs_close(file); return -1; }
    int got = 0;
    while (got<256) {
        int n = dnd_fs_read_native(file, header+got, 256-got);
        if (n<=0) { dnd_eh_pop(&guard); dnd_fs_close(file); return -4; }
        got += n;
    }
    int i = create(header, (size_t)length);
    dnd_eh_pop(&guard);
    if (i<0) { dnd_fs_close(file); return i; }
    slots[i].file = file;
    return slots[i].token;
}
int dnd_dol_step(int token) {
    volatile int i = locate(token);
    if (i<0 || slots[i].queued) return -6;
    if (slots[i].ready) return 1;
    DndEhFrame guard;
    dnd_eh_push(&guard);
    if (setjmp(guard.environment)) {
        dnd_eh_pop(&guard);
        release(i);
        dnd_exception_rethrow();
        return -4;
    }
    size_t remaining = slots[i].length-slots[i].position;
    if (remaining) {
        int n = dnd_fs_read_native(slots[i].file, slots[i].stage->image+slots[i].position,
                                  (int)(remaining>4096 ? 4096 : remaining));
        if (n<=0) { dnd_eh_pop(&guard); release(i); return -4; }
        slots[i].position += (size_t)n;
    }
    dnd_eh_pop(&guard);
    if (slots[i].position != slots[i].length) return 0;
    dnd_fs_close(slots[i].file);
    slots[i].file = 0;
    slots[i].ready = true;
    return 1;
}
int dnd_dol_info(int token, int kind) {
    int i = locate(token);
    if (i<0) return -6;
    const DndDolPlan *p = &slots[i].stage->plan;
    switch (kind) {
        case 0: return (int)p->entry;
        case 1: return (int)slots[i].length;
        case 2: return (int)p->count;
        case 3: return (int)p->bss_size;
        default: return -1;
    }
}
int dnd_dol_dispose(int token) {
    int i = locate(token);
    if (i<0) return -6;
    if (!slots[i].queued) release(i); /* A queued launch belongs to bootstrap. */
    return 0;
}
int dnd_dol_request(int token) {
    int i = locate(token);
    if (pending) return -7;
    if (i<0 || !slots[i].ready) return -6;
    slots[i].queued = true;
    pending = token;
    return 0;
}
bool dnd_dol_pending(void) { return pending!=0; }
void dnd_dol_finish(bool success) {
    int i = locate(pending);
    pending = 0;
    if (i>=0 && success) {
        Stage *stage = slots[i].stage;
        /* No managed/runtime calls after platform shutdown starts. */
        dnd_dol_platform_launch(&stage->plan, stage->image, stage->stub);
    }
    for (i=0; i<SLOT_COUNT; i++) if (slots[i].token) release(i);
}
