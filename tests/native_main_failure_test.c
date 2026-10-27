/* cc -std=c11 -Wall -Wextra -Werror -Iinclude tests/native_main_failure_test.c -o /tmp/dnd-main-test */
#include <assert.h>
#include <stdio.h>
#include <string.h>
#define main dnd_native_main
#include "../source/main.c"
#undef main

static jmp_buf stopped;
static intptr_t entry_result;
static bool pending, waited, reset, finished_ok;
static char output[2048];
const DndType DND_TYPE_EXCEPTION = { .name="System.Exception" };
static DndException error = { .object={ .type=&DND_TYPE_EXCEPTION } };

void dnd_platform_init(void) {}
void dnd_managed_heap_init(DndManagedHeap *h,void *m,size_t n) {(void)h;(void)m;(void)n;}
intptr_t dnd_aot_entry(DndManagedHeap *h) {(void)h;return entry_result;}
void dnd_audio_shutdown(void) {}
int dnd_network_init(void) {return -1;}
int dnd_net_status(void) {return -1;}
int dnd_net_stop(void) {return 0;}
void dnd_input_shutdown(void) {}
void dnd_storage_shutdown(void) {}
bool dnd_dol_pending(void) {return false;}
void dnd_dol_finish(bool success) {finished_ok=success;}
bool dnd_exception_pending(void) {return pending;}
const char *dnd_exception_message(void) {return "Managed heap exhausted.";}
DndObject *dnd_exception_object(void) {return &error.object;}
bool dnd_type_is_assignable_from(const DndType *a,const DndType *b) {return a==b;}
DndString *dnd_exception_get_message(DndException *e) {return e->message;}
void dnd_device_shutdown(void) {reset=true;}
void dnd_console_set_visible(int v) {assert(v==1);}
void dnd_console_write_line(const char *s) {
    assert(strlen(output)+strlen(s)+2<sizeof(output));
    strcat(output,s);strcat(output,"\n");
}
void dnd_platform_wait_forever(void) {
    waited=true;
    assert(reset);
    longjmp(stopped,1);
}
static void run(int result,bool exception) {
    entry_result=result;pending=exception;
    waited=reset=finished_ok=false;output[0]=0;
    if(setjmp(stopped)==0)assert(dnd_native_main()==0);
}
int main(void) {
    run(0,false);
    assert(!waited && !reset && finished_ok);
    assert(strstr(output,"MANAGED MAIN: OK"));
    run(1,false);
    assert(waited && !finished_ok);
    assert(strstr(output,"MANAGED MAIN: FAILED"));
    run(0,true);
    assert(waited && !finished_ok);
    assert(strstr(output,"Managed heap exhausted."));
    /* Managed exception messages are UTF-16 and must not require allocation. */
    struct {DndObject object;uint32_t length;uint16_t chars[100];} message={0};
    message.length=100;
    for(int i=0;i<100;i++)message.chars[i]='X';
    message.chars[1]=0x1234;
    error.message=(DndString *)&message;
    run(1,true);
    assert(waited && strstr(output,"X?XXX"));
    assert(strstr(output,"START: EXIT"));
    puts("native main failure diagnostics: PASS");
    return 0;
}
