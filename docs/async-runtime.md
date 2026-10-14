# Automatic async/await

Ordinary C# `async Task`, `async Task<T>` and `async Task<int> Main()` run through the production AOT compiler without application-owned `Scheduler.Pump()` or `NetworkRuntime.Pump()` calls. The compiler uses the assembly's actual CLI entry point, including Roslyn's synchronous `<Main>` wrapper for async Main.

```csharp
using System.Net.Http;
using System.Threading.Tasks;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;

static async Task<int> Main()
{
    try
    {
        await NetworkRuntime.StartAsync();
        using var http = new HttpClient();
        var text = await http.GetStringAsync("http://192.168.1.2:18081/json");
        return text.Length > 0 ? 0 : 1;
    }
    finally { if (NetworkRuntime.IsReady) NetworkRuntime.Stop(); }
}
```

The [AsyncNetworking](../samples/AsyncNetworking/Program.cs) and [GoogleLogoAram](../samples/GoogleLogoAram/Program.cs) samples use async Main directly. HTTP currently supports plain HTTP only; HTTPS is not implemented.

## Dispatch and continuation placement

The generated native entry point starts a persistent service thread when the reachable program uses tasks. It services nonblocking network operations and bounded background action batches, sleeps on a condition variable when idle, and uses 1 ms timed waits while socket/DMA polling is pending. Submissions and completions signal the condition variable. Startup failure is reported; shutdown joins the service thread before releasing the application's static roots.

| Operation | Executor |
| --- | --- |
| Initial application code | Application thread |
| Default pending `await` | Captures the current executor; application awaits resume on the application thread |
| `Task.Yield()` | Queues another turn on the current executor |
| `Task.Run(Action)` / `Task.Run<T>(Func<T>)` | Service thread |
| Pending `ConfigureAwait(false)` | Service thread; an already completed await may continue inline |
| Network service / ARAM transfer queue | Service thread |
| Graphics | Application thread; background calls are rejected |

Application continuations are dispatched automatically at generated application basic-block safe points (including loop backedges), at most once per millisecond, and from standard task waits. There are no async dispatch safe points inside Core/GameCube runtime operations: queue, task, cancellation, allocator and native bridge updates remain atomic relative to the other executor. An application can start an async operation and continue its ordinary input/update/render loop without adding service calls.

`Wait`, `Result` and `GetAwaiter().GetResult()` on pending tasks integrate with the dispatcher and condition-variable waits. They do not spin or require the caller to advance networking. They still block that call; use `await` to keep an application's logical flow asynchronous. A long synchronous native operation can delay dispatch until it returns.

## Managed execution safety

An execution gate serializes managed code between the application and service threads. This supplies background scheduling and I/O progress, **not parallel managed execution or a general thread pool**. Thread handoffs save and restore GC-frame chains, native exception-handler stacks, exception objects and pending exception state. The parked thread's live native stack remains registered as GC roots until it resumes. GC and heap mutation run exclusively under the same gate; they can safely trace both active and parked stacks, including nested exception frames.

The runtime supports these two owned executors. Arbitrary native threads must not call managed code, manipulate managed buffers, allocate or collect. The independent native-only worker queue and stop-the-world coordinator remain lower-level primitives, not permission to create additional managed mutators. Class initialization also holds an explicit critical scope, so another executor cannot observe partially initialized static fields. Waiting for a pending task inside a class initializer is unsupported. Synchronous storage calls still run under the execution gate and can delay the other executor.

Shutdown wakes the service thread and requests cancellation at application safe points and standard task waits in any active background callback, allowing finally blocks to unwind before join. Callbacks that catch shutdown exceptions indefinitely, or native calls that never return, can prevent graceful shutdown. Pending fire-and-forget work is not guaranteed to finish; await work and dispose its resources before Main returns.

## Namespaces and assemblies

Reference `DolphinDotNet.Core` for runtime implementations; graphics/input also require `DolphinDotNet.GameCube`. Applications use `System.Threading.Tasks`, `System.Threading` and the supported `System.IO` / `System.Net` signatures. AOT maps these contracts to Dolphin implementations. Direct ARAM and model methods also expose standard Task/cancellation signatures. The desktop CLR does not provide a native GameCube backend.

No scheduler namespace import is needed for normal async applications. `Dolphin.Threading.Tasks.Scheduler.IsApplicationThread` is available for diagnostics. Explicit pump methods remain for low-level compatibility/characterization tests. Host tests can set `DND_ASYNC_MANUAL=1` to disable automatic dispatch for deterministic queue-boundary checks; this switch is not present in the GameCube backend.

## Supported Task subset

| Feature | Scope |
| --- | --- |
| Async methods | Task / Task<T>, nested awaits, result/fault/cancellation and async Main |
| Task state / factories | IsCompleted, IsCompletedSuccessfully, IsFaulted, IsCanceled; CompletedTask, FromResult, FromException, FromCanceled |
| Work and composition | Run(Action / Func<T>, optional token), Yield, WhenAll(Task[]), WhenAny(Task[]) |
| Completion source | TaskCompletionSource<T>: Task, SetResult, SetException, SetCanceled |
| Continuation placement | Captured executor by default; ConfigureAwait(bool) |
| Cancellation | Source.Cancel, token checks; no registrations or timed cancellation |
| Blocking access | Wait, Result and GetAwaiter().GetResult integrate with automatic dispatch |

Task.Run has no async-delegate unwrapping overload. Task.Delay, async void, ValueTask, async streams, user-created Thread, locks, timers, general SynchronizationContext / ExecutionContext and full TPL behavior are not supplied. WhenAll reports the first captured fault ahead of cancellation and does not supply AggregateException collections. WhenAny returns the winning task; await it separately to observe its result.

## Verification

`tests/AutomaticAsyncSmoke` contains no pump calls. Compiled native execution covers captured/default awaits, Task.Run, Yield, ConfigureAwait(false), cancellation during active work, a synchronous application loop, exception rethrow across thread handoffs, GC roots on both executors, DNS/HTTP streaming to ARAM and multi-chunk readback/disposal. CI runs optimized sanitizers with forced GC and 32-bit native execution. Legacy tests still characterize explicit queue boundaries in manual host mode. The DOL build validates the libogc LWP backend; actual console timing and DMA behavior remain hardware validation tasks.
