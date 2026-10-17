using System;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

namespace Dolphin.Hardware;

public enum MemoryCardSlot { A=0, B=1 }

/// <summary>Immutable physical connection state, independent of mounting or save readiness.</summary>
public readonly struct MemoryCardState
{
    private readonly int transitions;
    /// <summary>Last confirmed presence; initially Unknown. Busy/Unknown probes preserve it.</summary>
    public HardwarePresence Presence { get; }
    /// <summary>The latest probe, including Busy, Unknown and Unsupported.</summary>
    public HardwarePresence ProbePresence { get; }
    public bool IsKnown => Presence==HardwarePresence.Present || Presence==HardwarePresence.Absent;
    public bool IsConnected => Presence==HardwarePresence.Present;
    public bool WasConnected => (transitions&1)!=0;
    public bool WasDisconnected => (transitions&2)!=0;

    internal MemoryCardState(HardwarePresence presence,HardwarePresence probe,bool connected,bool disconnected)
    { Presence=presence;ProbePresence=probe;transitions=(connected?1:0)|(disconnected?2:0); }

    internal MemoryCardState Next(HardwarePresence probe)
    {
        bool known=probe==HardwarePresence.Present || probe==HardwarePresence.Absent;
        var confirmed=known?probe:Presence;
        return new MemoryCardState(confirmed,probe,
            known && confirmed==HardwarePresence.Present && Presence!=HardwarePresence.Present,
            known && confirmed==HardwarePresence.Absent && Presence==HardwarePresence.Present);
    }
}

/// <summary>Call Update on the application executor, then read cached slot snapshots.
/// Probing never mounts, unmounts, formats or writes a card.</summary>
public static class MemoryCards
{
    private static readonly MemoryCardState[] states={
        new MemoryCardState(HardwarePresence.Unknown,HardwarePresence.Unknown,false,false),
        new MemoryCardState(HardwarePresence.Unknown,HardwarePresence.Unknown,false,false)
    };
    public const int SlotCount=2;

    public static void Update()
    {
        RequireApplicationThread();
        // Fetch both before publishing the next update. Extended card probes
        // check physical presence even when a storage mount is still cached.
        var a=Probe(0);var b=Probe(1);
        states[0]=states[0].Next(a);states[1]=states[1].Next(b);
    }

    public static MemoryCardState GetState(MemoryCardSlot slot=MemoryCardSlot.A)
    {
        RequireApplicationThread();
        if(slot!=MemoryCardSlot.A && slot!=MemoryCardSlot.B)throw new ArgumentOutOfRangeException("slot");
        return states[(int)slot];
    }

    private static HardwarePresence Probe(int slot)
    {
        int value=NativeHardware.HardwareProbe((int)HardwareKind.MemoryCard,(int)HardwarePort.SlotA+slot,(int)HardwareProbeMode.Extended);
        return value>=0 && value<=4?(HardwarePresence)value:HardwarePresence.Unknown;
    }

    private static void RequireApplicationThread()
    {
        if(!Scheduler.IsApplicationThread)throw new InvalidOperationException("Read or update card snapshots on the application executor; copy values before background work.");
    }
}
