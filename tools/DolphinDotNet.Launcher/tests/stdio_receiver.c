/* Host-only integration peer: runs the production protocol/ELF validation,
 * reports the resulting plan, and NEVER executes uploaded machine code. */
#include "protocol.h"
#include <stdio.h>
#include <stdlib.h>
static int rd(void *p, void *b, size_t n) { (void)p; return (int)fread(b,1,n,stdin); }
static int wr(void *p, const void *b, size_t n) { (void)p; size_t r=fwrite(b,1,n,stdout); fflush(stdout); return (int)r; }
static uint8_t *alloc(void *p, size_t n, uintptr_t *start, size_t *extent) {
    uint8_t **allocation=p; *allocation=malloc(n); *start=0x81700000; *extent=0x10000; return *allocation;
}
int main(void) {
    uint8_t *allocation=NULL, *image=NULL; DndElfPlan plan;
    DndLauncherIo io={&allocation,rd,wr,alloc,NULL,DND_LAUNCHER_MAX_IMAGE};
    int result=dnd_launcher_session(&io,&image,&plan);
    if (result==1) fprintf(stderr,"VALIDATED entry=%08x segments=%u\n",plan.entry,plan.count);
    free(allocation);
    return result<0 ? 1 : 0;
}
