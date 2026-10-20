using System;
using System.IO;
using Dolphin.Storage;
using Dolphin.Timing;
using Defrag = Dolphin.IO.Defrag;

/// <summary>
/// No graphics or input: reads an explicit ARM file and writes a report on
/// the selected test SD device. Never touches files outside dnd-defrag-test.
/// </summary>
internal static class Program
{
    private const int Size = 1024 * 1024;
    private const string Folder = "dnd-defrag-test";
    private static byte Pattern(int offset) => (byte)((offset * 31 + 7) & 255);

    private static bool Verify(string path)
    {
        using var file = File.OpenRead(path);
        if(file.Length != Size) return false;
        var data = new byte[4096];
        int position=0;
        while(position<Size)
        {
            int n=file.Read(data,0,data.Length);
            if(n<=0)return false;
            for(int i=0;i<n;i++) if(data[i]!=Pattern(position+i))return false;
            position+=n;
        }
        return file.ReadByte()==-1;
    }

    private static void Fill(byte[] data)
    {
        for(int i=0;i<data.Length;i++)data[i]=Pattern(i);
    }

    private static string TimedWrite(string folder,byte[] payload,bool enabled,int chunk)
    {
        var path=Path.Combine(folder,"benchmark.bin");
        if(File.Exists(path))File.Delete(path);
        Defrag.OptIn(enabled);
        var watch=Stopwatch.StartNew();
        using(var output=File.Open(path,FileMode.CreateNew,FileAccess.Write))
        {
            for(int pos=0;pos<payload.Length;pos+=chunk)
            {
                int n=payload.Length-pos;
                if(n>chunk)n=chunk;
                output.Write(payload,pos,n);
            }
        }
        watch.Stop();
        bool valid=Verify(path);
        var repaired=Defrag.Optimize(path);
        File.Delete(path);
        return "optin="+enabled+" chunk="+chunk+" ms="+watch.ElapsedMilliseconds+
               " verify="+valid+" extents="+repaired.Before.ExtentCount+
               " outcome="+repaired.Outcome+" after="+repaired.After.ExtentCount+"\n";
    }

    private static string TimedKnownLength(string folder,byte[] payload,bool enabled)
    {
        var path=Path.Combine(folder,"known-length.bin");
        if(File.Exists(path))File.Delete(path);
        Defrag.OptIn(enabled);
        var watch=Stopwatch.StartNew();
        File.WriteAllBytes(path,payload);
        watch.Stop();
        bool valid=Verify(path);
        var result=Defrag.Optimize(path);
        File.Delete(path);
        return "known-size optin="+enabled+" ms="+watch.ElapsedMilliseconds+
               " verify="+valid+" extents="+result.Before.ExtentCount+
               " outcome="+result.Outcome+" after="+result.After.ExtentCount+"\n";
    }

    private static int Run(StorageDevice device,string folder)
    {
        string arm=Path.Combine(folder,"arm.txt");
        var option=File.ReadAllText(arm).Trim();
        var report=Path.Combine(folder,"report.txt");
        string inflight=Path.Combine(folder,"inflight.bin");
        // A previously completed file must be intact. A partially staged
        // file must have been removed by mount recovery before this point.
        bool preexisting=File.Exists(inflight);
        bool preexistingValid=!preexisting || Verify(inflight);
        if(!preexistingValid)
        {
            File.WriteAllText(report,"FAIL: published inflight file is corrupt\n");
            return 3;
        }
        if(preexisting)File.Delete(inflight);
        if(option=="BENCH")
        {
            var data=new byte[Size];
            Fill(data);
            string result="DolphinDotNet SD fragmentation benchmark\n";
            result+="device="+device+" size="+Size+"\n";
            result+=TimedKnownLength(folder,data,false);
            result+=TimedKnownLength(folder,data,true);
            result+=TimedWrite(folder,data,false,16384);
            result+=TimedWrite(folder,data,true,16384);
            result+=TimedWrite(folder,data,true,65536);
            var stats=Defrag.GetStatistics();
            result+="automatic attempts="+stats.Attempts+
                    " reserved="+stats.ContiguousReservations+
                    " repairs="+stats.Repairs+
                    " no-space="+stats.NoSpace+
                    " failures="+stats.Failures+"\n";
            Defrag.OptIn(false);
            File.WriteAllText(report,result);
            return 0;
        }
        if(option=="POWER")
        {
            // On a dedicated test card only. Remove power at arbitrary points
            // in this loop, then reboot to check recovery. Successful files
            // are verified before deleting; journal recovery happens on mount.
            var data=new byte[16384];
            for(int pos=0;pos<data.Length;pos++)data[pos]=Pattern(pos);
            Defrag.OptIn(true);
            for(;;)
            {
                if(File.Exists(inflight))
                {
                    if(!Verify(inflight))
                    {
                        File.WriteAllText(report,"FAIL: published file checksum\n");
                        return 4;
                    }
                    File.Delete(inflight);
                }
                using(var output=Storage.CreateContiguousFile(inflight,Size))
                {
                    for(int offset=0;offset<Size;offset+=data.Length)
                        output.Write(data,0,data.Length);
                    output.Complete();
                }
            }
        }
        File.WriteAllText(report,"Unknown mode. Use BENCH or POWER in arm.txt\n");
        return 2;
    }

    private static int Main()
    {
        // Explicit opt-in: arm.txt must be placed manually on the chosen
        // target card. If neither device is armed, there are NO writes.
        StorageDevice[] devices={StorageDevice.SdSerialPort2,StorageDevice.GcLoader};
        for(int i=0;i<devices.Length;i++)
        {
            StorageDevice device=devices[i];
            if(!Storage.Mount(device))continue;
            string folder=Storage.GetPath(device,Folder);
            string arm=Path.Combine(folder,"arm.txt");
            if(File.Exists(arm))return Run(device,folder);
            Storage.Unmount(device);
        }
        return 0;
    }
}
