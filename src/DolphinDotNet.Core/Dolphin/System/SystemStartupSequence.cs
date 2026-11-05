using System;
namespace Dolphin.System;

public enum SystemStartupStage { Drop, Trace, Assemble, Letters, Hold, Fade, Completed }

/// <summary>Optional elapsed-time rendition of the original IPL startup. Curves
/// reconstruct the sequence; they are not decoded firmware animation tracks.
/// Seeking has no playback, storage, graphics or reset side effects.</summary>
public sealed class SystemStartupSequence
{
    public const double DurationSeconds=6.4;
    private double elapsed;
    public double ElapsedSeconds=>elapsed;
    public SystemStartupStage Stage=>elapsed<.45?SystemStartupStage.Drop:elapsed<3.1?SystemStartupStage.Trace:
        elapsed<3.65?SystemStartupStage.Assemble:elapsed<4.9?SystemStartupStage.Letters:
        elapsed<5.6?SystemStartupStage.Hold:elapsed<DurationSeconds?SystemStartupStage.Fade:SystemStartupStage.Completed;
    public bool IsComplete=>elapsed>=DurationSeconds;
    public float Opacity=>1-Ramp(5.6,DurationSeconds);
    public float LogoOpacity=>Ramp(3.1,3.65)*Opacity;
    public float CoverOpacity=>Ramp(.35,.5)*(1-Ramp(3.1,3.65))*.32f*Opacity;
    public float CubeOpacity=>(1-Ramp(3.2,3.65))*Opacity;
    public float TraceProgress=>Ramp(.45,3.1)*15;
    public float CameraProgress=>Smooth(Ramp(0,3.65));
    public float CubeRise=>1-Smooth(Ramp(0,.45));
    public void Seek(double seconds)
    {
        if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        elapsed=Math.Min(DurationSeconds,seconds);
    }
    public void Update(double deltaSeconds)
    {
        if(double.IsNaN(deltaSeconds)||double.IsInfinity(deltaSeconds)||deltaSeconds<0)throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        Seek(elapsed+Math.Min(deltaSeconds,DurationSeconds));
    }
    public void Restart()=>elapsed=0;
    public float TileOpacity(int index)
    {
        if(index<0||index>=15)throw new ArgumentOutOfRangeException(nameof(index));
        double start=.45+index*(2.65/15);
        return Ramp(start,start+.09)*(1-Ramp(3.1,3.65))*Opacity;
    }
    public float LetterProgress(int index)
    {
        if(index<0||index>=13)throw new ArgumentOutOfRangeException(nameof(index));
        double start=3.65+index*.055;return Smooth(Ramp(start,start+.45));
    }
    private float Ramp(double start,double end)=>(float)Math.Max(0,Math.Min(1,(elapsed-start)/(end-start)));
    private static float Smooth(float value)=>value*value*(3-2*value);
}
