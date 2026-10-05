# Async networking target

The long-term integration target is ordinary C# asynchronous networking on GameCube:

```csharp
while (true)
{
    var count = await stream.ReadAsync(buffer);
    ProcessPacket(buffer, count);
    await Game.NextFrame();
}
```

DolphinDotNet will not equate async with native threads. The first Task scheduler is single-threaded and frame-driven. Non-blocking libogc sockets will register I/O waiters; socket readiness completes managed tasks and queues continuations for the game thread.

## Dependency order

1. Interface dispatch.
2. Precise value-type layout and GC maps.
3. Typed primitive C representation.
4. Delegates and continuations.
5. Managed exceptions and EH.
6. Managed CoreLib algorithms.
7. Measured .NET Standard contract coverage.
8. Task method builders/state-machine characterization.
9. Non-blocking socket awaiters.
10. Optional managed threads after async I/O is stable.

The runtime should keep thread-dependent state out of global GC/exception structures where practical so a later multi-threaded collector can introduce per-thread root chains and stop-the-world safepoints without redesigning managed APIs.
