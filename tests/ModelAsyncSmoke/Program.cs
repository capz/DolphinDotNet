using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Storage;
using Dolphin.Timing;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static float[] Identity(){var m=new float[16];m[0]=m[5]=m[10]=m[15]=1;return m;}
    private static void Draw(Renderer renderer,ModelInstance instance)
    {
        if(!Scheduler.IsApplicationThread)throw new InvalidOperationException("Render continuation left application executor.");
        renderer.BeginFrame(Identity(),Identity());renderer.DrawModel(instance,Identity());renderer.EndFrame();
    }
    // The native host pauses ufbx at entry, with the execution gate released.
    // It publishes this file only once the real parser runs on another thread.
    private static void WhileParsing(Renderer renderer,ModelInstance instance,CancellationTokenSource? cancellation=null)
    {
        long started=Clock.Timestamp;
        while(!Dolphin.IO.File.Exists("sd:/native-import-active"))
        {
            Draw(renderer,instance);
            if(Clock.Timestamp-started>10000000)throw new InvalidOperationException("Background parser never started.");
        }
        Dolphin.IO.File.Delete("sd:/native-import-active");
        var retained=string.Concat("application-","root");
        for(int i=0;i<32;i++)
        {
            Draw(renderer,instance);
            // Forced-GC mode collects while the native parser stack is suspended.
            var allocation=new byte[257];allocation[i]=17;
            if(allocation[i]!=17||retained!="application-root")throw new InvalidOperationException("Application GC root.");
        }
        if(cancellation!=null)cancellation.Cancel();
        DebugOverlay.WriteLine("release-native-import");
    }
    private static async Task<int> Main()
    {
        if(!Storage.Mount(StorageDevice.SdSerialPort2))return 1;
        using(var existing=await Model.LoadAsync("sd:/animated.dndmodel"))
        using(var instance=existing.CreateInstance())
        {
            var renderer=new Renderer();
            var loading=Model.LoadFbxAsync("sd:/skinned.fbx");
            WhileParsing(renderer,instance);
            using(var imported=await loading)
            {
                if(!Scheduler.IsApplicationThread||imported.Animations.Count!=1)return 2;
                using(var importedInstance=imported.CreateInstance())Draw(renderer,importedInstance);
            }
            using(var cancellation=new CancellationTokenSource())
            {
                var canceled=Model.LoadFbxAsync("sd:/embedded-binary.fbx",cancellation.Token);
                WhileParsing(renderer,instance,cancellation);
                try{using(var unexpected=await canceled){}return 3;}catch(OperationCanceledException){}
                if(!canceled.IsCanceled||!Scheduler.IsApplicationThread)return 4;
            }
            // Queued imports share one native parser executor and keep await context.
            var obj=Model.LoadObjAsync("sd:/concave.obj");
            var glb=Model.LoadGlbAsync("sd:/embedded.glb");
            var pso=Model.LoadPsoAsync("sd:/direct.gj","sd:/direct.gvm","sd:/direct.njm",30);
            var fzero=Model.LoadFZeroGxAsync("sd:/fz.arc.lz");
            var sunshine=Model.LoadJ3dAsync("sd:/sms-member.arc","sd:/sms-member.arc#move.bck");
            var re4=Model.LoadResidentEvil4Async("sd:/re4.drs","sd:/re4.drs","sd:/re4.drs");
            var rw=Model.LoadRenderWareAsync("sd:/rw-native-skin.dff","sd:/rw.txd","sd:/rw.anm");
            var melee=Model.LoadMeleeAsync("sd:/melee-weighted.dat","sd:/melee-weighted-motion.dat");
            var ep3=Model.LoadPsoEpisode3Async("sd:/ep3.bml",motionPath:"sd:/ep3-motion.rel");
            await Task.WhenAll(new Task[]{obj,glb,pso,fzero,sunshine,re4,rw,melee,ep3});
            using(var w=await rw)using(var m=await melee)using(var e=await ep3)
            {
                if(!Scheduler.IsApplicationThread||w.Animations.Count!=1||m.Animations.Count!=1||e.Animations.Count!=1)return 17;
                using(var wi=w.CreateInstance())using(var mi=m.CreateInstance())using(var ei=e.CreateInstance())
                {wi.Animator.Play("Motion");mi.Animator.Play("Motion");ei.Animator.Play("Motion");wi.Update(.5f);mi.Update(.5f);ei.Update(.5f);Draw(renderer,wi);Draw(renderer,mi);Draw(renderer,ei);}
            }
            using(var o=await obj)using(var g=await glb)using(var p=await pso)using(var f=await fzero)
            using(var s=await sunshine)using(var r=await re4)
            {
                if(o.Animations.Count!=0||g.Animations.Count!=5||p.Animations.Count!=1||f.NodeCount!=1||f.Animations.Count!=0)return 5;
                if(!Scheduler.IsApplicationThread||s.Animations.Count!=1||r.Animations.Count!=1)return 12;
                using(var si=s.CreateInstance())using(var ri=r.CreateInstance()){Draw(renderer,si);Draw(renderer,ri);}
                Draw(renderer,instance);
            }
            using(var canceledFzero=new CancellationTokenSource())
            {
                canceledFzero.Cancel();
                try{await Model.LoadFZeroGxAsync("sd:/fz.arc",cancellationToken:canceledFzero.Token);return 11;}catch(OperationCanceledException){}
            }
            try{await Model.LoadFbxAsync("sd:/missing.fbx");return 6;}catch(System.IO.IOException){}
            try{await Model.LoadObjAsync("sd:/missing-sidecar.obj");return 7;}catch(System.IO.IOException){}
            try{await Model.LoadGlbAsync("sd:/embedded.glb",nativeBudgetBytes:1024);return 8;}catch(System.IO.IOException){}
            try{await Model.LoadGlbAsync("sd:/sparse.glb");return 9;}catch(System.IO.IOException){}
            try{await Model.LoadJ3dAsync("sd:/sms-bad-chunk.bmd");return 13;}catch(System.IO.IOException){}
            try{await Model.LoadResidentEvil4Async("sd:/re4.bin","sd:/re4.tpl","sd:/re4-ik.fcv");return 14;}catch(System.IO.IOException){}
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel();
                try{await Model.LoadJ3dAsync("sd:/sms.bmd",cancellationToken:cancel.Token);return 15;}catch(OperationCanceledException){}
                try{await Model.LoadResidentEvil4Async("sd:/re4.bin","sd:/re4.tpl",cancellationToken:cancel.Token);return 16;}catch(OperationCanceledException){}
            }
            try{await Model.LoadPsoEpisode3Async("sd:/ep3-bad.prs");return 18;}catch(System.IO.IOException){}
            try{await Model.LoadMeleeAsync("sd:/melee-cycle.dat");return 19;}catch(System.IO.IOException){}
            try{await Model.LoadRenderWareAsync("sd:/rw-bad.dff");return 20;}catch(System.IO.IOException){}
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel();
                try{await Model.LoadPsoEpisode3Async("sd:/ep3.bml",cancellationToken:cancel.Token);return 21;}catch(OperationCanceledException){}
                try{await Model.LoadMeleeAsync("sd:/melee.dat",cancellationToken:cancel.Token);return 22;}catch(OperationCanceledException){}
                try{await Model.LoadRenderWareAsync("sd:/rw.dff",cancellationToken:cancel.Token);return 23;}catch(OperationCanceledException){}
            }
            using(var recovered=await Model.LoadFbxAsync("sd:/textured.fbx"))
            using(var recoveredInstance=recovered.CreateInstance())Draw(renderer,recoveredInstance);
            // A load invoked from background code still returns to its awaiter.
            using(var background=await Task.Run(()=>Model.LoadGlbAsync("sd:/embedded.glb").Result))
            {
                if(!Scheduler.IsApplicationThread||background.Animations.Count!=5)return 10;
            }
        }
        return 0;
    }
}
