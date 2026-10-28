#ifndef DND_ASYNC_H
#define DND_ASYNC_H
#include "dnd_managed.h"
/* Two serialized managed executors. The application owns graphics; the service
 * thread owns background dispatch. The execution gate protects the entire
 * managed heap, task queues, native bridges and exception state. */
typedef int (*DndAsyncDispatch)(int context);
bool dnd_async_start(DndAsyncDispatch dispatch);
void dnd_async_stop(void);
bool dnd_async_running(void);
int dnd_async_context(void); /* 0=application, 1=background */
void dnd_async_wake(void);
void dnd_async_wait_external(void); /* Release gate while external native work is pending. */
void dnd_async_dispatch_enter(void);
void dnd_async_dispatch_leave(void);
void dnd_async_service(void); /* Service with the gate held; caller must recheck its task. */
void dnd_async_wait(void); /* Park only if the rechecked task is still pending. */
void dnd_async_wait_reset(void);
void dnd_async_wait_skipped(void);
void dnd_async_deadline(int64_t due); /* Absolute monotonic microseconds, -1 if none. */
void dnd_async_deadline_waits(int enabled);
void dnd_async_timer_late(int64_t late);
void dnd_async_wait_snapshot(DndArray *data);
int64_t dnd_async_wait_stat(int kind);
void dnd_async_progress(void); /* Standard Task.Wait/GetResult integration. */
void dnd_async_productive_waits(int enabled);
int dnd_async_app_wait_begin(void);
void dnd_async_app_wait_end(int context);
void dnd_async_poll_stride(int stride);
int64_t dnd_async_poll_stat(int kind);
void dnd_async_safepoint(void); /* Application AOT blocks, never runtime internals. */
/* Read only while holding the managed execution gate. Atomic regions still
 * poll GC but cannot dispatch/yield, so avoid a native call at every block. */
extern bool dnd_async_active;
extern int dnd_async_owner, dnd_async_critical_depth[2];
extern bool dnd_async_stopping;
extern unsigned dnd_async_poll_budget[2];
extern uint64_t dnd_async_poll_calls;
DND_FAST_INLINE void dnd_async_poll(void) {
    int current=dnd_async_owner;
    if (dnd_async_active && !dnd_async_critical_depth[current]) {
        /* Preserve shutdown cancellation and exact poll counters. Only the
         * budget-decrement case avoids the full scheduler call. */
        if(dnd_async_poll_budget[current]>1&&!(dnd_async_stopping&&current==1)) {
            dnd_async_poll_budget[current]--;dnd_async_poll_calls++;return;
        }
        dnd_async_safepoint();
    }
}
void dnd_async_require_application_thread(void);
void dnd_async_enter_critical(void); /* Atomic class initialization. */
void dnd_async_leave_critical(void);
/* Service-thread native work only: suspend its managed context and release the
 * execution gate. Until end(), touch no managed objects, exception machinery,
 * storage tables or renderer/model globals. Reacquire around shared bridges.
 * The returned context is -1 in manual mode; always pair begin/end on the same
 * thread. Application-thread callers are rejected. Shutdown joins this work. */
int dnd_async_native_begin(void);
void dnd_async_native_end(int context);
#endif
