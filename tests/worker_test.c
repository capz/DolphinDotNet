#define _POSIX_C_SOURCE 200809L
#include "dnd_worker.h"
#include <assert.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <time.h>

static atomic_int started, gate, calls, ran_on_main;
static pthread_t main_thread;

static void nap(void) {
    struct timespec delay = {0, 1000000};
    nanosleep(&delay, NULL);
}

static int blocking_job(void *data) {
    if (pthread_equal(pthread_self(), main_thread)) atomic_store(&ran_on_main, 1);
    atomic_fetch_add(&started, 1);
    while (!atomic_load(&gate)) nap();
    atomic_fetch_add(&calls, 1);
    return *(const int *)data + 10;
}

int main(void) {
    main_thread = pthread_self();
    assert(!dnd_worker_running());
    assert(dnd_worker_take(1, NULL) == -1);
    assert(dnd_worker_submit(blocking_job, NULL) == 0);
    assert(dnd_worker_start());
    assert(dnd_worker_running());
    assert(dnd_worker_start()); /* idempotent */

    int values[32];
    uint32_t tickets[32];
    for (int i = 0; i < 32; ++i) {
        values[i] = i;
        tickets[i] = dnd_worker_submit(blocking_job, &values[i]);
        assert(tickets[i] != 0);
    }
    while (!atomic_load(&started)) nap();
    assert(dnd_worker_submit(blocking_job, &values[0]) == 0);
    assert(dnd_worker_take(tickets[0], NULL) == 0);
    atomic_store(&gate, 1);

    for (int i = 0; i < 32; ++i) {
        int result = -1;
        int done;
        do {
            done = dnd_worker_take(tickets[i], &result);
            if (done == 0) nap();
        } while (done == 0);
        assert(done == 1);
        assert(result == i + 10);
        assert(dnd_worker_take(tickets[i], NULL) == -1);
    }
    assert(atomic_load(&calls) == 32);
    assert(!atomic_load(&ran_on_main));
    assert(!dnd_worker_submit(NULL, NULL));
    dnd_worker_stop();
    assert(!dnd_worker_running());
    assert(dnd_worker_submit(blocking_job, &values[0]) == 0);
    assert(dnd_worker_start());
    uint32_t id = dnd_worker_submit(blocking_job, &values[0]);
    assert(id != 0);
    int waited_result = -1;
    assert(dnd_worker_wait(id, &waited_result) == 1);
    assert(waited_result == 10);
    assert(dnd_worker_wait(id, NULL) == -1);
    dnd_worker_stop(); /* drain even when result is not polled */
    assert(atomic_load(&calls) == 33);
    puts("Native worker queue and lifecycle tests passed.");
    return 0;
}
