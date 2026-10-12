#include "dnd_stw.h"

/* Collection only happens once every registered mutator has acknowledged
 * suspension. This entry point deliberately does not start threading or
 * install AOT hooks: callers must establish full root registration first.
 * Automatic allocator-triggered GC remains unsupported for parallel mutators.
 */
bool dnd_gc_collect_stopped(DndStwCoordinator *world, int collector_slot,
                            DndManagedHeap *heap, const DndRootSet *roots) {
    if (!world || !heap) return false;
    if (!dnd_stw_suspend_others(world, collector_slot)) return false;
    dnd_gc_collect(heap, roots);
    dnd_stw_resume_others(world, collector_slot);
    return true;
}
