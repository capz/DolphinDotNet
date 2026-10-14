using System;
using System.Collections;
using System.Collections.Generic;
using Dolphin.Collections;
using Dolphin.Memory;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
namespace Dolphin.Graphics;

public sealed class AnimationClipInfo
{
    public string Name {get;}
    public float DurationSeconds {get;}
    internal readonly int Index;
    internal AnimationClipInfo(string name,float duration,int index){Name=name;DurationSeconds=duration;Index=index;}
}
public sealed class AnimationCollection : IReadOnlyList<AnimationClipInfo>
{
    private readonly AnimationClipInfo[] clips;
    public int Count=>clips.Length;
    public AnimationClipInfo this[int index]=>clips[index];
    internal AnimationCollection(AnimationClipInfo[] clips){this.clips=clips;}
    public IEnumerator<AnimationClipInfo> GetEnumerator()=>ArrayEnumerable<AnimationClipInfo>.Enumerate(clips);
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}
/// <summary>Shared model package and resident native resources. Application-thread operations only.</summary>
public sealed class Model : IDisposable
{
    internal readonly int Token;
    private bool disposed,busy;
    private AramBuffer? cold;
    public AnimationCollection Animations {get;}
    public int NodeCount {get;}
    public bool IsResident {get{Check();return NativeModel.ModelStat(Token,0)==1;}}
    public int NativeBytes {get{Check();return NativeModel.ModelStat(Token,1);}}
    public int AramBytes {get{Check();return cold==null?0:cold.Length;}}
    internal Model(int token)
    {
        Token=token;NodeCount=NativeModel.ModelStat(token,4);
        var clips=new AnimationClipInfo[NativeModel.ModelStat(token,3)];
        for(int i=0;i<clips.Length;i++)clips[i]=new AnimationClipInfo(NativeModel.ModelName(token,i,-1),NativeModel.ModelDuration(token,i),i);
        Animations=new AnimationCollection(clips);
    }
    public static Task<Model> LoadAsync(string path,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(path==null)throw new ArgumentNullException("path");if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken);Scheduler.Post(work.Step);return work.Completion.Task;
    }
    public bool TryGetAnimation(string name,out AnimationClipInfo clip)
    {
        Check();if(name==null)throw new ArgumentNullException("name");for(int i=0;i<Animations.Count;i++)if(Animations[i].Name==name){clip=Animations[i];return true;}clip=null!;return false;
    }
    internal int Clip(string name){if(!TryGetAnimation(name,out var clip))throw new ArgumentException("Unknown animation: "+name);return clip.Index;}
    public string GetNodeName(int index){Check();if(index<0||index>=NodeCount)throw new ArgumentOutOfRangeException("index");return NativeModel.ModelName(Token,-1,index);}
    public ModelInstance CreateInstance(int nativeBudgetBytes=4*1024*1024)
    {
        Check();if(busy)throw new InvalidOperationException("Model residency transition is active.");if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        var token=NativeModel.ModelInstance(Token,nativeBudgetBytes);if(token==0)throw new InvalidOperationException("Model is cold or instance memory budget is exhausted.");
        try{return new ModelInstance(this,token);}catch{NativeModel.ModelInstanceDispose(token);throw;}
    }
    /// <summary>Commit the ARAM copy before freeing main-RAM payloads. Cancellation retains the original residency.</summary>
    public Task UnloadToAramAsync(CancellationToken cancellationToken=default)=>Transition(false,cancellationToken);
    /// <summary>Restore all resources from ARAM before drawing; never performs hidden I/O in DrawModel.</summary>
    public Task PrepareAsync(CancellationToken cancellationToken=default)=>Transition(true,cancellationToken);
    public Task PrepareAnimationAsync(string name,CancellationToken cancellationToken=default){Clip(name);return PrepareAsync(cancellationToken);}
    private Task Transition(bool restore,CancellationToken cancellation)
    {
        Check();if(busy)throw new InvalidOperationException("Model residency transition is active.");
        if(restore==IsResident)return Task.CompletedTask;
        if(!restore&&cold!=null){cancellation.ThrowIfCancellationRequested();if(NativeModel.ModelUnload(Token)<0)throw new InvalidOperationException("End the render frame before unloading models.");return Task.CompletedTask;}
        if(!restore&&!Aram.IsAvailable)throw new InvalidOperationException("Initialize ARAM first.");
        if(restore&&cold==null)throw new InvalidOperationException("No ARAM backing package.");
        var work=new ResidencyWork(this,restore,cancellation);busy=true;
        try{Scheduler.Post(work.Step);}catch{busy=false;throw;}return work.Completion.Task;
    }
    internal void Check(){if(disposed)throw new ObjectDisposedException("Model");}
    internal void CheckInstanceUse(){if(busy)throw new InvalidOperationException("Model residency transition is active.");}
    public void Dispose(){if(disposed)return;if(busy){disposed=true;return;}if(NativeModel.ModelDispose(Token)<0)throw new InvalidOperationException("End the render frame before disposing models.");disposed=true;if(cold!=null){cold.Dispose();cold=null;}}
    private void EndWork(){busy=false;if(disposed){NativeModel.ModelDispose(Token);if(cold!=null){cold.Dispose();cold=null;}}}
    private sealed class LoadWork
    {
        private readonly string path;private readonly int budget;private readonly CancellationToken cancellation;private int token;
        internal readonly TaskCompletionSource<Model> Completion=new TaskCompletionSource<Model>();
        internal LoadWork(string path,int budget,CancellationToken cancellation){this.path=path;this.budget=budget;this.cancellation=cancellation;}
        internal void Step(){try{cancellation.ThrowIfCancellationRequested();if(token==0){token=NativeModel.ModelBegin(path,budget);if(token==0)throw new System.IO.IOException("Model header, native budget or handle limit is invalid.");}int result=NativeModel.ModelStep(token);if(result<0)throw new System.IO.IOException("Invalid or truncated model package, or insufficient native memory.");if(result==0){Scheduler.Post(Step);return;}cancellation.ThrowIfCancellationRequested();var model=new Model(token);token=0;Completion.SetResult(model);}catch(Exception error){if(token!=0){try{NativeModel.ModelDispose(token);}catch(Exception closeError){error=closeError;}token=0;}Completion.SetException(error);}}
    }
    private sealed class ResidencyWork
    {
        private readonly Model model;private readonly bool restore;private readonly CancellationToken cancellation;
        private readonly byte[] buffer=new byte[4096];private AramBuffer? storage;private Task? pending;private int position,count,length;private bool begun;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal ResidencyWork(Model model,bool restore,CancellationToken cancellation){this.model=model;this.restore=restore;this.cancellation=cancellation;}
        internal void Step()
        {
            try
            {
                if(pending!=null){if(!pending.IsCompleted){Scheduler.Post(Step);return;}pending.Wait();pending=null;if(restore&&NativeModel.ModelCopy(model.Token,position,buffer,count,1)<0)throw new System.IO.IOException("Model restore copy failed.");position+=count;}
                cancellation.ThrowIfCancellationRequested();model.Check();
                if(!begun){length=NativeModel.ModelStat(model.Token,2);storage=restore?model.cold:Aram.Allocate(length);if(restore&&NativeModel.ModelRestoreBegin(model.Token)<0)throw new OutOfMemoryException("Model restore exceeds its native budget.");begun=true;}
                if(position==length)
                {
                    if(restore){if(NativeModel.ModelRestoreEnd(model.Token,1)<0)throw new System.IO.IOException("Model restore validation or instance budget failed.");}
                    else{if(NativeModel.ModelUnload(model.Token)<0)throw new InvalidOperationException("End the render frame before unloading a model.");if(model.cold!=null)model.cold.Dispose();model.cold=storage;storage=null;}
                    model.EndWork();Completion.SetResult(0);return;
                }
                count=length-position;if(count>buffer.Length)count=buffer.Length;
                if(restore)pending=storage!.ReadAsync(position,buffer,0,count,cancellation);
                else{if(NativeModel.ModelCopy(model.Token,position,buffer,count,0)<0)throw new System.IO.IOException("Model snapshot failed.");pending=storage!.WriteAsync(buffer,0,position,count,cancellation);}
                Scheduler.Post(Step);
            }
            catch(Exception error)
            {
                if(restore&&begun)NativeModel.ModelRestoreEnd(model.Token,0);
                if(!restore&&storage!=null)storage.Dispose();model.EndWork();Completion.SetException(error);
            }
        }
    }
}
