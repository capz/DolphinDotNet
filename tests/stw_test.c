#define _POSIX_C_SOURCE 200809L
#include "dnd_stw.h"
#include <assert.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <time.h>

static DndStwCoordinator *world;
static atomic_int ready, iterations, quit;
static int worker_slot = -1;

static void nap(void) {
    struct timespec delay = {0, 2000000};
    nanosleep(&delay, NULL);
}

static void *mutator(void *unused) {
    (void)unused;
    worker_slot = dnd_stw_register(world);
    assert(worker_slot >= 0);
    assert(dnd_stw_current_slot(world) == worker_slot);
    assert(dnd_stw_register(world) == -1);
    atomic_store(&ready, 1);
    while (!atomic_load(&quit)) {
        atomic_fetch_add(&iterations, 1);
        dnd_stw_managed_hook(world);
    }
    dnd_stw_unregister(world, worker_slot);
    return NULL;
}

int main(void) {
    world = dnd_stw_create();
    assert(world);
    int owner = dnd_stw_register(world);
    assert(owner >= 0);
    assert(dnd_stw_current_slot(world) == owner);
    assert(dnd_stw_register(world) == -1);
    pthread_t worker;
    assert(pthread_create(&worker, NULL, mutator, NULL) == 0);
    while (!atomic_load(&ready)) nap();
    assert(dnd_stw_current_slot(world) == owner);
    /* A thread cannot impersonate another registered mutator slot. */
    dnd_stw_unregister(world, worker_slot);
    while (atomic_load(&iterations) < 10) nap();

    assert(dnd_stw_suspend_others(world, owner));
    assert(!dnd_stw_suspend_others(world, owner));
    int paused = atomic_load(&iterations);
    nap();
    assert(atomic_load(&iterations) == paused);
    dnd_stw_resume_others(world, owner);

    while (atomic_load(&iterations) <= paused) nap();
    assert(dnd_stw_suspend_others(world, owner));
    dnd_stw_resume_others(world, owner);
    atomic_store(&quit, 1);
    assert(pthread_join(worker, NULL) == 0);
    assert(!dnd_stw_suspend_others(world, worker_slot));
    dnd_stw_unregister(world, owner);
    dnd_stw_destroy(world);
    puts("Cooperative stop-the-world rendezvous tests passed.");
    return 0;
}
