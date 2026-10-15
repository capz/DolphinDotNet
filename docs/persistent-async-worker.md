# Persistent async runtime

The runtime now provides automatic async progress through a persistent service thread and an execution gate. Applications use ordinary async Main and await; they do not write task/network pump loops. See [automatic async/await](async-runtime.md) for continuation placement, task APIs, shutdown behavior and verification.

`source/async_runtime.c` implements libogc LWP and POSIX backends. Generated application safe points and standard Task waits dispatch application-thread continuations automatically. The service thread runs background tasks, network service and ARAM work. Model source parsers use native-only scopes that release the gate while parsing/decoding; storage and model publication resume their saved service context under the gate. Both executors share one serialized managed execution gate; paused native stacks, GC roots and exception frames are registered and restored at handoff. This avoids parallel access to the allocator, task/cancellation queues and managed/native buffers.

The older `source/worker.c` queue remains a separate native-only facility with native context ownership. It is no longer started as an unused queue by the GameCube entry point. General parallel managed threading remains outside this implementation; the two runtime-owned executors are the supported scheduling model.

## Historical foundations

The following sections describe the earlier incremental groundwork. Their restrictions apply to standalone use of those primitives. Automatic async handoffs use the execution gate and dedicated suspend/resume API described above; they do not enable arbitrary concurrent managed mutators or install the optional stop-the-world hook.

## Managed GC-root registration groundwork

The managed runtime now supports `dnd_gc_register_roots` and `dnd_gc_unregister_roots`. Callers can register a `DndRootSet` whose rooted managed objects remain alive across GC collections, even when the immediate stack frame is no longer participating. Tests verify that a registered managed object remains live during an explicit collection.

**Safety boundary:** The registration list and managed allocator remain unsynchronized. Registering/unregistering roots, allocating, and collecting must all happen under the existing single-managed-thread assumption. This is the GC *root-enumeration foundation*, not a declaration that managed workers or background continuations are safe. Before enabling cross-thread managed work, add a native thread registry, stop-the-world safepoints, per-thread exception/GC frames, and synchronized access to heap and scheduler state, then test nested exceptions and forced GC on multiple threads.

## Sequential execution-context isolation

`dnd_context_switch(from, to)` saves and restores managed GC-frame pointers and pending exception state, allowing separate *sequentially executed* managed contexts to retain independent exception information. It rejects null/same-context switches and switches while an exception handler is active, because jumping across native stacks is invalid. A host regression test verifies pending exception isolation across switches.

This is **not managed multi-thread support**. There is no safe-point handshake or allocator locking, and concurrently executing managed code remains prohibited. Before collecting while another context is inactive, its live object slots and exception object must be explicitly included in registered GC roots; the sequential switch alone does not make inactive execution-context roots visible to the collector. Do not enable worker-thread managed callbacks based solely on this API.

## Inactive execution contexts are GC roots

The new `dnd_context_register` and `dnd_context_unregister` functions allow sequentially suspended managed contexts to be traced by GC. The collector now marks their pending exception objects and all objects referenced by saved GC frames; a regression test collects while a context holding an exception is inactive and checks that the exception survives. Callers must register inactive contexts before a collection can occur and unregister them before the context or its saved frame storage is destroyed.

This is still **single-managed-thread-only**. Registration, context switching, allocation, and collections are not synchronized between threads. The next milestone is a thread registry, mutual exclusion for heap operations, per-thread root and exception handling, and stop-the-world safepoints before actually executing managed continuations on the native worker.

## Nested root verification for suspended continuations

The follow-up regression test suspends a context with two nested GC frames, collects from another sequential context, and verifies both objects survive. Registration of a context pointing at the currently active GC frame stack is now rejected. This protects against accidentally representing a running context as suspended and tracing its mutable frames through the inactive-context registry.

These checks do **not** make the collector thread-safe. The heap, global exception state, GC frame registry and managed scheduler are still single-threaded. Native worker jobs must remain native-only until explicit thread registration, stop-the-world synchronization and thread-safe managed scheduling are implemented and tested.

### Active-frame registration safety

Inactive root registration now refuses any execution context whose saved GC-frame chain overlaps the current active frame chain (not merely the head frame). This avoids accidentally traversing an active stack as though it were suspended. The nested-root regression verifies preservation across GC and rejection of both the top and an older active frame.

## Cooperative stop-the-world rendezvous primitive

`include/dnd_stw.h` and `source/stw.c` introduce a native thread coordinator with up to eight registered thread slots. A collector calls `dnd_stw_suspend_others(world, slot)`; this waits until all other registered mutators explicitly reach `dnd_stw_safepoint` and park. After scanning and collection, `dnd_stw_resume_others` wakes the parked threads. The coordinator sleeps on condition variables rather than spin-polling. Host tests exercise repeated pause/resume cycles and check that a mutator cannot advance while parked.

**This is an unintegrated safety primitive, not a complete GC solution.** Compiler-generated managed code still needs safepoints at method entries and backward branches, GC allocation must be synchronized, all native thread stacks/exception states must be registered and traversed, and workers blocked in I/O must have a path to reach the rendezvous. The coordinator must not be enabled for arbitrary managed worker callbacks before these conditions are met.

### AOT safepoint insertion

Both C compiler backends now emit `dnd_gc_safepoint()` at managed basic-block/label entries after establishing method GC roots. The hook defaults to a no-op; a startup-installed native callback may later rendezvous with the stop-the-world coordinator. This supplies loop-backedge opportunities for the collector to park active managed code. Installing the callback and registering mutators across real threads is **not yet enabled**: safe thread-local GC frame/exception state, heap synchronization and thread ownership must be implemented first. Until then the emitted checks preserve the existing cooperative execution model.

### Native thread identity and safepoint callback

Coordinator registrations now bind to the calling libogc LWP or POSIX thread. Duplicate registrations are rejected and the collector and unregister operations validate the native caller's ownership. `dnd_stw_current_slot` and `dnd_stw_safepoint_current` resolve the calling thread automatically. `dnd_stw_managed_hook` is compatible with the AOT `dnd_gc_set_safepoint_hook` callback signature, but **must not be installed yet**: multi-threaded managed frame/exception isolation, synchronized allocation and actual collector integration remain unfinished. Host tests exercise these ownership checks and invoke the hook while suspending the mutator.

## Explicit GC collection under stop-the-world coordination

`dnd_gc_collect_stopped(world, collector_slot, heap, roots)` now verifies that the caller is a registered collector, parks other registered native threads through `dnd_stw_suspend_others`, runs the existing managed collector, and resumes the parked threads. A host managed-runtime test covers wrong-slot rejection and successful collection of a rooted object. This API deliberately does not install safepoint callbacks or enable concurrent managed allocation. **It is only safe when all mutator threads register and reach safepoints, and when managed roots for all threads are enumerated while stopped.** A future integration step must prevent unsynchronized allocation and account for all managed execution stacks; automatic allocation-triggered collections remain single-threaded.
