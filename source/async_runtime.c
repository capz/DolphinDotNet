#define _POSIX_C_SOURCE 200809L
#include "dnd_async.h"
#include <stdlib.h>
#include <string.h>
#include <time.h>

#ifdef DND_GAMECUBE_STORAGE
#include <ogc/lwp.h>
#include <ogc/mutex.h>
#include <ogc/cond.h>
#include <ogc/lwp_watchdog.h>
typedef mutex_t AsyncMutex;
typedef cond_t AsyncCond;
typedef lwp_t AsyncThread;
static int mutex_init(AsyncMutex *m) { return LWP_MutexInit(m, false); }
static void lock(AsyncMutex *m) { LWP_MutexLock(*m); }
static void unlock(AsyncMutex *m) { LWP_MutexUnlock(*m); }
static void mutex_destroy(AsyncMutex *m) { LWP_MutexDestroy(*m); }
static int cond_init(AsyncCond *c) { return LWP_CondInit(c); }
static void signal_all(AsyncCond *c) { LWP_CondBroadcast(*c); }
static void cond_destroy(AsyncCond *c) { LWP_CondDestroy(*c); }
static void wait_cond(AsyncCond *c, AsyncMutex *m, bool timed) {
    if (timed) { struct timespec delay = {0, 1000000}; LWP_CondTimedWait(*c, *m, &delay); }
    else LWP_CondWait(*c, *m);
}
static uint64_t now_us(void) { return ticks_to_microsecs(gettime()); }
static int thread_start(AsyncThread *t, void *(*fn)(void *)) {
    /* Equal, timesliced priorities let both executors advance on the console's
     * single CPU even while native parsing has released the managed gate.
     * A lower priority service thread could starve behind a busy frame loop. */
    int priority = LWP_GetThreadPriority(LWP_GetSelf());
    if (priority < LWP_PRIO_LOWEST) priority = LWP_PRIO_NORMAL;
    if (priority > LWP_PRIO_HIGHEST) priority = LWP_PRIO_HIGHEST;
    return LWP_CreateThread(t, fn, NULL, NULL, 64 * 1024, (uint8_t)priority);
}
static void thread_join(AsyncThread *t) { LWP_JoinThread(*t, NULL); }
static void yield_thread(void) { LWP_YieldThread(); }
#else
#include <pthread.h>
#include <sched.h>
typedef pthread_mutex_t AsyncMutex;
typedef pthread_cond_t AsyncCond;
typedef pthread_t AsyncThread;
static int mutex_init(AsyncMutex *m) { return pthread_mutex_init(m, NULL); }
static void lock(AsyncMutex *m) { pthread_mutex_lock(m); }
static void unlock(AsyncMutex *m) { pthread_mutex_unlock(m); }
static void mutex_destroy(AsyncMutex *m) { pthread_mutex_destroy(m); }
static int cond_init(AsyncCond *c) { return pthread_cond_init(c, NULL); }
static void signal_all(AsyncCond *c) { pthread_cond_broadcast(c); }
static void cond_destroy(AsyncCond *c) { pthread_cond_destroy(c); }
static void wait_cond(AsyncCond *c, AsyncMutex *m, bool timed) {
    if (timed) {
        struct timespec deadline; clock_gettime(CLOCK_REALTIME, &deadline);
        deadline.tv_nsec += 1000000;
        if (deadline.tv_nsec >= 1000000000) { deadline.tv_sec++; deadline.tv_nsec -= 1000000000; }
        pthread_cond_timedwait(c, m, &deadline);
    } else pthread_cond_wait(c, m);
}
static uint64_t now_us(void) {
    struct timespec value; clock_gettime(CLOCK_MONOTONIC, &value);
    return (uint64_t)value.tv_sec * 1000000 + (uint64_t)value.tv_nsec / 1000;
}
static int thread_start(AsyncThread *t, void *(*fn)(void *)) { return pthread_create(t, NULL, fn, NULL); }
static void thread_join(AsyncThread *t) { pthread_join(*t, NULL); }
static void yield_thread(void) { sched_yield(); }
#endif

static AsyncMutex gate;
static AsyncCond changed;
static AsyncThread thread;
static DndExecutionContext contexts[2];
static DndAsyncDispatch dispatch;
static bool running, stopping, failed;
static int owner, dispatch_depth[2], critical_depth[2];
static uint64_t next_poll[2];

/* Called only with the managed execution gate held. This also isolates native
 * EH frames: a longjmp can never target the other executor's C stack. */
static int service(void) {
    DndEhFrame frame;
    volatile int result = -1;
    dispatch_depth[owner]++;
    dnd_eh_push(&frame);
    if (setjmp(frame.environment) == 0) result = dispatch(owner);
    dnd_eh_pop(&frame);
    dispatch_depth[owner]--;
    return result;
}

static void park(bool timed) {
    int current = owner;
    dnd_context_suspend(&contexts[current]);
    wait_cond(&changed, &gate, timed); /* atomically releases/reacquires gate */
    dnd_context_resume(&contexts[current]);
    owner = current;
}

