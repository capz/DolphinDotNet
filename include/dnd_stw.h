#ifndef DND_STW_H
#define DND_STW_H

#include <stdbool.h>

/* Cooperative stop-the-world rendezvous for future managed mutator threads.
 * Not yet wired into the managed allocator or generated AOT safepoints.
 * Native threads must register, poll regularly, and unregister before exit.
 */
#define DND_STW_MAX_THREADS 8
typedef struct DndStwCoordinator DndStwCoordinator;

/* Platform-specific implementation owns the lock and condition variable.
 * Use create/destroy rather than allocating an opaque coordinator on stack.
 */
DndStwCoordinator *dnd_stw_create(void);
void dnd_stw_destroy(DndStwCoordinator *world);
/* Returns slot [0, 7], or -1 when full or a stop is active. */
int dnd_stw_register(DndStwCoordinator *world);
/* Caller must not hold the heap lock; unregister only after exiting managed code. */
void dnd_stw_unregister(DndStwCoordinator *world, int slot);
/* Called at compiler-inserted safepoints by registered mutators. */
void dnd_stw_safepoint(DndStwCoordinator *world, int slot);
/* Collector requests suspension and blocks until other registrants are parked.
 * Only one collector is supported. Caller must itself be a registered thread.
 * Every running managed thread must regularly poll its safepoint. */
bool dnd_stw_suspend_others(DndStwCoordinator *world, int collector_slot);
void dnd_stw_resume_others(DndStwCoordinator *world, int collector_slot);

#endif
