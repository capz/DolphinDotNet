#ifndef DND_WORKER_H
#define DND_WORKER_H

#include <stdbool.h>
#include <stdint.h>

/* Native-only worker. NEVER pass managed heap objects, managed arrays or
 * managed callbacks: the AOT GC has no cross-thread stack/root coordination.
 * A job owns its opaque native context until completion is collected.
 * Jobs run serially on one persistent worker, outside the game thread.
 */
typedef int (*DndWorkerOperation)(void *native_context);

bool dnd_worker_start(void);
bool dnd_worker_running(void);
/* Positive ticket on success; zero when stopped or the 32-slot queue is full. */
uint32_t dnd_worker_submit(DndWorkerOperation operation, void *native_context);
/* 0=pending, 1=completed and consumed, -1=unknown ticket.
 * A ticket's context can be freed only after 1 has been returned. */
int dnd_worker_take(uint32_t ticket, int *result);
/* Wait without busy-spinning. Returns 1 and consumes the result, or -1 for
 * an unknown ticket. Do not call on the worker itself (would deadlock). */
int dnd_worker_wait(uint32_t ticket, int *result);
/* Drain queued work and join the worker before releasing any job contexts. */
void dnd_worker_stop(void);

#endif
