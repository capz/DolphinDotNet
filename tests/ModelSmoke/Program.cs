using System;
using Dolphin.Graphics;
using Dolphin.Memory;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
using Dolphin.Storage;
class Program
{
    static float[] Identity(){var a=new float[16];a[0]=a[5]=a[10]=a[15]=1;return a;}
    static async System.Threading.Tasks.Task AwaitModel()
    {
        using(var model=await Model.LoadAsync("sd:/animated.dndmodel")){if(model.Animations.Count!=5)throw new InvalidOperationException("Awaited model metadata.");}
    }
    static void SourceImports()
    {
        var nativeTasks=new Task<Model>[]{Model.LoadRenderWareAsync("sd:/rw-native-skin.dff","sd:/rw.txd","sd:/rw.anm"),Model.LoadMeleeAsync("sd:/melee-weighted.dat","sd:/melee-weighted-motion.dat"),Model.LoadPsoEpisode3Async("sd:/ep3.bml",motionPath:"sd:/ep3-motion.rel")};
        for(int i=0;i<nativeTasks.Length;i++)using(var model=nativeTasks[i].Result)using(var instance=model.CreateInstance())
        {
            if(model.Animations.Count!=1)throw new InvalidOperationException("Native adapter animation metadata.");
            instance.Animator.Play("Motion");instance.Update(.5f);
            model.UnloadToAramAsync().Wait();model.PrepareAsync().Wait();
            var draw=new Renderer();draw.BeginFrame(Identity(),Identity());draw.DrawModel(instance,Identity());draw.EndFrame();
        }
        using(var model=Model.LoadJ3dAsync("sd:/sms-archive.szs","sd:/sms.arc#move.bck").Result)
        using(var instance=model.CreateInstance())
        {
            if(model.NodeCount!=1||model.GetNodeName(0)!="Root"||model.Animations.Count!=1)throw new InvalidOperationException("Native J3D metadata.");
            instance.Animator.Play("Motion");instance.Update(.5f);
            var matrix=Identity();instance.GetNodeTransform(0,matrix);
            if(matrix[12]<1.99f||matrix[12]>2.01f)throw new InvalidOperationException("Native BCK motion.");
            model.UnloadToAramAsync().Wait();model.PrepareAsync().Wait();
            var renderer=new Renderer();renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
        }
        using(var model=Model.LoadResidentEvil4Async("sd:/re4.drs#0","sd:/re4.drs#1","sd:/re4.drs#2").Result)
        using(var instance=model.CreateInstance())
        {
            if(model.NodeCount!=2||model.Animations.Count!=1)throw new InvalidOperationException("Native RE4 metadata.");
            instance.Animator.Play("Motion");instance.Update(.5f);
            var matrix=Identity();instance.GetNodeTransform(1,matrix);
            if(matrix[12]<1.49f||matrix[12]>1.51f)throw new InvalidOperationException("Native FCV motion.");
            model.UnloadToAramAsync().Wait();model.PrepareAsync().Wait();
            var renderer=new Renderer();renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
        }
        using(var model=Model.LoadFZeroGxAsync("sd:/fz.arc.lz").Result)
        using(var instance=model.CreateInstance())
        {
            if(model.NodeCount!=1||model.Animations.Count!=0)throw new InvalidOperationException("F-Zero GX metadata.");
            var renderer=new Renderer();renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
        }

        using(var model=Model.LoadFbxAsync("sd:/skinned.fbx").Result)
        using(var instance=model.CreateInstance())
        {
            if(model.Animations.Count!=1)throw new InvalidOperationException("Native FBX metadata.");
            instance.Animator.Play("Move");instance.Update(.5f);
            int bone=-1;for(int i=0;i<model.NodeCount;i++)if(model.GetNodeName(i)=="Bone")bone=i;
            if(bone<0)throw new InvalidOperationException("Native FBX skeleton.");
            var matrix=Identity();instance.GetNodeTransform(bone,matrix);
            if(matrix[12]<.99f||matrix[12]>1.01f)throw new InvalidOperationException("Native FBX animation.");
            var renderer=new Renderer();renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
        }
        using(var model=Model.LoadObjAsync("sd:/concave.obj").Result)
        using(var instance=model.CreateInstance())
        {
            if(model.Animations.Count!=0)throw new InvalidOperationException("Native textured OBJ metadata.");
            var renderer=new Renderer();renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
        }
        using(var model=Model.LoadPsoAsync("sd:/direct.gj","sd:/direct.gvm","sd:/direct.njm",30).Result)
        using(var instance=model.CreateInstance())
        {
            if(model.Animations.Count!=1||model.Animations[0].Name!="Motion")throw new InvalidOperationException("Native PSO metadata.");
            instance.Animator.Play("Motion");instance.Update(.5f);
            var matrix=Identity();instance.GetNodeTransform(0,matrix);
            if(matrix[12]<.99f||matrix[12]>1.01f)throw new InvalidOperationException("Native PSO motion.");
            var renderer=new Renderer();renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
        }
        using(var model=Model.LoadGlbAsync("sd:/embedded.glb").Result)
        using(var instance=model.CreateInstance())
        {
            if(model.Animations.Count!=5)throw new InvalidOperationException("Native GLB metadata.");
            instance.Animator.Play("Walk");instance.Update(.5f);
            var matrix=Identity();instance.GetNodeTransform(1,matrix);
            if(matrix[12]<.99f||matrix[12]>1.01f)throw new InvalidOperationException("Native GLB animation.");
        }
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();
            try{Model.LoadGlbAsync("sd:/embedded.glb",cancel.Token).Wait();throw new InvalidOperationException("Canceled native import accepted.");}
            catch(OperationCanceledException){}
        }
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
        ModelEffectChecks.Run();
        SourceImports();
        using(var transparentModel=Model.LoadFZeroGxAsync("sd:/fz-translucent.gma").Result)
        using(var instance=transparentModel.CreateInstance())
        {
            transparentModel.UnloadToAramAsync().Wait();transparentModel.PrepareAsync().Wait();
            var draw=new Renderer();draw.BeginFrame(Identity(),Identity());draw.DrawModel(instance,Identity());draw.EndFrame();
        }
        using(var directObj=Model.LoadObjAsync("sd:/live.obj").Result)
        {
            if(directObj.NodeCount<1||directObj.Animations.Count!=0)return 38;
            using(var instance=directObj.CreateInstance())
            {
                var cameraLive=new Camera();
                cameraLive.LookAt(new Vector3(0,0,5),Vector3.Zero,new Vector3(0,1,0));
                var drawLive=new Renderer();
                drawLive.BeginFrame(cameraLive);drawLive.DrawModel(instance,Identity());drawLive.EndFrame();
            }
        }
        byte[] modelData=Dolphin.IO.File.ReadAllBytes("sd:/animated.dndmodel");
        using(var modelMemory=Model.FromPackage(modelData))
        {
            if(modelMemory.Animations.Count!=5)return 39;
            modelData[0]=0; // model owns a copy
            if(!modelMemory.IsResident)return 40;
        }
        using(var pso=Model.LoadAsync("sd:/geometry-gj.dndmodel").Result)
        {
            if(pso.NodeCount!=1||pso.Animations.Count!=0)return 41;
        }
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{Model.LoadAsync("sd:/animated.dndmodel",canceled.Token).Wait();return 3;}catch(OperationCanceledException){}}
        using(var imported=Model.LoadAsync("sd:/gx-material.dndmodel").Result)
        using(var placement=new GameObject(imported.CreateInstance()))
        {
            imported.UnloadToAramAsync().Wait();imported.PrepareAsync().Wait();
            var batch=new Renderer();batch.BeginFrame(new Camera());
            batch.DrawModels(new[]{placement},new int[1],new float[16]);batch.EndFrame();
        }
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
        using(var secondObject=new GameObject(model.CreateInstance()))
        using(var emptyObject=new GameObject())
        {
            secondObject.Transform.LocalPosition=new Vector3(0,0,-3);
            var objects=new[]{entity,secondObject,emptyObject};var tokens=new int[3];var transforms=new float[48];
            renderer.BeginFrame(camera);renderer.DrawModels(objects,tokens,transforms);
            if(tokens[0]==0||tokens[1]==0||tokens[2]!=0||transforms[16+14]!=-3)return 51;
            try{renderer.DrawModels(objects,new int[2],transforms);return 52;}catch(ArgumentException){}
            renderer.EndFrame();
        }
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
