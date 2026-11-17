#include "dnd_launch.h"
#include <stdint.h>

#ifdef DND_CAPTURE_LAUNCH_ARGUMENTS
#define DND_LAUNCH_MAX_ARGUMENTS 64
#define DND_LAUNCH_MAX_BYTES 4096
static char launch_bytes[DND_LAUNCH_MAX_BYTES];
static uint16_t launch_offsets[DND_LAUNCH_MAX_ARGUMENTS];
static int launch_count;
#if defined(GEKKO) || defined(DND_TEST_LAUNCH_BOUNDS)
static bool launch_mem1_range(uintptr_t address,size_t length) {
    return address>=0x80000000u && address<0x81800000u &&
        length<=0x81800000u-address;
}
#endif
static bool launch_readable(const void *pointer,size_t length) {
    uintptr_t address=(uintptr_t)pointer;
    if(!address || length>UINTPTR_MAX-address)return false;
#ifdef GEKKO
    return launch_mem1_range(address,length);
#else
    return true;
#endif
}
#endif

void dnd_launch_capture(int argc,const char *const *argv) {
#ifdef DND_CAPTURE_LAUNCH_ARGUMENTS
    launch_count=0;
    if(argc<=0 || argc>DND_LAUNCH_MAX_ARGUMENTS ||
       (uintptr_t)argv%sizeof(*argv) ||
       !launch_readable(argv,(size_t)argc*sizeof(*argv)))return;
    size_t used=0;
    for(int i=0;i<argc;i++) {
        const char *text=argv[i];
        if(!text)return;
        launch_offsets[i]=(uint16_t)used;
        for(size_t j=0;;j++) {
            uintptr_t address=(uintptr_t)text;
            if(used==sizeof(launch_bytes) || j>UINTPTR_MAX-address ||
               !launch_readable((const void *)(address+j),1))return;
            char value=*(const char *)(address+j);launch_bytes[used++]=value;
            if(!value)break;
        }
    }
    launch_count=argc; /* Publish only a complete, bounded snapshot. */
#else
    (void)argc;(void)argv;
#endif
}
int dnd_launch_count(void) {
#ifdef DND_CAPTURE_LAUNCH_ARGUMENTS
    return launch_count;
#else
    return 0;
#endif
}
DndString *dnd_launch_argument(DndManagedHeap *heap,int index) {
#ifdef DND_CAPTURE_LAUNCH_ARGUMENTS
    if(index<0 || index>=launch_count)return NULL;
    return dnd_string_from_utf8(heap,launch_bytes+launch_offsets[index]);
#else
    (void)heap;(void)index;return NULL;
#endif
}
