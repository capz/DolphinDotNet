using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Memory;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Empty(){Check(NativeGraphics.BufferHolds==0&&NativeGraphics.AramHolds==0&&!NativeGraphics.Pending&&!NativeGraphics.EarlyRelease,"Dispatcher failure orphaned or prematurely released a DMA owner.");}
    private static void Failed(Task task,string message){try{task.GetAwaiter().GetResult();throw new Exception("Expected dispatch failure.");}catch(InvalidOperationException error){Check(error.Message==message,"Wrong failure.");}}
    public static void Main()
    {
        var buffer=new NativeBuffer();var aram=new AramBuffer();
        Scheduler.Reset();Scheduler.FailBackground=true;
        try{AramTransferQueue.CopyToAramAsync(buffer,0,aram,0,32);throw new Exception("Initial dispatch refusal was swallowed.");}
        catch(InvalidOperationException error){Check(error.Message=="background refused","Wrong initial dispatch failure.");}
        Empty();
        Scheduler.Reset();var pending=AramTransferQueue.CopyToAramAsync(buffer,0,aram,0,32);
        Scheduler.FailBackground=true;Scheduler.Run();Failed(pending,"background refused");Empty();
        Scheduler.Reset();pending=AramTransferQueue.CopyToAramAsync(buffer,0,aram,0,32);
        Scheduler.FailApplication=true;Scheduler.Run();Scheduler.Run();Failed(pending,"application refused");Empty();
        Scheduler.Reset();pending=AramTransferQueue.CopyToAramAsync(buffer,0,aram,0,32);
        NativeGraphics.FailPoll=true;Scheduler.Run();Failed(pending,"native poll refused");Empty();
        Console.WriteLine("Native transfer dispatch refusal drains tickets before releasing owners: PASS");
    }
}
namespace Dolphin.Memory
{
    public sealed class AramBuffer{internal int NativeTransferToken=>1;public int Length=>64;}
    public static class Aram{public static void WakeTransfers(){}}
}
namespace Dolphin.Graphics
{
    public sealed class NativeBuffer{internal int Token=>1;public int Length=>64;public ulong GetAddress(int offset)=>32;}
    internal static class NativeGraphics
    {
        internal static int BufferHolds,AramHolds;
        internal static bool Pending,EarlyRelease,FailPoll;
        private static bool canceled;private static int steps,ticket;
        internal static int AramTransferHold(int token){AramHolds++;return 0;}
        internal static int BufferHold(int token){BufferHolds++;return 0;}
        internal static int BufferUnhold(int token){if(Pending)EarlyRelease=true;BufferHolds--;return 0;}
        internal static int AramTransferRelease(int token){if(Pending)EarlyRelease=true;AramHolds--;return 0;}
        internal static int AramRequestNative(int token,int aramOffset,int buffer,int bufferOffset,int count,int write,int priority)
        {if(Pending)throw new Exception("Fixture already owns a request.");BufferHolds++;AramHolds++;Pending=true;canceled=false;steps=0;return ++ticket;}
        internal static int AramRequestCancel(int request){canceled=true;return 0;}
        internal static int AramRequestPoll(int request)
        {
            if(FailPoll){FailPoll=false;throw new InvalidOperationException("native poll refused");}
            if(!canceled&&steps++==0)return 0;
            Pending=false;BufferHolds--;AramHolds--;return canceled?-125:1;
        }
    }
}
namespace Dolphin.Threading.Tasks
{
    internal static class Scheduler
    {
        private static readonly Queue<Action> background=new Queue<Action>();
        private static readonly Queue<Action> application=new Queue<Action>();
        internal static bool FailBackground,FailApplication;
        internal static void Reset(){background.Clear();application.Clear();FailBackground=FailApplication=false;}
        internal static void PostBackground(Action action){if(FailBackground)throw new InvalidOperationException("background refused");background.Enqueue(action);}
        internal static void Post(Action action,int context){if(FailApplication)throw new InvalidOperationException("application refused");application.Enqueue(action);}
        internal static void Run(){if(background.Count!=0)background.Dequeue()();while(application.Count!=0)application.Dequeue()();}
    }
}
