using System;

namespace Dolphin.Timing;

/// <summary>Game-update delta time with pause support and a configurable maximum step.</summary>
public sealed class FrameClock
{
    private long previous;
    private bool hasPrevious;
    private float maximum=0.25f;
    public bool IsPaused { get; private set; }
    public float DeltaSeconds { get; private set; }
    /// <summary>Accumulated, clamped simulation time, excluding pauses.</summary>
    public double TotalSeconds { get; private set; }
    public float MaximumDeltaSeconds
    {
        get => maximum;
        set { if (!(value>0 && value<=60)) throw new ArgumentOutOfRangeException("value"); maximum=value; }
    }
    public float Tick()
    {
        long now=Clock.Timestamp;
        float delta=hasPrevious && !IsPaused ? (float)Clock.ElapsedSeconds(previous,now) : 0;
        previous=now; hasPrevious=true;
        if (delta>maximum) delta=maximum;
        DeltaSeconds=delta; TotalSeconds+=delta;
        return delta;
    }
    public void Pause() { IsPaused=true; DeltaSeconds=0; }
    public void Resume() { IsPaused=false; hasPrevious=false; DeltaSeconds=0; }
    public void Reset() { previous=0; hasPrevious=false; IsPaused=false; DeltaSeconds=0; TotalSeconds=0; }
}
