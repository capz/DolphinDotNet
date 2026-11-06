using Dolphin.System;
using Dolphin.Diagnostics;
internal static class Program
{
    private static int Main()
    {
        var data=new byte[FirmwareImage.ImageBytes];
        FirmwareImage.Transform(data);
        if(data[256]!=0x89||data[257]!=0x7e||data[0x1aff00]!=0)return 1;
        FirmwareImage.Transform(data);
        for(int i=0;i<data.Length;i++)if(data[i]!=0)return 2;
        string header="Nintendo GameCube IPL synthetic";
        for(int i=0;i<header.Length;i++)data[i]=(byte)header[i];
        for(int offset=4096;offset<=8192;offset+=4096)
        {
            data[offset]=89;data[offset+1]=97;data[offset+2]=121;data[offset+3]=48;
            data[offset+7]=4;data[offset+11]=20;data[offset+15]=20;
            data[offset+16]=240;
            for(int i=0;i<4;i++)data[offset+20+i]=(byte)(65+i);
        }
        using(var image=FirmwareImage.Read(data))
        using(var catalog=image.CreateCatalog())
        {
            if(catalog.Count!=2||catalog.Extract(0)[0]!=65)return 3;
        }
        DebugOverlay.WriteLine("SYSTEM FIRMWARE CIPHER PASS");
        return 0;
    }
}
