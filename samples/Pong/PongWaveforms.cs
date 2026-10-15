using System;

/// <summary>Generate reproducible, unsigned mono PCM8 samples at 22050 Hz.</summary>
internal static class PongWaveforms
{
    internal const int Rate=22050;

    // Basic triangle-wave chirps with a decay envelope. No external WAV files.
    internal static byte[] Chirp(int fromHz,int toHz,int milliseconds,int strength)
    {
        if(fromHz<100||toHz<100||fromHz>5000||toHz>5000)
            throw new ArgumentOutOfRangeException("fromHz");
        if(milliseconds<10||milliseconds>500||strength<1||strength>110)
            throw new ArgumentOutOfRangeException("milliseconds");
        int count=Rate*milliseconds/1000;
        var bytes=new byte[count];
        float phase=0f;
        for(int i=0;i<count;i++)
        {
            float progress=(float)i/count;
            float hz=fromHz+(toHz-fromHz)*progress;
            phase+=hz/Rate;
            if(phase>=1f)phase-=1f;
            float triangle=phase<0.5f?4f*phase-1f:3f-4f*phase;
            float envelope=1f-progress;
            int value=128+(int)(triangle*strength*envelope);
            if(value<0)value=0;
            if(value>255)value=255;
            bytes[i]=(byte)value;
        }
        return bytes;
    }
}
