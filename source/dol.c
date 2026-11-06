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
    uint8_t arguments[4096];
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
static int locate(int token);

static uint32_t be32(const uint8_t *p) {
    return (uint32_t)p[0]<<24 | (uint32_t)p[1]<<16 | (uint32_t)p[2]<<8 | p[3];
}
static void put32(uint8_t *p,uint32_t value) {
    p[0]=(uint8_t)(value>>24);p[1]=(uint8_t)(value>>16);p[2]=(uint8_t)(value>>8);p[3]=(uint8_t)value;
}
int dnd_dol_arguments(int token,DndArray *arguments) {
    int i=locate(token);
    if(i<0||!slots[i].ready||slots[i].queued)return -6;
    if(!arguments||arguments->element_size!=1||arguments->length<2||arguments->length>4096||arguments->data[arguments->length-1]!=0)return -10;
    size_t count=0;bool start=true;
    for(size_t at=0;at<arguments->length;at++) {
        if(start&&arguments->data[at]==0)return -10;
        if(arguments->data[at]==0){count++;start=true;}else start=false;
    }
    if(count>64)return -10;
    Stage *stage=slots[i].stage;
    for(unsigned section=0;section<7;section++) {
        DndDolSection *s=&stage->plan.sections[section];
        if(stage->plan.entry<s->address||s->size<32||stage->plan.entry-s->address>s->size-32)continue;
        uint8_t *entry=stage->image+s->offset+(stage->plan.entry-s->address);
        if(be32(entry+4)!=0x5f617267u)return -9; /* libogc CRT _arg marker */
        memcpy(stage->arguments,arguments->data,arguments->length);
        put32(entry+8,0x5f617267u);put32(entry+12,DND_DOL_ARGUMENTS_ADDRESS);
        put32(entry+16,(uint32_t)arguments->length);
        memset(entry+20,0,12);
        stage->plan.arguments=stage->arguments;
        stage->plan.arguments_size=(uint32_t)arguments->length;
        /* Packed loaders can overwrite the caller heap before libogc copies
         * argv. Final shutdown moves this bounded block below all accepted DOL
         * destinations. Do not write reserved low memory while libogc runs. */
        return 0;
    }
    return -9;
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
        if (i<7 && section->size>=4 && plan->entry >= section->address && plan->entry-section->address <= section->size-4) executable = true;
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
/* Find an aligned active stage after any overlapping destinations. The malloc
 * prefix may be overwritten after shutdown; the plan/stub/image may not. */
uintptr_t dnd_dol_stage_location(const DndDolPlan *plan,uintptr_t start,size_t size) {
    if(!plan||!size||start>UINTPTR_MAX-31)return 0;
    uintptr_t candidate=(start+31)&~(uintptr_t)31;
    for(unsigned pass=0;pass<DND_DOL_SECTIONS+2;pass++){
        if(candidate>UINTPTR_MAX-size)return 0;
        uintptr_t next=candidate;
        if(overlaps(candidate,size,plan->bss_address,plan->bss_size))next=(uintptr_t)plan->bss_address+plan->bss_size;
        for(unsigned i=0;i<DND_DOL_SECTIONS;i++){
            const DndDolSection *s=&plan->sections[i];
            if(overlaps(candidate,size,s->address,s->size)&&(uintptr_t)s->address+s->size>next)next=(uintptr_t)s->address+s->size;
        }
        if(next==candidate)return candidate;
        if(next>UINTPTR_MAX-31)return 0;
        candidate=(next+31)&~(uintptr_t)31;
    }
    return 0;
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
    size_t required=sizeof(Stage)+length,padding=31;
    void *allocation=NULL;Stage *stage=NULL;
    for(unsigned attempt=0;attempt<DND_DOL_SECTIONS+2;attempt++){
        if(padding>DND_DOL_MAX_SIZE||required>DND_DOL_MAX_SIZE-padding)return -2;
        size_t size=required+padding;
        allocation=malloc(size);
        if(!allocation)return -2;
        uintptr_t address=dnd_dol_stage_location(&plan,(uintptr_t)allocation,required);
        if(!address){free(allocation);return -3;}
        size_t skip=address-(uintptr_t)allocation;
        if(skip<=size-required){stage=(Stage *)address;break;}
        free(allocation);allocation=NULL;
        if(skip>SIZE_MAX-31)return -3;
        padding=skip+31;
    }
    if(!stage)return -3;
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
