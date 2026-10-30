using System;
using System.IO;
using Dolphin.Formats;

internal static class Program
{
    private static void Check(bool condition,string detail)
    {
        if(!condition)throw new Exception("Fixture probe failed: "+detail);
    }
    private static byte[] TestBti()
    {
        // Native GX I4 8x8: valid 0x20-byte header + 32-byte tile.
        var bytes=new byte[0x40];bytes[0]=0;
        bytes[2]=0;bytes[3]=8;bytes[4]=0;bytes[5]=8;
        bytes[0x18]=1;bytes[0x1f]=0x20;
        for(int i=0x20;i<bytes.Length;i++)bytes[i]=(byte)(i*7);
        return bytes;
    }
    private static int Main()
    {
        var bytes=TestBti();
        var result=JSystemFixtureProbe.Inspect("test.bti",bytes);
        Check(result.Format==GameCubeFileFormat.Bti && result.HasNativeVisual,"BTI visual");
        Check(result.Elements==1,"BTI mip count");
        bool bad=false;
        try{JSystemFixtureProbe.Inspect("test.bti",new byte[3]);}
        catch(NotSupportedException){bad=true;}
        Check(bad,"Reject truncation without guessing.");
        bad=false;
        try{JSystemFixtureProbe.Inspect("not-j3d.bin",bytes);}
        catch(NotSupportedException){bad=true;}
        Check(bad,"Reject unhinted data.");
        bad=false;
        try{JSystemFixtureProbe.Inspect("too-big.arc",new byte[GameCubeBinary.MaxAssetBytes+1]);}
        catch(InvalidDataException){bad=true;}
        Check(bad,"Reject excessive size.");
        Console.WriteLine("JSystem fixture probe checks OK");
        return 0;
    }
}
