/// <summary>120 seconds of active play between saves; retry a failed save after 15 seconds.</summary>
internal sealed class SaveInterval
{
    internal const float NormalSeconds=120f;
    internal const float RetrySeconds=15f;
    private float remaining=NormalSeconds;
    internal float Remaining=>remaining;
    internal bool Advance(float activeSeconds)
    {
        if(activeSeconds<0f)activeSeconds=0f;
        remaining-=activeSeconds;
        return remaining<=0f;
    }
    internal void Saved()=>remaining=NormalSeconds;
    internal void Failed()=>remaining=RetrySeconds;
}
