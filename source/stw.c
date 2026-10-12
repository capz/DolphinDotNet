#include "dnd_stw.h"
#include <stdlib.h>

#ifdef DND_GAMECUBE_STORAGE
#include <ogc/lwp.h>
#include <ogc/mutex.h>
#include <ogc/cond.h>
typedef lwp_t StwThread;
static StwThread current_thread(void) { return LWP_GetSelf(); }
static bool same_thread(StwThread a, StwThread b) { return a == b; }
typedef mutex_t StwMutex;
typedef cond_t StwCond;
static int mutex_init(StwMutex *m) { return LWP_MutexInit(m, false); }
static void lock(StwMutex *m) { LWP_MutexLock(*m); }
static void unlock(StwMutex *m) { LWP_MutexUnlock(*m); }
static void mutex_destroy(StwMutex *m) { LWP_MutexDestroy(*m); }
static int cond_init(StwCond *c) { return LWP_CondInit(c); }
static void wait_cond(StwCond *c, StwMutex *m) { LWP_CondWait(*c, *m); }
static void signal_all(StwCond *c) { LWP_CondBroadcast(*c); }
static void cond_destroy(StwCond *c) { LWP_CondDestroy(*c); }
#else
#include <pthread.h>
typedef pthread_t StwThread;
static StwThread current_thread(void) { return pthread_self(); }
static bool same_thread(StwThread a, StwThread b) { return pthread_equal(a, b) != 0; }
typedef pthread_mutex_t StwMutex;
typedef pthread_cond_t StwCond;
static int mutex_init(StwMutex *m) { return pthread_mutex_init(m, NULL); }
static void lock(StwMutex *m) { pthread_mutex_lock(m); }
static void unlock(StwMutex *m) { pthread_mutex_unlock(m); }
static void mutex_destroy(StwMutex *m) { pthread_mutex_destroy(m); }
static int cond_init(StwCond *c) { return pthread_cond_init(c, NULL); }
static void wait_cond(StwCond *c, StwMutex *m) { pthread_cond_wait(c, m); }
static void signal_all(StwCond *c) { pthread_cond_broadcast(c); }
static void cond_destroy(StwCond *c) { pthread_cond_destroy(c); }
#endif

struct DndStwCoordinator {
    StwMutex mutex;
    StwCond changed;
    bool present[DND_STW_MAX_THREADS];
    StwThread owners[DND_STW_MAX_THREADS];
    bool parked[DND_STW_MAX_THREADS];
    bool stop_requested;
    int collector;
};

DndStwCoordinator *dnd_stw_create(void) {
    DndStwCoordinator *world = (DndStwCoordinator *)calloc(1, sizeof(*world));
    if (!world) return NULL;
    if (mutex_init(&world->mutex) != 0) { free(world); return NULL; }
    if (cond_init(&world->changed) != 0) {
        mutex_destroy(&world->mutex);
        free(world);
        return NULL;
    }
    world->collector = -1;
    return world;
}

void dnd_stw_destroy(DndStwCoordinator *world) {
    if (!world) return;
    /* Caller must join all registered threads before destroying the lock. */
    lock(&world->mutex);
    bool safe = !world->stop_requested;
    for (int i = 0; i < DND_STW_MAX_THREADS; ++i)
        if (world->present[i]) safe = false;
    unlock(&world->mutex);
    if (!safe) return; /* Never destroy synchronization held by a mutator. */
    cond_destroy(&world->changed);
    mutex_destroy(&world->mutex);
    free(world);
}

int dnd_stw_register(DndStwCoordinator *world) {
    if (!world) return -1;
    lock(&world->mutex);
    int slot = -1;
    StwThread owner = current_thread();
    for (int i = 0; i < DND_STW_MAX_THREADS; ++i)
        if (world->present[i] && same_thread(world->owners[i], owner)) {
            unlock(&world->mutex);
            return -1; /* a native thread may own only one slot */
        }
    if (!world->stop_requested)
        for (int i = 0; i < DND_STW_MAX_THREADS; ++i)
            if (!world->present[i]) {
                slot = i;
                world->present[i] = true;
                world->owners[i] = owner;
                break;
            }
    unlock(&world->mutex);
    return slot;
}

void dnd_stw_unregister(DndStwCoordinator *world, int slot) {
    if (!world || slot < 0 || slot >= DND_STW_MAX_THREADS) return;
    lock(&world->mutex);
    if (world->present[slot] && world->collector != slot &&
        same_thread(world->owners[slot], current_thread())) {
        world->present[slot] = false;
        world->parked[slot] = false;
        signal_all(&world->changed);
    }
    unlock(&world->mutex);
}

void dnd_stw_safepoint(DndStwCoordinator *world, int slot) {
    if (!world || slot < 0 || slot >= DND_STW_MAX_THREADS) return;
    lock(&world->mutex);
    if (world->present[slot] && same_thread(world->owners[slot], current_thread()) &&
        world->stop_requested && slot != world->collector) {
        world->parked[slot] = true;
        signal_all(&world->changed);
        while (world->stop_requested && world->present[slot])
            wait_cond(&world->changed, &world->mutex);
        world->parked[slot] = false;
    }
    unlock(&world->mutex);
}

int dnd_stw_current_slot(DndStwCoordinator *world) {
    if (!world) return -1;
    StwThread owner = current_thread();
    lock(&world->mutex);
    int slot = -1;
    for (int i = 0; i < DND_STW_MAX_THREADS; ++i)
        if (world->present[i] && same_thread(world->owners[i], owner)) {
            slot = i;
            break;
        }
    unlock(&world->mutex);
    return slot;
}

void dnd_stw_safepoint_current(DndStwCoordinator *world) {
    int slot = dnd_stw_current_slot(world);
    if (slot >= 0) dnd_stw_safepoint(world, slot);
}

void dnd_stw_managed_hook(void *world) {
    dnd_stw_safepoint_current((DndStwCoordinator *)world);
}

bool dnd_stw_suspend_others(DndStwCoordinator *world, int collector_slot) {
    if (!world || collector_slot < 0 || collector_slot >= DND_STW_MAX_THREADS)
        return false;
    lock(&world->mutex);
    if (world->stop_requested || !world->present[collector_slot] ||
        !same_thread(world->owners[collector_slot], current_thread())) {
        unlock(&world->mutex);
        return false;
    }
    world->stop_requested = true;
    world->collector = collector_slot;
    signal_all(&world->changed);
    for (;;) {
        bool everyone_parked = true;
        for (int i = 0; i < DND_STW_MAX_THREADS; ++i)
            if (i != collector_slot && world->present[i] && !world->parked[i])
                everyone_parked = false;
        if (everyone_parked) break;
        wait_cond(&world->changed, &world->mutex);
    }
    unlock(&world->mutex);
    return true;
}

void dnd_stw_resume_others(DndStwCoordinator *world, int collector_slot) {
    if (!world) return;
    lock(&world->mutex);
    if (world->stop_requested && world->collector == collector_slot &&
        same_thread(world->owners[collector_slot], current_thread())) {
        world->stop_requested = false;
        world->collector = -1;
        signal_all(&world->changed);
    }
    unlock(&world->mutex);
}
