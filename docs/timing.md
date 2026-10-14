# Timing

`Dolphin.Timing` provides monotonic clocks, elapsed and frame timing, async delays,
and disposable callbacks. GameCube uses libogc's PowerPC time-base clock
(`gettime`); host tests use POSIX `CLOCK_MONOTONIC`. These are elapsed-time APIs,
not calendar/UTC time or guaranteed real-time scheduling.

## Clock and stopwatch

```csharp
long start = Clock.Timestamp;
// ... work ...
double seconds = Clock.ElapsedSeconds(start, Clock.Timestamp);
var watch = Stopwatch.StartNew();
// ... work ...
watch.Stop();
long milliseconds = watch.ElapsedMilliseconds;
```

`Clock.Frequency` is 1,000,000: public timestamps are normalized microseconds.
`ElapsedSeconds` rejects an end before the start. `Stopwatch` exposes
`ElapsedTicks`, `ElapsedMilliseconds`, `ElapsedSeconds` and `IsRunning`, with
Start/Stop (accumulate), Reset (stop and clear), Restart and StartNew. Use the
`Dolphin.Timing` types; `System.Diagnostics.Stopwatch` is not mapped by this API.

## Frame timing

```csharp
var clock = new FrameClock { MaximumDeltaSeconds = 0.1f };
// Each game update:
float delta = clock.Tick();
Controller.Update();
UpdateGame(delta);
```

The first tick is zero. Subsequent ticks use monotonic elapsed time, clamped to
`MaximumDeltaSeconds` (default 0.25 seconds, allowed range (0,60]). `DeltaSeconds`
and `TotalSeconds` describe processed simulation time; clamped-away time is not
added to TotalSeconds. Pause produces zero deltas, Resume establishes a fresh
baseline, and Reset clears time and pause state. Calendar time is unaffected.
`UpdateGame` stands for application logic.

## Async delays

```csharp
await Dolphin.Timing.Time.DelayAsync(250);
await Task.Delay(250, cancellationToken);
```

Standard `Task.Delay(int)` and `Task.Delay(int, CancellationToken)` map to the same
runtime implementation. Zero completes immediately; -1 waits until canceled;
values below -1 are rejected. Pre-canceled tokens return a canceled task. Active
cancellation is observed by the runtime service. Default awaits preserve their
executor; `ConfigureAwait(false)` remains supported.

No pump is needed. Timers are serviced on the runtime thread using approximately
1 ms polling while timing work is pending. Deadlines prevent early completion;
actual continuation delivery can be later because of scheduling, application
safe points or blocking native calls. This is not precision audio/video timing.

## Callback timers

```csharp
using var timer = Dolphin.Timing.Timer.Schedule(
    () => RefreshState(), dueMilliseconds: 100, periodMilliseconds: 500);
```

Callbacks run on the executor captured when scheduled, under the managed
execution gate. Application-created timers can access application-only APIs;
background-created timers run on the background executor. No managed callback
executes inside an interrupt handler.

- A zero period is one-shot; positive periods repeat. Due times/periods must be
  nonnegative integer milliseconds. Missed periods coalesce instead of creating
  a catch-up burst. Only one callback per timer can be queued at a time.
- `Completion` completes after a one-shot callback, or when a callback timer is
  disposed. Callback faults settle Completion with an exception and stop the
  timer while it remains active. Observe Completion to receive callback faults.
- Dispose suppresses queued callbacks and removes pending work. Cancellation
  cancels Completion and suppresses callbacks. Disposing callback timers completes
  normally; pending delay work is canceled during runtime cleanup.
- At most 64 combined pending delays/callback timers are allowed. Disposed or
  completed operations release their slot.
- The generated bootstrap joins the service thread and cancels remaining timing
  work before releasing managed roots.

TimeSpan/TimeProvider overloads, `System.Threading.Timer`, `PeriodicTimer` and
calendar APIs are not included. Wii U requires a native timing/thread backend.
The deterministic manual scheduler test mode is not a timing execution backend.

Controller rumble uses native libogc system alarms instead: its motor timeout is
independent of managed callback delivery and game-update cadence.

## Validation

TimingSmoke checks monotonic deadlines, stopwatch accumulation, frame clamping and
pause/resume, standard Task.Delay, zero/infinite delays, cancellation, bounded
slots, captured application/background callbacks, faults, disposal and shutdown.
CI runs forced-GC/sanitizer and 32-bit host builds, links a PowerPC test DOL and
checks its successful completion in Dolphin. Physical console latency remains a
hardware validation task.
