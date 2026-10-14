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
#endif
