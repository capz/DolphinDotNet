using System;
using System.Threading.Tasks;
using Dolphin.Timing;

// Fixed slots; one shared SDK worker. Never recycle output while its Task runs.
internal sealed class BenchmarkWork
{
    internal byte[] Pixels;
    internal int Offset;
    internal readonly Action Action;
    internal Task? Task;
    internal int Id,Group;
    internal long Queued,Start,End;
    internal BenchmarkWork(byte[] pixels){Pixels=pixels;Action=Prepare;}
    internal void Begin(int id,int group)
    {if(Task!=null)throw new InvalidOperationException("Occupied preparation slot.");Id=id;Group=group;Queued=Clock.Timestamp;Start=End=0;Task=System.Threading.Tasks.Task.Run(Action);}
    private void Prepare(){Start=Clock.Timestamp;if(BenchmarkOptions.FastRows)BenchmarkSource.FillRows(Pixels,Id,Group,Offset);else BenchmarkSource.Fill(Pixels,Id,Group,Offset);End=Clock.Timestamp;}
    internal void Retire(){if(Task==null||!Task.IsCompleted)throw new InvalidOperationException("Premature scratch reuse.");Task.GetAwaiter().GetResult();Task=null;}
}
