# Cooperative async/await

Ordinary C# `async Task` and `async Task<T>` methods compile through AOT, including generated state machines, deferred continuations and retained locals. Execution uses one application thread. An await of a pending task suspends the method; its continuation runs when the application pumps the scheduler. Awaiting an already completed task can continue immediately.

## Namespaces and assemblies

Reference `DolphinDotNet.Core` for the runtime implementations; graphics/input also require `DolphinDotNet.GameCube`. Assembly names are not API namespaces.

| Application surface | Runtime implementation / platform surface |
| --- | --- |
| `System.Threading.Tasks.Task`, `Task<T>`, `TaskCompletionSource<T>` | AOT maps to `Dolphin.Threading.Tasks` |
| `System.Threading.CancellationToken`, `CancellationTokenSource` | AOT maps to `Dolphin.Threading` |
| Compiler-generated `System.Runtime.CompilerServices` async types | AOT maps the supported builders, awaiters and state-machine contracts to `Dolphin.Runtime.CompilerServices` |
| Standard `System.IO`, `System.Net`, `System.Net.Sockets`, `System.Net.Http` calls | Selected signatures map to `Dolphin.IO`, `Dolphin.Network` and `Dolphin.Network.Http` |
| Queue pumping | `Dolphin.Threading.Tasks.Scheduler` (no standard .NET equivalent) |
| Network startup/pumping/options | `Dolphin.Network.NetworkRuntime`, `NetworkOptions` |
| Direct model/ARAM APIs | `Dolphin.Graphics`, `Dolphin.Memory`; expose standard `System.Threading.Tasks.Task` and `System.Threading.CancellationToken` |

Use `using System.Threading.Tasks;` for normal async method return types. Alias the scheduler instead of importing both Task namespaces:

```csharp
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;
```

The public I/O, ARAM, networking and model APIs now use standard Task and cancellation signatures. The desktop CLR still sees the Dolphin task implementation as a separate type: do not cast between task types. The AOT compiler rewrites standard task, awaiter and cancellation metadata to the cooperative runtime equivalents. AOT redirection supplies the standard API compatibility on the target. Running a platform sample with `dotnet run` does not substitute for native AOT execution; platform internal calls require the native backend.

Custom Dolphin tasks remain internal runtime plumbing and are not the public application contract; custom async-method builders are not advertised for declaring `async Dolphin.Threading.Tasks.Task` methods. Use the standard Task return type. The legacy `DolphinDotNet.Runtime` information facade is separate from compiler-generated `Dolphin.Runtime.CompilerServices`; game platform information is exposed through `Dolphin.Platform`.

## Established Task subset

| Feature | Supported scope |
| --- | --- |
| Async methods | `async Task`, `async Task<T>`, nested awaits, result/fault/cancellation completion |
| Task state | IsCompleted, IsCompletedSuccessfully, IsFaulted, IsCanceled |
| Factories | CompletedTask, FromResult, FromException, FromCanceled |
| Queued work | Run(Action[, token]), Run<T>(Func<T>[, token]), Yield |
| Composition | WhenAll(Task[]), WhenAny(Task[]); generic result-array/enumerable overloads are not established |
| Completion source | TaskCompletionSource<T>: Task, SetResult, SetException, SetCanceled |
| Await configuration | ConfigureAwait(bool); no synchronization or execution context is captured |
| Cancellation | Source.Cancel, token state and ThrowIfCancellationRequested; cooperative checks, no cancellation registrations or timed cancellation |
| Blocking access | Wait, Result and GetAwaiter().GetResult pump scheduler work; use only intentionally, outside a frame loop or after completion |

`Task.Run` schedules its delegate on the application thread; it does not provide parallel execution. It has no dedicated async-delegate unwrapping overload: do not assume desktop `Task.Run(Func<Task>)` semantics. `Task.Yield` schedules another queue turn, not necessarily the next rendered frame. No `Task.Delay`, async void, ValueTask, async streams, managed Thread/thread pool, locks, timers or full TPL/context behavior is supplied.

WhenAll waits for all tasks, reports the first captured fault ahead of cancellation and does not expose a desktop AggregateException collection. WhenAny returns the completed candidate task; await that candidate separately to observe its result/fault/cancellation.

## Keep the outer loop running

Start an async operation and keep pumping from the outer loop. Do not put a synchronous Wait/Result/GetResult on a pending network task in that loop: only networking's service pump can advance its socket/DNS/HTTP work. A scheduler wait with no queued completion path throws; it does not service networking automatically.

```csharp
var pending = DownloadValueAsync(); // ordinary async Task<int> method
while (!pending.IsCompleted)
{
    NetworkRuntime.Pump();
    Scheduler.Pump(16);
    // Input, update and render remain application-owned.
}
var value = pending.GetAwaiter().GetResult(); // already completed; observes faults
```

The complete, compiled [AsyncNetworking example](../samples/AsyncNetworking/Program.cs) defines DownloadValueAsync, awaits direct Dolphin startup, then awaits standard HttpClient DNS/HTTP work and reads a JSON value. Its loop is headless; a game can use the same pumps inside its existing frame loop. Defaults match the local network fixture, not a production server. HTTPS remains unsupported.

`Scheduler.Pump()` executes one queued action. `Pump(maximumActions)` executes at most that many actions and no more than the queue count at entry; continuations posted during the call may remain for a later pump. RunUntilIdle drains the queue and can block rendering. Action count is not a time budget: a long CPU delegate or synchronous native call can exceed a frame's duration.

File/stream/text operations yield between bounded chunks, but storage descriptor calls remain synchronous within a chunk. Network sockets use nonblocking operations with a separate bounded service pump. ARAM operations submit/poll native DMA and queue continuations; they still require main-RAM staging. Serialize concurrent operations on a shared stream and keep resources/buffers alive until their operations complete.

## Examples and verification

- `tests/AsyncSmoke`: ordinary System Task/cancellation namespaces, nested awaits and retained strings, one-action pumping, faults/catch/finally, cancellation, completion sources, WhenAll/WhenAny and awaiting a direct Dolphin task.
- `tests/LibrarySmoke`: collections/text plus compiled async Yield/Run/ConfigureAwait and stream copying.
- `tests/StorageSmoke`, `tests/AramSmoke`, `tests/ModelSmoke`: cooperative platform I/O and resource lifecycle tests.
- `tests/NetworkSmoke` and `samples/AsyncNetworking`: generated native execution against deterministic UDP/TCP/DNS/HTTP fixtures.

CI compiles the dedicated async smoke to native C, runs it under sanitizers and GC stress on 64-bit hosts, and runs the GC-stress path on 32-bit hosts. The network example also executes through AOT against local fixtures. GameCube build/link evidence is separate from execution: actual Dolphin/console behavior and frame-time/stack acceptance remain pending.
