#define _POSIX_C_SOURCE 200809L
#include "dnd_async.h"
#include <assert.h>
#include <stdatomic.h>
#include <stdio.h>
#include <time.h>
static atomic_int ran;
static int service_wake;
static int dispatch(int context){if(context==1)atomic_store(&ran,1);else if(service_wake)dnd_async_wake();return 0;}
int main(void) {
 assert(dnd_async_start(dispatch));dnd_async_enter_critical();
 dnd_async_productive_waits(1);int context=dnd_async_app_wait_begin();assert(context==0);
 struct timespec pause={0,1000000};for(int i=0;i<1000&&!atomic_load(&ran);i++)nanosleep(&pause,0);
 dnd_async_app_wait_end(context);assert(dnd_async_context()==0);assert(atomic_load(&ran));
 dnd_async_leave_critical();dnd_async_poll_stride(64);
 int64_t before=dnd_async_poll_stat(1);
 for(int i=0;i<64;i++)dnd_async_safepoint();
 assert(dnd_async_poll_stat(1)-before==1);
 dnd_async_poll_stride(1);before=dnd_async_poll_stat(1);
 for(int i=0;i<64;i++)dnd_async_safepoint();
 assert(dnd_async_poll_stat(1)-before==64);
 // Split service lets Task.Wait recheck without parking after completion.
 service_wake=1;before=dnd_async_wait_stat(1);dnd_async_service();
 assert(dnd_async_wait_stat(1)==before);dnd_async_wait_skipped();assert(dnd_async_wait_stat(7)==1);
 // Legacy progress still parks after the same service for the A/B control.
 dnd_async_progress();assert(dnd_async_wait_stat(1)>before);service_wake=0;
 dnd_async_deadline_waits(1);dnd_async_deadline(0);before=dnd_async_wait_stat(1);
 dnd_async_wait();assert(dnd_async_wait_stat(1)==before); // Due deadlines yield to service.
 dnd_async_deadline(-1);dnd_async_wait();assert(dnd_async_wait_stat(1)>before);
 assert(dnd_async_wait_stat(2)>0&&dnd_async_wait_stat(4)>0);
 dnd_async_timer_late(27);assert(dnd_async_wait_stat(8)==27);
 dnd_async_wait_reset();assert(dnd_async_wait_stat(1)==0&&dnd_async_wait_stat(8)==0);
 dnd_async_stop();assert(!dnd_async_running());assert(!dnd_exception_pending());
 puts("Productive app wait restores context; bounded polling retains legacy mode: PASS");
}
