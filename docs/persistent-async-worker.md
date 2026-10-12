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
