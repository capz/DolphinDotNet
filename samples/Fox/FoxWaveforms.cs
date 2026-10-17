using System;

internal static class FoxWaveforms
{
    internal const int Rate=22050;
    internal static byte[] Pulse(int startHz,int endHz,int milliseconds)
    {
        if(startHz<20 || startHz>5000 || endHz<20 || endHz>5000 || milliseconds<10 || milliseconds>500)
            throw new ArgumentOutOfRangeException("milliseconds");
        int count=Rate*milliseconds/1000;
        var result=new byte[count];
        int phase=0;
        for(int i=0;i<count;i++)
        {
            int hz=startHz+(endHz-startHz)*i/count;
            phase+=hz;if(phase>=Rate)phase-=Rate;
            int amplitude=72*(count-1-i)/count;
            result[i]=(byte)(128+(phase<Rate/2?amplitude:-amplitude));
        }
        return result;
    }
}
