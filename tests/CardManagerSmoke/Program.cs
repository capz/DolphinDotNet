using System;
using Dolphin.Storage;
internal static class Program
{
    internal static readonly int[] Result=new int[2],Probe=new int[2];
    internal static int Steps;
    internal static string Status="Not started";
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static int Main()
    {
        try{
            var display=Dolphin.Graphics.GraphicsDevice.Default;
            for(int i=0;i<24;i++){display.BeginFrame(Dolphin.Graphics.Color32.Black);display.EndFrame();}
            Result[1]=1;
            // This fixture is run only with fresh isolated emulator card files.
            using(var probe=MemoryCardManager.Open(StorageDevice.MemoryCardSlotA)){Probe[0]=probe.MountResult;if(!probe.IsReady)probe.Format();}
            Result[1]=2;
            using(var probe=MemoryCardManager.Open(StorageDevice.MemoryCardSlotB)){Probe[1]=probe.MountResult;if(!probe.IsReady)probe.Format();}
            Result[1]=3;
            var bytes=new byte[16384];for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)(i*37+i/257*13+i/8192*83);
            using(var scoped=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("ABCD","01"))){scoped.WriteAllBytes("DND_native_fixture",bytes);var back=scoped.ReadAllBytes("DND_native_fixture");Check(back.Length==bytes.Length,"Full sector read length");for(int i=0;i<bytes.Length;i++)Check(back[i]==bytes[i],"Non-repeating full sector data roundtrip");}
            Result[1]=4;
            using(var a=MemoryCardManager.Open(StorageDevice.MemoryCardSlotA))
            using(var b=MemoryCardManager.Open(StorageDevice.MemoryCardSlotB)){
                Check(a.IsReady&&b.IsReady&&a.Count==1&&b.Count==0,"Use fresh isolated emulator cards");
                Result[1]=5;
                var save=a.ReadPage(0)[0];Check(save.Length==24576&&save.GameCode=="ABCD","All-game native listing/ownership");
                Result[1]=6;
                using(var copy=a.BeginCopy(save,b)){while(!copy.Complete){copy.Step();Steps++;}Check(copy.Progress==100,"Copy progress");}
                Check(a.Count==1&&b.Count==1,"Verified native copy retains source");
                bool stale=false;try{a.Delete(save);}catch(InvalidOperationException){stale=true;}Check(stale,"Transfer invalidates old snapshots");
                Result[1]=7;
                a.Delete(a.ReadPage(0)[0]);
                Result[1]=8;
                using(var move=b.BeginCopy(b.ReadPage(0)[0],a,true)){while(!move.Complete){move.Step();Steps++;}}
                Check(a.Count==1&&b.Count==0,"Verified native move removes source");
                Result[1]=9;
                var icon=a.ReadIcon(a.ReadPage(0)[0]);Check(icon.Length==4096,"Bounded native icon read");
                a.Delete(a.ReadPage(0)[0]);Check(a.Count==0&&b.Count==0,"Native delete refreshes directory");
            }
            Result[0]=1;Status="Native card listing, copy, CRC readback, stale snapshots, move and delete passed.";Console.WriteLine(Status);return 0;
        }catch(Exception e){Result[0]=-1;Status=e.Message??"Native I/O failed; inspect the native error record.";Console.WriteLine(Status);return 1;}
    }
}
