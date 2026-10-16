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
void dnd_async_progress(void); /* Standard Task.Wait/GetResult integration. */
void dnd_async_safepoint(void); /* Application AOT blocks, never runtime internals. */
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
