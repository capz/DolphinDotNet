using System;
using System.Threading.Tasks;
internal interface IReader { int Read(); }
internal sealed class Reader : IReader
{
    private readonly int value;
    internal Reader(int value){this.value=value;}
    public int Read()=>value;
}
internal struct Mixed { public long Number; public string Text; }
internal static class Program
{
    private static T Load<T>(ref T value)=>value;
    private static IReader Interface<T>(ref T value) where T:IReader=>value;
    private static async Task<int> Deferred()
    {
        var value=new Reader(41);
        await Task.Yield();
        var increment=await Task.Run(()=>1);
        await Task.Yield();
        return Interface(ref value).Read()+increment;
    }
    private static int Main()
    {
        // Exercise reference arrays in the System namespace and the scheduler
        // queue's growth, wrapped head, FIFO order and drained-slot reuse.
        var actions=new Action[]{()=>{}};actions[0]();
        int observed=0;
        for(int round=0;round<3;round++)
        {
            for(int i=0;i<40;i++){int expected=round*40+i;Dolphin.Threading.Tasks.Scheduler.Post(()=>{if(observed!=expected)throw new Exception("FIFO");observed++;});}
            Dolphin.Threading.Tasks.Scheduler.Pump(17);
            Dolphin.Threading.Tasks.Scheduler.RunUntilIdle();
        }
        if(observed!=120)return 7;
        // Force a safepoint deadline inside an explicitly pumped callback.
        // It must not recursively run the next application callback.
        int pumping=0;
        Dolphin.Threading.Tasks.Scheduler.Post(()=>{
            long start=Dolphin.Timing.Clock.Timestamp;
            while(Dolphin.Timing.Clock.Timestamp-start<3000){}
            if(pumping!=0)throw new Exception("Recursive manual dispatch");pumping=1;
        });
        Dolphin.Threading.Tasks.Scheduler.Post(()=>{if(pumping!=1)throw new Exception("Manual FIFO");pumping=2;});
        Dolphin.Threading.Tasks.Scheduler.Pump(2);
        if(pumping!=2)return 14;
        // An explicit wait inside a callback can still settle application work.
        var nested=new TaskCompletionSource<int>();
        Dolphin.Threading.Tasks.Scheduler.Post(()=>{
            Dolphin.Threading.Tasks.Scheduler.Post(()=>nested.SetResult(17));
            if(nested.Task.GetAwaiter().GetResult()!=17)throw new Exception("Nested wait");
        });
        Dolphin.Threading.Tasks.Scheduler.Pump();
        // Completion during application dispatch must return to Wait's predicate
        // without an unconditional park; exercise both modes under GC stress.
        Dolphin.Threading.Tasks.Scheduler.CompletionAwareWaits=true;
        Dolphin.Threading.Tasks.Scheduler.ResetWaitStats();
        var settled=new TaskCompletionSource<int>();
        Dolphin.Threading.Tasks.Scheduler.Post(()=>settled.SetResult(42));
        if(settled.Task.GetAwaiter().GetResult()!=42)return 8;
        if(Dolphin.Threading.Tasks.Scheduler.WaitStat(7)==0)return 9;
        var waitSnapshot=new long[9];Dolphin.Threading.Tasks.Scheduler.CopyWaitStats(waitSnapshot);
        if(waitSnapshot[7]==0)return 13;
        foreach(bool applicationTimers in new[]{false,true})
        foreach(bool deadline in new[]{false,true})
        {
            Dolphin.Threading.Tasks.Scheduler.ApplicationTimers=applicationTimers;
            Dolphin.Threading.Tasks.Scheduler.DeadlineWaits=deadline;
            Dolphin.Timing.Time.DelayAsync(1).GetAwaiter().GetResult();
            using(var cancellation=new System.Threading.CancellationTokenSource())
            {
                var delay=Dolphin.Timing.Time.DelayAsync(-1,cancellation.Token);
                Dolphin.Threading.Tasks.Scheduler.Post(()=>cancellation.Cancel());
                try{delay.GetAwaiter().GetResult();return 10;}catch(OperationCanceledException){}
            }
            var failed=new TaskCompletionSource<int>();
            Dolphin.Threading.Tasks.Scheduler.Post(()=>failed.SetException(new InvalidOperationException("expected")));
            try{failed.Task.GetAwaiter().GetResult();return 11;}catch(InvalidOperationException){}
        }
        Dolphin.Threading.Tasks.Scheduler.CompletionAwareWaits=false;
        var legacy=new TaskCompletionSource<int>();
        Dolphin.Threading.Tasks.Scheduler.Post(()=>legacy.SetResult(7));
        if(legacy.Task.GetAwaiter().GetResult()!=7)return 12;
        Dolphin.Threading.Tasks.Scheduler.CompletionAwareWaits=true;
        var reader=new Reader(41);
        if(!object.ReferenceEquals(Load(ref reader),reader))return 1;
        if(Interface(ref reader).Read()!=41)return 2;
        Reader? missing=null;if(Load(ref missing)!=null)return 3;
        int narrow=-123;long wide=0x100000002L;float single=3.25f;double real=7.125;
        if(Load(ref narrow)!=narrow||Load(ref wide)!=wide||Load(ref single)!=single||Load(ref real)!=real)return 4;
        var mixed=new Mixed{Number=wide,Text=string.Concat("root", "ed")};
        var copy=Load(ref mixed);
        if(copy.Number!=wide||copy.Text.Length!=6)return 5;
        if(Deferred().GetAwaiter().GetResult()!=42)return 6;
        return 0;
    }
}
