#include "dnd_worker.h"
#include <stddef.h>
#include <string.h>

#ifdef DND_GAMECUBE_STORAGE
#include <ogc/lwp.h>
#include <ogc/mutex.h>
#include <ogc/cond.h>
typedef mutex_t WorkerMutex;
typedef cond_t WorkerCond;
typedef lwp_t WorkerThread;
static int mutex_init(WorkerMutex *m) { return LWP_MutexInit(m, false); }
static void mutex_lock(WorkerMutex *m) { LWP_MutexLock(*m); }
static void mutex_unlock(WorkerMutex *m) { LWP_MutexUnlock(*m); }
static void mutex_close(WorkerMutex *m) { LWP_MutexDestroy(*m); }
static int cond_init(WorkerCond *c) { return LWP_CondInit(c); }
static void cond_wait(WorkerCond *c, WorkerMutex *m) { LWP_CondWait(*c, *m); }
static void cond_signal(WorkerCond *c) { LWP_CondSignal(*c); }
static void cond_broadcast(WorkerCond *c) { LWP_CondBroadcast(*c); }
static void cond_close(WorkerCond *c) { LWP_CondDestroy(*c); }
static int thread_start(WorkerThread *t, void *(*f)(void *)) {
    return LWP_CreateThread(t, f, NULL, NULL, 16 * 1024, 40);
}
static void thread_join(WorkerThread *t) { LWP_JoinThread(*t, NULL); }
#else
#include <pthread.h>
typedef pthread_mutex_t WorkerMutex;
typedef pthread_cond_t WorkerCond;
typedef pthread_t WorkerThread;
static int mutex_init(WorkerMutex *m) { return pthread_mutex_init(m, NULL); }
static void mutex_lock(WorkerMutex *m) { pthread_mutex_lock(m); }
static void mutex_unlock(WorkerMutex *m) { pthread_mutex_unlock(m); }
static void mutex_close(WorkerMutex *m) { pthread_mutex_destroy(m); }
static int cond_init(WorkerCond *c) { return pthread_cond_init(c, NULL); }
static void cond_wait(WorkerCond *c, WorkerMutex *m) { pthread_cond_wait(c, m); }
static void cond_signal(WorkerCond *c) { pthread_cond_signal(c); }
static void cond_broadcast(WorkerCond *c) { pthread_cond_broadcast(c); }
static void cond_close(WorkerCond *c) { pthread_cond_destroy(c); }
static int thread_start(WorkerThread *t, void *(*f)(void *)) {
    return pthread_create(t, NULL, f, NULL);
}
static void thread_join(WorkerThread *t) { pthread_join(*t, NULL); }
#endif

#define JOB_CAPACITY 32

typedef struct {
    uint32_t ticket;
    DndWorkerOperation operation;
    void *context;
    int result;
    int state; /* 0=free, 1=queued, 2=running, 3=completed */
} WorkerJob;

static WorkerMutex mutex;
static WorkerCond changed;
static WorkerThread thread;
static bool running, stopping;
static uint32_t next_ticket = 1;
static WorkerJob jobs[JOB_CAPACITY];
static unsigned cursor;

static int queued_locked(void) {
    for (unsigned n = 0; n < JOB_CAPACITY; ++n) {
        unsigned i = (cursor + n) % JOB_CAPACITY;
        if (jobs[i].state == 1) {
            cursor = (i + 1) % JOB_CAPACITY;
            return (int)i;
        }
    }
    return -1;
}

static void *worker_main(void *arg) {
    (void)arg;
    mutex_lock(&mutex);
    for (;;) {
        int i = queued_locked();
        if (i < 0) {
            if (stopping) break;
            cond_wait(&changed, &mutex);
            continue;
        }

        /* Release lock while running native-only operations. */
        WorkerJob *job = &jobs[i];
        DndWorkerOperation operation = job->operation;
        void *context = job->context;
        job->state = 2;
        mutex_unlock(&mutex);
        int value = operation(context);
        mutex_lock(&mutex);
        job->result = value;
        job->state = 3;
        cond_broadcast(&changed);
    }
    mutex_unlock(&mutex);
    return NULL;
}

bool dnd_worker_start(void) {
    if (running) return true; /* startup/shutdown are application-thread-owned */
    if (mutex_init(&mutex) != 0) return false;
    if (cond_init(&changed) != 0) {
        mutex_close(&mutex);
        return false;
    }
    memset(jobs, 0, sizeof(jobs));
    stopping = false;
    cursor = 0;
    if (thread_start(&thread, worker_main) != 0) {
        cond_close(&changed);
        mutex_close(&mutex);
        return false;
    }
    running = true;
    return true;
}

bool dnd_worker_running(void) { return running && !stopping; }

uint32_t dnd_worker_submit(DndWorkerOperation operation, void *context) {
    if (!running || !operation) return 0;
    mutex_lock(&mutex);
    if (stopping) { mutex_unlock(&mutex); return 0; }
    for (unsigned i = 0; i < JOB_CAPACITY; ++i) {
        if (jobs[i].state != 0) continue;
        uint32_t id = next_ticket++;
        if (!id) id = next_ticket++;
        jobs[i] = (WorkerJob){id, operation, context, 0, 1};
        cond_signal(&changed);
        mutex_unlock(&mutex);
        return id;
    }
    mutex_unlock(&mutex);
    return 0;
}

int dnd_worker_take(uint32_t ticket, int *result) {
    if (!running || ticket == 0) return -1;
    mutex_lock(&mutex);
    for (unsigned i = 0; i < JOB_CAPACITY; ++i) {
        WorkerJob *job = &jobs[i];
        if (job->ticket != ticket || job->state == 0) continue;
        if (job->state != 3) { mutex_unlock(&mutex); return 0; }
        if (result) *result = job->result;
        memset(job, 0, sizeof(*job));
        mutex_unlock(&mutex);
        return 1;
    }
    mutex_unlock(&mutex);
    return -1;
}

void dnd_worker_stop(void) {
    if (!running) return;
    mutex_lock(&mutex);
    stopping = true;
    cond_broadcast(&changed);
    mutex_unlock(&mutex);
    /* Worker completes queued work before exit. No managed GC access allowed. */
    thread_join(&thread);
    cond_close(&changed);
    mutex_close(&mutex);
    running = false;
}
