using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;

internal static class ResidencyChecks
{
    private static void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
    private static int Drain(Model model,Task task)
    {
        int steps=0;
        while(model.IsTransitioning)
        {
            model.AdvanceResidency();
            if(++steps>100000)throw new InvalidOperationException("Manual model residency did not settle.");
        }
        task.GetAwaiter().GetResult();return steps;
    }
    internal static void Run()
    {
        using(var model=Model.LoadAsync("sd:/animated.dndmodel").Result)
        using(var instance=model.CreateInstance())
        {
            model.AutomaticResidencyProgress=false;
            model.ResidencyBytesPerStep=256;model.ResidencyWorkItemsPerStep=7;
            Check(model.SupportsAramResidency&&model.PackageBytes>=160,"Immutable package residency metadata.");
            instance.Animator.Play("Walk");instance.Update(.5f);
            var before=new float[16];var after=new float[16];instance.GetNodeTransform(1,before);
            int native=model.NativeBytes,skin=instance.NativeBytes;
            var unload=model.UnloadToAramAsync();
            Check(model.IsTransitioning&&model.IsResident&&!unload.IsCompleted,"Manual transition must await grants.");
            Check(Drain(model,unload)>2,"Model demotion must retire in bounded steps.");
            Check(!model.IsResident&&model.NativeBytes<native&&instance.NativeBytes<skin,"Demotion must reclaim native payloads.");
            Check(model.AramBytes==((model.PackageBytes+31)&~31),"Snapshot allocation must expose rounded ARAM charge.");
            try{model.ReleaseAramBacking();throw new InvalidOperationException("Cold backing released.");}catch(InvalidOperationException error){Check(error.Message!="Cold backing released.","Cold backing release must reject.");}
            using(var cancel=new CancellationTokenSource())
            {
                var restore=model.PrepareAsync(cancel.Token);
                model.AdvanceResidency();model.AdvanceResidency();cancel.Cancel();
                try{Drain(model,restore);throw new InvalidOperationException("Canceled restore succeeded.");}catch(OperationCanceledException){}
                Check(!model.IsResident&&model.AramBytes>0,"Canceled prepare must retain cold backing.");
            }
            Drain(model,model.PrepareAsync());instance.GetNodeTransform(1,after);
            for(int i=0;i<16;i++)Check(before[i]==after[i],"Residency advanced or changed animation pose.");
            // A warm backing avoids another copy, but retirement remains incremental.
            Check(Drain(model,model.UnloadToAramAsync())>2,"Warm demotion bypassed bounded retirement.");
            Drain(model,model.PrepareAsync());
            Check(model.ReleaseAramBacking()&&model.AramBytes==0&&!model.ReleaseAramBacking(),"Redundant backing release lifecycle.");
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel();var rejected=model.UnloadToAramAsync(cancel.Token);
                try{Drain(model,rejected);throw new InvalidOperationException("Canceled demotion succeeded.");}catch(OperationCanceledException){}
                Check(model.IsResident&&!model.IsTransitioning,"Canceled precommit demotion changed residency.");
            }
        }
        var abandoned=Model.LoadAsync("sd:/animated.dndmodel").Result;
        abandoned.AutomaticResidencyProgress=false;
        var pending=abandoned.UnloadToAramAsync();
        abandoned.AdvanceResidency();abandoned.AdvanceResidency();abandoned.Dispose();
        try{Drain(abandoned,pending);throw new InvalidOperationException("Disposed manual transition succeeded.");}catch(ObjectDisposedException){}
        Check(!abandoned.IsTransitioning,"Disposed manually driven transition did not drain.");
    }
}
