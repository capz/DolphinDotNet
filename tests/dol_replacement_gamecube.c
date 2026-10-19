#include <gccore.h>
#include <unistd.h>
volatile uint32_t dnd_dol_replacement_result;
static volatile uint32_t initialized = 0x12345678;
static volatile uint32_t zeroed[64];
static volatile uint32_t worker_result;
__attribute__((noinline,noreturn)) void dnd_dol_replacement_done(void) { for (;;) usleep(1000); }
static void *worker(void *unused) { (void)unused;worker_result=0x55aa;return NULL; }
int main(void) {
    bool ok=initialized==0x12345678 && worker_result==0;
    for (unsigned i=0;i<64;i++) if (zeroed[i]) ok=false;
    lwp_t thread;
    if (LWP_CreateThread(&thread,worker,NULL,NULL,8192,64)!=0) ok=false;
    else LWP_JoinThread(thread,NULL);
    if (worker_result!=0x55aa) ok=false;
    dnd_dol_replacement_result=ok ? 0x444f4c32 : 0xdead0001;
    dnd_dol_replacement_done();
}
