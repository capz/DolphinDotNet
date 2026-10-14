namespace Dolphin.Timing;

/// <summary>A monotonic stopwatch; Start/Stop preserve accumulated time.</summary>
public sealed class Stopwatch
{
    private long accumulated, started;
    public bool IsRunning { get; private set; }
    public long ElapsedTicks => accumulated + (IsRunning ? Clock.Timestamp-started : 0);
    public long ElapsedMilliseconds => ElapsedTicks/1000;
    public double ElapsedSeconds => ElapsedTicks/(double)Clock.Frequency;
    public static Stopwatch StartNew() { var watch=new Stopwatch(); watch.Start(); return watch; }
    public void Start() { if (IsRunning) return; started=Clock.Timestamp; IsRunning=true; }
    public void Stop() { if (!IsRunning) return; accumulated+=Clock.Timestamp-started; IsRunning=false; }
    public void Reset() { accumulated=0; started=0; IsRunning=false; }
    public void Restart() { Reset(); Start(); }
}
