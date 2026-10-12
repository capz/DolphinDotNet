# Persistent async runtime: native worker foundation

This branch adds a **native-only** persistent I/O worker, started before the managed GameCube entry point and joined after that entry point returns. It is implemented with libogc LWP threads, mutexes, and a condition variable on GameCube, and POSIX threads in portable tests.

The native worker sleeps when idle, executes one queued native operation at a time, and provides bounded 32-slot submission and completion-ticket polling or condition-variable waiting with `dnd_worker_wait()` (no busy-spin). It drains queued jobs during shutdown. Native job contexts are owned by the caller and must stay valid until their completion ticket has been consumed (or until the worker has been joined).

**This is a foundational part of the requested background async model, not completion of it.** It does **not** remove `NetworkRuntime.Pump()` or `Scheduler.Pump()` from application code, and it does not execute managed `Task` continuations on the native thread.

## Why managed callbacks are prohibited

The existing AOT managed runtime uses process-global `gc_frames`, exception state and a single-thread heap allocator. There is no per-thread registration, thread-local exception state, safe-point coordination, cross-thread GC root enumeration, or locking for task and network state. Simply executing managed delegates from a native LWP would produce data races and potential use-after-free during collection.

The new `dnd_worker_submit` accepts **native-only** operations that do not access the managed heap, generated managed frames, or mutable managed socket/ARAM state. Do not capture or pass pointers to managed arrays, strings, or objects. The existing native networking and ARAM adapters remain application-thread-only until they have an explicitly thread-safe ownership design.

## Required to finish the requested feature

1. Add managed thread registration and per-thread GC roots, exception state, safe points and synchronized heap access. Test forced collections while both threads hold live object graphs.
2. Make `Task`, `TaskCompletionSource`, the scheduler and cancellation safe across threads with appropriate memory ordering. Specify where `await` resumes, including `SynchronizationContext` / main-thread dispatch for graphics.
3. Separate native-only socket/ARAM work from managed buffers using owned staging storage, with thread-safe submission/completion queues and cancellation. Avoid touching GX or game objects from the I/O thread.
4. Move network service and task progress to the background worker, while providing a sleep/wakeup mechanism instead of busy-polling. Preserve bounded service latency and avoid starving rendering.
5. Remove the two explicit pump calls from samples **only after** integration tests prove downloads and ARAM transfers make progress without them. Test GameCube hardware and emulator behavior, shutdown, cancellation and GC stress.

The worker foundation and host tests deliberately preserve the existing default single-thread managed behavior. The current `System.Threading.Tasks.Task` public signatures remain supported, but their scheduler still requires cooperative pumping.

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
