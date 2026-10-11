using System;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Threading;
using Dolphin.Threading.Tasks;
using Dolphin.Storage;
class Program
{
    static float[] Identity(){var a=new float[16];a[0]=a[5]=a[10]=a[15]=1;return a;}
    static async System.Threading.Tasks.Task AwaitModel()
    {
        using(var model=await Model.LoadAsync("sd:/animated.dndmodel")){if(model.Animations.Count!=5)throw new InvalidOperationException("Awaited model metadata.");}
    }
    static int Main()
    {
        if(Dolphin.Diagnostics.DebugOverlay.PerformanceVisible)return 35;
        Dolphin.Diagnostics.DebugOverlay.PerformanceVisible=true;
        if(!Dolphin.Diagnostics.DebugOverlay.PerformanceVisible)return 36;
        Dolphin.Diagnostics.DebugOverlay.PerformanceVisible=false;
        if(Dolphin.Diagnostics.DebugOverlay.PerformanceVisible)return 37;
        if(!Storage.Mount(StorageDevice.SdSerialPort2))return 1;
        if(!Aram.Initialize())return 2;
        TransformChecks.Run();
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{Model.LoadAsync("sd:/animated.dndmodel",canceled.Token).Wait();return 3;}catch(OperationCanceledException){}}
        AwaitModel().GetAwaiter().GetResult();
        var task=Model.LoadAsync("sd:/cache.dndmodel");if(task.IsCompleted)return 4;var model=task.Result;
        if(model.Animations.Count!=5||!model.TryGetAnimation("Walk",out var clip)||clip.DurationSeconds!=1||model.TryGetAnimation("walk",out _))return 5;
        System.Collections.Generic.IReadOnlyList<AnimationClipInfo> metadata=model.Animations;if(metadata.Count!=5||metadata[0].Name!="Walk")return 30;
        int clips=0;foreach(var c in model.Animations){if(c.Name.Length==0)return 6;clips++;}if(clips!=5)return 7;
        var a=model.CreateInstance();var b=model.CreateInstance();a.Animator.Play("Walk");a.Update(0.5f);b.Animator.Play("Walk");b.Update(1);
        var matrix=Identity();a.GetNodeTransform(1,matrix);if(matrix[12]<0.99f||matrix[12]>1.01f)return 8;b.GetNodeTransform(1,matrix);if(matrix[12]<1.99f||matrix[12]>2.01f)return 9;
        a.Animator.CrossFade("Parent",0.5f);a.Update(0.25f);a.Animator.Pause();a.Animator.Resume();a.Animator.SetSpeed(2);a.Animator.Seek(0.2f);a.Animator.Stop();
        var entity=new GameObject(a);var parent=new Transform();parent.LocalPosition=new Vector3(2,0,0);entity.Transform.Parent=parent;entity.Transform.LocalPosition=new Vector3(1,0,0);
        var world=Identity();entity.Transform.CopyWorldMatrix(world);if(world[12]!=3)return 20;
        parent.LocalPosition=new Vector3(4,0,0);entity.Transform.CopyWorldMatrix(world);if(world[12]!=5)return 21;
        entity.Transform.Position=new Vector3(7,0,0);if(entity.Transform.LocalPosition.X!=3)return 22;
        entity.Transform.Rotate(0,0,90);entity.Transform.CopyWorldMatrix(world);if(world[1]<0.99f||world[1]>1.01f)return 23;
        entity.Transform.Translate(new Vector3(1,0,0));if(entity.Transform.LocalPosition.Y<0.99f||entity.Transform.LocalPosition.Y>1.01f)return 31;
        try{parent.Parent=entity.Transform;return 24;}catch(ArgumentException){}
        var camera=new Camera();camera.LookAt(new Vector3(0,0,5),Vector3.Zero,new Vector3(0,1,0));
        var renderer=new Renderer();renderer.BeginFrame(camera);renderer.Draw(entity);
        try{entity.Dispose();return 32;}catch(InvalidOperationException){}
        renderer.Draw(entity);renderer.EndFrame();entity.Update(0);
        renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(a,Identity());try{a.Update(0);return 10;}catch(InvalidOperationException){}renderer.EndFrame();
        int instanceMemory=a.NativeBytes;int memory=model.NativeBytes;a.Animator.SetSpeed(1);a.Animator.Play("Walk");a.Update(0.5f);model.UnloadToAramAsync().Wait();Dolphin.IO.File.Delete("sd:/cache.dndmodel");if(model.IsResident||model.NativeBytes>=memory||a.NativeBytes>=instanceMemory||model.AramBytes==0)return 11;
        try{a.Update(0);return 12;}catch(InvalidOperationException){}
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{model.PrepareAsync(canceled.Token).Wait();return 13;}catch(OperationCanceledException){}}if(model.IsResident)return 14;
        model.PrepareAnimationAsync("Walk").Wait();if(!model.IsResident)return 15;a.GetNodeTransform(1,matrix);if(matrix[12]<0.99f||matrix[12]>1.01f)return 16;
        renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(a,Identity());renderer.EndFrame();model.UnloadToAramAsync().Wait();model.PrepareAsync().Wait();
        entity.Dispose();entity.Dispose();
        try{a.Update(0);return 33;}catch(ObjectDisposedException){}
        if(!model.IsResident)return 34;
        b.Dispose();a.Dispose();model.Dispose();
        try{Model.LoadAsync("sd:/corrupt.dndmodel").Wait();return 17;}catch(System.IO.IOException){}
        try{Model.LoadAsync("sd:/animated.dndmodel",default,1024).Wait();return 18;}catch(System.IO.IOException){}
        var abandoned=Model.LoadAsync("sd:/animated.dndmodel").Result;var unload=abandoned.UnloadToAramAsync();Scheduler.Pump();abandoned.Dispose();try{unload.Wait();return 19;}catch(ObjectDisposedException){}
        using(var cancel=new CancellationTokenSource()){var canceledLoad=Model.LoadAsync("sd:/large.dndmodel",cancel.Token);Scheduler.Pump();cancel.Cancel();try{canceledLoad.Wait();return 25;}catch(OperationCanceledException){}}
        var large=Model.LoadAsync("sd:/large.dndmodel").Result;
        using(var cancel=new CancellationTokenSource()){var canceledUpload=large.UnloadToAramAsync(cancel.Token);Scheduler.Pump();Scheduler.Pump();cancel.Cancel();try{canceledUpload.Wait();return 26;}catch(OperationCanceledException){}}if(!large.IsResident)return 27;
        large.UnloadToAramAsync().Wait();
        using(var cancel=new CancellationTokenSource()){var canceledRestore=large.PrepareAsync(cancel.Token);Scheduler.Pump();Scheduler.Pump();cancel.Cancel();try{canceledRestore.Wait();return 28;}catch(OperationCanceledException){}}if(large.IsResident)return 29;
        large.PrepareAsync().Wait();large.Dispose();
        return 0;
    }
}
