#define _POSIX_C_SOURCE 200809L
#include "dnd_dol.h"
#include "dnd_storage.h"
#include "dnd_async.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>
#include <unistd.h>

static int launched;
bool dnd_dol_platform_supported(void) { return true; }
size_t dnd_dol_platform_stub_size(void) { return 4; }
void dnd_dol_platform_stub_copy(void *p) { (void)p; }
void dnd_dol_platform_launch(DndDolPlan *plan,void *image,void *stub) {
    (void)image;(void)stub;assert(plan->entry==0x80004000);assert(!dnd_async_running());launched++;
}
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(int argc,char **argv) {
    (void)argv;
    char root[]="/tmp/dnd-dol-XXXXXX",dir[256],path[256];assert(mkdtemp(root));
    setenv("DND_STORAGE_ROOT",root,1);
    snprintf(dir,sizeof(dir),"%s/sd",root);assert(mkdir(dir,0700)==0);
    snprintf(path,sizeof(path),"%s/sd/next.dol",root);
    unsigned char bytes[9000]={0};
    bytes[2]=1;bytes[0x48]=0x80;bytes[0x49]=0;bytes[0x4a]=0x40;
    bytes[0x93]=32;bytes[0xe0]=0x80;bytes[0xe2]=0x40;
    FILE *f=fopen(path,"wb");assert(f);assert(fwrite(bytes,1,sizeof(bytes),f)==sizeof(bytes));assert(!fclose(f));
    assert(dnd_storage_mount(2));
    static unsigned char memory[512*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>1);
    intptr_t result=dnd_aot_entry(&heap);
    if(result || dnd_exception_pending()) {
        fprintf(stderr,"DOL managed result=%ld exception=%s\n",(long)result,dnd_exception_message());return 1;
    }
    assert(dnd_dol_pending() && !dnd_async_running() && dnd_fs_open_handles()==0);
    dnd_storage_shutdown();dnd_dol_finish(true);assert(launched==1);
    unlink(path);rmdir(dir);rmdir(root);
    puts("Managed DOL API, async preparation, cancellation, dispose, finally cleanup and deferred handoff passed.");
}
