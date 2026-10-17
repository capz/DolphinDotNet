/* Real libogc PAD + managed Controller API. Only pacing/diagnostics are wrapped;
 * neither inputs nor managed expectations are written through the debugger. */
#include "dnd_managed.h"
#include "dnd_input.h"
#include <gccore.h>
#include <unistd.h>

volatile uint32_t dnd_input_test_case_index, dnd_input_test_result;
DndGamePad dnd_input_test_snapshot[4];
static unsigned frames;
static unsigned char storage[1024*1024];
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
void __real_dnd_input_poll(void);
__attribute__((noinline)) void dnd_input_test_case(void) { dnd_input_test_result=0xb0070200; }
__attribute__((noinline,noreturn)) void dnd_input_test_done(void) { for (;;) usleep(1000); }
void __wrap_dnd_input_poll(void) {
    if (frames%8==0) { dnd_input_test_case_index=frames/8; dnd_input_test_case(); }
    VIDEO_WaitVSync();
    __real_dnd_input_poll();
    for(unsigned p=0;p<4;p++) dnd_input_test_snapshot[p]=*dnd_input_gamepad(p);
    frames++;
}
int main(void) {
    dnd_input_test_result=0xb0070001;
    VIDEO_Init();
    GXRModeObj *mode=VIDEO_GetPreferredMode(NULL);
    void *fb=MEM_K0_TO_K1(SYS_AllocateFramebuffer(mode));
    VIDEO_Configure(mode);VIDEO_SetNextFramebuffer(fb);VIDEO_SetBlack(false);VIDEO_Flush();VIDEO_WaitVSync();
    dnd_input_test_result=0xb0070002;
    dnd_input_init();
    dnd_input_test_result=0xb0070003;
    for(unsigned i=0;i<60;i++) { VIDEO_WaitVSync();PAD_ScanPads();dnd_input_test_result=0xb0070100+i; }
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap,storage,sizeof(storage));
    dnd_input_test_result=0xffffffff;
    intptr_t result=dnd_aot_entry(&heap);
    dnd_input_test_result=result==0 && !dnd_exception_pending()?0x50414453:0xdead0000u|(uint32_t)result;
    dnd_input_test_done();
}
