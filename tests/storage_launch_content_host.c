#define _POSIX_C_SOURCE 200809L
#include "dnd_managed.h"
#include "dnd_launch.h"
#include "dnd_storage.h"
#include <stdio.h>
#include <stdlib.h>

void dnd_console_write_line(const char *text) { puts(text); }
void dnd_console_write_managed_line(DndString *text) {
    char output[512];unsigned count=text->length<511?text->length:511;
    for(unsigned i=0;i<count;i++)output[i]=text->chars[i]<128?(char)text->chars[i]:'?';
    output[count]=0;puts(output);
}
uint16_t dnd_input_buttons_down(unsigned port) { (void)port;return 0; }
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
#ifdef DND_TEST_LAUNCH_CONTENT_IMAGE
void launch_content_image_finish(void);
#endif

int main(int argc,char **argv) {
    if(argc<3)return 90;
    int scenario=atoi(argv[1]);
    const char *incoming[]={argv[2],argv[1]};
    dnd_launch_capture(scenario==0?0:2,incoming);
    static unsigned char memory[1024*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>3);
    intptr_t result=dnd_aot_entry(&heap);
    int handles=dnd_fs_open_handles();
    if(result||dnd_exception_pending()||handles){
        fprintf(stderr,"Launch content scenario=%d result=%ld handles=%d exception=%s\n",
                scenario,(long)result,handles,dnd_exception_message());
        return 1;
    }
#ifdef DND_TEST_LAUNCH_CONTENT_IMAGE
    launch_content_image_finish();
#endif
    printf("Launch content scenario %d passed%s\n",scenario,argc>3?" with GC stress":"");
    return 0;
}
