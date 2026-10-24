#include "protocol.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

static void put16(uint8_t *p, unsigned n) { p[0]=(uint8_t)(n>>8); p[1]=(uint8_t)n; }
static void put32(uint8_t *p, uint32_t n) { p[0]=(uint8_t)(n>>24); p[1]=(uint8_t)(n>>16); p[2]=(uint8_t)(n>>8); p[3]=(uint8_t)n; }
static uint32_t be32(const uint8_t *p) { return (uint32_t)p[0]<<24 | (uint32_t)p[1]<<16 | (uint32_t)p[2]<<8 | p[3]; }
static void fixture(uint8_t image[256]) {
    memset(image,0,256); memcpy(image,"\177ELF\1\2\1",7);
    put16(image+16,2); put16(image+18,20); put32(image+20,1);
    put32(image+24,0x80004000); put32(image+28,52);
    put16(image+40,52); put16(image+42,32); put16(image+44,1);
    put32(image+52,1); put32(image+56,128); put32(image+60,0x80004000); put32(image+64,0x80004000);
    put32(image+68,32); put32(image+72,64); put32(image+76,5); put32(image+80,32);
    memset(image+128,0x5a,32);
}
typedef struct {
    uint8_t input[1024], output[128], image[512];
    size_t length, position, written, limit;
    unsigned allocations, states, fail_write;
} Peer;
static int rd(void *context, void *buffer, size_t size) {
    Peer *p=context;
    if (size>3) size=3;
    if (size>p->length-p->position) size=p->length-p->position;
    memcpy(buffer,p->input+p->position,size); p->position+=size; return (int)size;
}
static int wr(void *context, const void *buffer, size_t size) {
    Peer *p=context;
    if (p->fail_write && p->written>=p->fail_write) return -1;
    if (size>2) size=2;
    assert(p->written+size<=sizeof(p->output));
    memcpy(p->output+p->written,buffer,size); p->written+=size; return (int)size;
}
static uint8_t *alloc(void *context, size_t size, uintptr_t *address, size_t *extent) {
    Peer *p=context; p->allocations++;
    *address=0x81700000; *extent=0x10000;
    return size<=p->limit ? p->image : NULL;
}
static void state(void *context, unsigned n) { ((Peer *)context)->states |= 1u<<n; }
static void frame(uint8_t *p, unsigned kind, uint32_t value, uint32_t detail) {
    memcpy(p,"DNDL\0\1\0",7); p[7]=(uint8_t)kind; put32(p+8,value); put32(p+12,detail);
}
static void prepare(Peer *p, unsigned last) {
    memset(p,0,sizeof(*p)); p->limit=sizeof(p->image);
    fixture(p->input+32);
    frame(p->input,DND_HELLO,0x12345678,0);
    frame(p->input+16,DND_UPLOAD,256,dnd_launcher_crc32(p->input+32,256));
    frame(p->input+288,last,0x12345678,0); frame(p->input+304,DND_COMMIT,0x12345678,0); p->length=320;
}
static int session(Peer *peer) {
    DndLauncherIo io={peer,rd,wr,alloc,state,512};
    DndElfPlan plan; uint8_t *image=NULL;
    int r=dnd_launcher_session(&io,&image,&plan);
    assert((image!=NULL)==(r==1));
    if (r==1) assert(plan.entry==0x80004000 && plan.count==1 && image==peer->image);
    return r;
}
int main(void) {
    uint8_t elf[256]; DndElfPlan plan;
    fixture(elf);
    assert(dnd_launcher_crc32((const uint8_t *)"123456789",9)==0xcbf43926);
    assert(dnd_elf_validate(elf,256,0x81700000,0x10000,&plan));
    assert(plan.segments[0].file_size==32 && plan.segments[0].memory_size==64);
    assert(!dnd_elf_validate(elf,256,0x80004020,0x20,&plan)); /* BSS collision. */
    for (size_t n=0; n<84; n++) assert(!dnd_elf_validate(elf,n,0x81700000,0x10000,&plan));
    const unsigned positions[]={4,5,6,16,18,20,24,28,40,42,44,52,56,60,64,68,72,76,80};
    for (unsigned i=0; i<sizeof(positions)/sizeof(*positions); i++) {
        fixture(elf); elf[positions[i]]=0xff;
        assert(!dnd_elf_validate(elf,256,0x81700000,0x10000,&plan));
    }
    fixture(elf); put16(elf+44,2); memcpy(elf+84,elf+52,32);
    assert(!dnd_elf_validate(elf,256,0x81700000,0x10000,&plan)); /* Overlapping PT_LOADs. */
    fixture(elf); put32(elf+68,65);
    assert(!dnd_elf_validate(elf,256,0x81700000,0x10000,&plan)); /* filesz > memsz. */
    fixture(elf); put32(elf+24,0x80004020);
    assert(!dnd_elf_validate(elf,256,0x81700000,0x10000,&plan)); /* Entry in BSS. */
    fixture(elf); put32(elf+52,2);
    assert(!dnd_elf_validate(elf,256,0x81700000,0x10000,&plan)); /* PT_DYNAMIC. */
    fixture(elf); put32(elf+24,0x815fffc0); put32(elf+60,0x815fffc0); put32(elf+64,0x815fffc0);
    assert(dnd_elf_validate(elf,256,0x81400000,0x10000,&plan)); /* Ends exactly at resident base. */
    put32(elf+72,65);
    assert(!dnd_elf_validate(elf,256,0x81400000,0x10000,&plan)); /* One BSS byte destroys receiver. */
    put32(elf+72,64); put32(elf+24,0x81600000); put32(elf+60,0x81600000); put32(elf+64,0x81600000);
    assert(!dnd_elf_validate(elf,256,0x81400000,0x10000,&plan)); /* Resident code/image. */
    Peer p;
    prepare(&p,DND_EXECUTE); assert(session(&p)==1 && p.written==48);
    assert(p.output[7]==DND_READY && be32(p.output+8)==0x12345678 && be32(p.output+12)==512);
    assert(p.output[23]==DND_ACCEPTED && p.output[39]==DND_EXECUTING);
    assert(p.states==((1u<<DND_READY)|(1u<<DND_UPLOAD)|(1u<<DND_ACCEPTED)|(1u<<DND_EXECUTING)));
    prepare(&p,DND_CANCEL); assert(session(&p)==0);
    prepare(&p,DND_EXECUTE); frame(p.input+16,DND_CANCEL,0x12345678,0); p.length=32;
    assert(session(&p)==0 && !p.allocations && p.written==16);
    prepare(&p,DND_EXECUTE); p.input[32]^=1;
    assert(session(&p)==-1 && p.output[23]==DND_ERROR && be32(p.output+24)==DND_BAD_CRC);
    prepare(&p,DND_EXECUTE); p.input[32]=0;
    put32(p.input+28,dnd_launcher_crc32(p.input+32,256));
    assert(session(&p)==-1 && be32(p.output+24)==DND_BAD_ELF);
    prepare(&p,DND_EXECUTE);
    put32(p.input+32+24,0x81600000); put32(p.input+32+60,0x81600000); put32(p.input+32+64,0x81600000);
    put32(p.input+28,dnd_launcher_crc32(p.input+32,256));
    assert(session(&p)==-1 && p.written==32 && be32(p.output+24)==DND_BAD_ELF);
    assert(!(p.states & ((1u<<DND_ACCEPTED)|(1u<<DND_EXECUTING))));
    prepare(&p,DND_EXECUTE); put32(p.input+24,513);
    assert(session(&p)==-1 && !p.allocations && be32(p.output+24)==DND_TOO_LARGE);
    prepare(&p,DND_EXECUTE); p.limit=0;
    assert(session(&p)==-1 && be32(p.output+24)==DND_NO_MEMORY);
    prepare(&p,DND_EXECUTE); p.input[299]^=1; assert(session(&p)==-1); /* Wrong nonce. */
    prepare(&p,DND_EXECUTE); p.fail_write=32; assert(session(&p)==-1); /* Cannot ACK execution. */
    prepare(&p,DND_EXECUTE); frame(p.input+304,DND_CANCEL,0x12345678,0); assert(session(&p)==0);
    prepare(&p,DND_EXECUTE); frame(p.input+304,DND_COMMIT,0x87654321,0); assert(session(&p)==-1);
    for (size_t i=0; i<320; i++) { prepare(&p,DND_EXECUTE); p.length=i; assert(session(&p)!=1); }
    puts("ELF validation, staging protection, CRC32, readiness, short I/O, cancellation and execution gate passed.");
    return 0;
}
