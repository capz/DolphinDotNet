using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Storage;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

// Deliberately reference only packaged model loading. Source imports must stay
// available to other programs without becoming reachable from this one.
internal static class Program
{
    private static float[] Identity(){var m=new float[16];m[0]=m[5]=m[10]=m[15]=1;return m;}
    private static async Task AwaitModel()
    {
        using(var model=await Model.LoadAsync("sd:/animated.dndmodel"))
        {
            if(!Scheduler.IsApplicationThread||model.Animations.Count!=5)
                throw new InvalidOperationException("Packaged model await left the caller executor.");
        }
    }
    private static int Main()
    {
        if(!Storage.Mount(StorageDevice.SdSerialPort2)||!Aram.Initialize())return 1;
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();
            try{Model.LoadAsync("sd:/animated.dndmodel",cancel.Token).Wait();return 2;}
            catch(OperationCanceledException){}
        }
        using(var cancel=new CancellationTokenSource())
        {
            var loading=Model.LoadAsync("sd:/large.dndmodel",cancel.Token);
            Scheduler.Pump();cancel.Cancel();
            try{loading.Wait();return 3;}catch(OperationCanceledException){}
            if(!loading.IsCanceled)return 4;
        }
        try{Model.LoadAsync("sd:/corrupt.dndmodel").Wait();return 5;}catch(System.IO.IOException){}
        try{Model.LoadAsync("sd:/missing.dndmodel").Wait();return 6;}catch(System.IO.IOException){}
        try{Model.LoadAsync("sd:/animated.dndmodel",nativeBudgetBytes:1024).Wait();return 7;}catch(System.IO.IOException){}
        AwaitModel().GetAwaiter().GetResult();
        using(var model=Model.LoadAsync("sd:/animated.dndmodel").Result)
        using(var instance=model.CreateInstance())
        {
            if(model.Animations.Count!=5||!model.TryGetAnimation("Walk",out var clip)||clip.DurationSeconds!=1)return 8;
            instance.Animator.Play("Walk");instance.Update(.5f);
            var matrix=Identity();instance.GetNodeTransform(1,matrix);
            if(matrix[12]<.99f||matrix[12]>1.01f)return 9;
            var renderer=new Renderer();
            renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
            model.UnloadToAramAsync().Wait();
            if(model.IsResident||model.AramBytes==0)return 10;
            model.PrepareAsync().Wait();
            if(!model.IsResident)return 11;
            instance.GetNodeTransform(1,matrix);
            if(matrix[12]<.99f||matrix[12]>1.01f)return 12;
            renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
        }
        return 0;
    }
}
