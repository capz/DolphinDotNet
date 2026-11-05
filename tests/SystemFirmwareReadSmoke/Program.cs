using Dolphin.System;
using Dolphin.Storage;
using System;
using System.Threading;
internal static class Program
{
    private static int Main()
    {
        using(var image=SystemFirmware.ReadConsole())
        using(var catalog=image.CreateCatalog())
            if(image.Source!=FirmwareSource.ConsoleRom||catalog.Count!=2)return 1;
        if(!Storage.Mount(StorageDevice.SdSerialPort2))return 2;
        using(var image=SystemFirmware.Open("sd:/firmware-synthetic.bin"))
        using(var catalog=image.CreateCatalog())
            if(image.Source!=FirmwareSource.ExplicitFile||catalog.Count!=2||catalog.Extract(0)[0]!=65)return 3;
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            try{SystemFirmware.ReadConsole(cancellation.Token);return 4;}
            catch(OperationCanceledException){}
        }
        return 0;
    }
}
