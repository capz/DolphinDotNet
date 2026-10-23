#include "protocol.h"
#include <string.h>

static uint32_t be32(const uint8_t *p) { return (uint32_t)p[0]<<24 | (uint32_t)p[1]<<16 | (uint32_t)p[2]<<8 | p[3]; }
static void put32(uint8_t *p, uint32_t v) { p[0]=(uint8_t)(v>>24); p[1]=(uint8_t)(v>>16); p[2]=(uint8_t)(v>>8); p[3]=(uint8_t)v; }
static bool exact(DndLauncherIo *io, void *data, size_t size, bool writing) {
    uint8_t *p=data;
    while (size) {
        size_t chunk=size>3968 ? 3968 : size;
        int n=writing ? io->write(io->context, p, chunk) : io->read(io->context, p, chunk);
        if (n<=0 || (size_t)n>chunk) return false;
        p+=n; size-=(size_t)n;
    }
    return true;
}
static bool header(DndLauncherIo *io, uint8_t h[16]) {
    return exact(io, h, 16, false) && !memcmp(h, "DNDL\0\1\0", 7);
}
static bool send(DndLauncherIo *io, unsigned kind, uint32_t value, uint32_t detail) {
    uint8_t h[16]={'D','N','D','L',0,1,0,(uint8_t)kind};
    put32(h+8,value); put32(h+12,detail);
    return exact(io,h,sizeof(h),true);
}
static int reject(DndLauncherIo *io, unsigned code) { send(io,DND_ERROR,code,0); return -1; }
int dnd_launcher_session(DndLauncherIo *io, uint8_t **image, DndElfPlan *plan) {
    uint8_t h[16];
    *image=NULL;
    if (!header(io,h) || h[7]!=DND_HELLO || be32(h+12)) return reject(io,DND_BAD_REQUEST);
    uint32_t nonce=be32(h+8);
    size_t maximum=io->maximum_image;
    if (maximum>DND_LAUNCHER_MAX_IMAGE) maximum=DND_LAUNCHER_MAX_IMAGE;
    if (!send(io,DND_READY,nonce,(uint32_t)maximum)) return -1;
    if (io->status) io->status(io->context,DND_READY);
    if (!header(io,h)) return -1;
    if (h[7]==DND_CANCEL && be32(h+8)==nonce && !be32(h+12)) return 0;
    if (h[7]!=DND_UPLOAD) return reject(io,DND_BAD_REQUEST);
    uint32_t length=be32(h+8), crc=be32(h+12);
    if (length<52 || length>maximum) return reject(io,DND_TOO_LARGE);
    uintptr_t stage=0; size_t stage_size=0;
    uint8_t *buffer=io->allocate(io->context,length,&stage,&stage_size);
    if (!buffer) return reject(io,DND_NO_MEMORY);
    if (io->status) io->status(io->context,DND_UPLOAD);
    if (!exact(io,buffer,length,false)) return -1;
    if (dnd_launcher_crc32(buffer,length)!=crc) return reject(io,DND_BAD_CRC);
    if (!dnd_elf_validate(buffer,length,stage,stage_size,plan)) return reject(io,DND_BAD_ELF);
    if (!send(io,DND_ACCEPTED,length,crc)) return -1;
    if (io->status) io->status(io->context,DND_ACCEPTED);
    if (!header(io,h)) return -1;
    if (h[7]==DND_CANCEL && be32(h+8)==nonce && !be32(h+12)) return 0;
    if (h[7]!=DND_EXECUTE || be32(h+8)!=nonce || be32(h+12)) return reject(io,DND_BAD_REQUEST);
    if (!send(io,DND_EXECUTING,nonce,0)) return -1;
    /* Do not shut down the NIC until the host proves it received EXECUTING. */
    if (!header(io,h)) return -1;
    if (h[7]==DND_CANCEL && be32(h+8)==nonce && !be32(h+12)) return 0;
    if (h[7]!=DND_COMMIT || be32(h+8)!=nonce || be32(h+12)) return reject(io,DND_BAD_REQUEST);
    if (io->status) io->status(io->context,DND_EXECUTING);
    *image=buffer;
    return 1;
}