static void yield_gate(void) {
    int current = owner;
    dnd_context_suspend(&contexts[current]);
    unlock(&gate);
    yield_thread();
    lock(&gate);
    dnd_context_resume(&contexts[current]);
    owner = current;
}

static void *background(void *unused) {
    (void)unused;
    lock(&gate);
    owner = 1;
    /* Initial worker context is empty; application context is parked. */
    dnd_context_resume(&contexts[1]);
    while (!stopping) {
        int pending = service();
        if (dnd_exception_pending()) { failed = true; break; }
        if (stopping) break; /* A callback can resume after shutdown began. */
        park(pending > 0);
    }
    dnd_context_suspend(&contexts[1]);
    signal_all(&changed);
    unlock(&gate);
    return NULL;
}

bool dnd_async_start(DndAsyncDispatch callback) {
    if (running || !callback) return false;
#ifndef DND_GAMECUBE_STORAGE
    /* Deterministic manual scheduler/queue characterization in legacy tests. */
    const char *manual = getenv("DND_ASYNC_MANUAL");
    if (manual && manual[0] == '1') return true;
#endif
    if (mutex_init(&gate) != 0) return false;
    if (cond_init(&changed) != 0) { mutex_destroy(&gate); return false; }
    memset(contexts, 0, sizeof(contexts));
    memset(dispatch_depth, 0, sizeof(dispatch_depth));
    memset(critical_depth, 0, sizeof(critical_depth));
    memset(next_poll, 0, sizeof(next_poll));
    dispatch = callback; owner = 0; stopping = false; failed = false;
    lock(&gate);
    if (thread_start(&thread, background) != 0) {
        unlock(&gate); cond_destroy(&changed); mutex_destroy(&gate); return false;
    }
    running = true;
    /* Return holding the gate: all generated managed code is serialized. */
    return true;
}

bool dnd_async_running(void) { return running; }
int dnd_async_context(void) { return owner; }
void dnd_async_wake(void) { if (running) signal_all(&changed); }

void dnd_async_progress(void) {
    if (!running) return;
    if (critical_depth[owner] != 0) { dnd_exception_throw(DND_EXCEPTION_NOT_SUPPORTED, "Pending task waits inside class initialization are unsupported."); return; }
    if (stopping && owner == 1 && !dnd_exception_pending()) { dnd_exception_throw(DND_EXCEPTION_OPERATION_CANCELED, "Runtime shutdown."); return; }
    if (failed) { dnd_exception_throw_object(contexts[1].exception_object); return; }
    (void)service();
    if (dnd_exception_pending()) { dnd_exception_rethrow(); return; }
    /* Timed waits cover nonblocking sockets/DMA, spurious wakes and wakeups
       occurring just before the wait. No task wait spins on the CPU. */
    park(true);
    if (failed) dnd_exception_throw_object(contexts[1].exception_object);
}

void dnd_async_safepoint(void) {
    if (!running) return;
    if (critical_depth[owner] != 0) return;
    if (stopping && owner == 1 && !dnd_exception_pending()) { dnd_exception_throw(DND_EXCEPTION_OPERATION_CANCELED, "Runtime shutdown."); return; }
    uint64_t now = now_us();
    if (now < next_poll[owner]) return;
    next_poll[owner] = now + 1000;
    if (dispatch_depth[owner] == 0 && !dnd_exception_pending()) {
        (void)service();
        if (dnd_exception_pending()) { dnd_exception_rethrow(); return; }
    }
    yield_gate();
    if (failed) dnd_exception_throw_object(contexts[1].exception_object);
}

void dnd_async_stop(void) {
    if (!running) return;
    stopping = true;
    dnd_context_suspend(&contexts[0]);
    signal_all(&changed);
    unlock(&gate);
    thread_join(&thread);
    lock(&gate);
    dnd_context_resume(&contexts[0]);
    dnd_context_unregister(&contexts[1]);
    owner = 0; running = false;
    unlock(&gate);
    cond_destroy(&changed); mutex_destroy(&gate);
    if (failed) dnd_exception_throw_object(contexts[1].exception_object);
}

void dnd_async_require_application_thread(void) {
    if (owner != 0) dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION, "Operation requires the application executor.");
}

void dnd_async_enter_critical(void) { critical_depth[owner]++; }
void dnd_async_leave_critical(void) { if (critical_depth[owner] > 0) critical_depth[owner]--; }

int dnd_async_native_begin(void) {
    if (!running) return -1;
    if (owner != 1 || critical_depth[owner] != 0) {
        dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION, "Native background work requires the service executor outside class initialization.");
        return -1;
    }
    dnd_context_suspend(&contexts[1]);
    unlock(&gate);
    return 1;
}

void dnd_async_native_end(int context) {
    if (context < 0) return;
    lock(&gate);
    dnd_context_resume(&contexts[context]);
    owner = context;
}
