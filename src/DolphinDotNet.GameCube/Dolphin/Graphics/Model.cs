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
/// <summary>Shared model package and resident native resources. Operations are serialized by the runtime execution gate.</summary>
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
    /// <summary>Load a DNMD package in bounded steps on the background executor.
    /// Await resumes on the caller's executor; storage/publication use the runtime gate.</summary>
    public static Task<Model> LoadAsync(string path,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(path==null)throw new ArgumentNullException("path");if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Import a static Wavefront OBJ directly on GameCube from a mounted
    /// volume. Supports MTL, base-color PNG/JPEG/TGA/BMP images, polygon
    /// triangulation and generated normals on the background executor.
    /// OBJ has no skeletal animation.</summary>
    public static Task<Model> LoadObjAsync(string path,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,1,allowMaterialApproximation);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Import binary or ASCII FBX directly on GameCube. Native-budget
    /// allocations cover parsing, embedded/external images, linear skinning and
    /// sampled TRS clips. Source parsing runs on the background executor.</summary>
    public static Task<Model> LoadFbxAsync(string path,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,float animationSampleRate=60,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if(!(animationSampleRate>=1&&animationSampleRate<=240))throw new ArgumentOutOfRangeException("animationSampleRate");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,4,allowMaterialApproximation,fps:animationSampleRate);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Import a bounded GLB v2 with embedded BIN and PNG/JPEG images on
    /// GameCube. Supports core triangle geometry, TRS animation and four-weight
    /// skins. Images must be power-of-two, 4..1024; source size is capped at 2 MiB.
    /// Parsing and image decoding run on the background executor.</summary>
    public static Task<Model> LoadGlbAsync(string path,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,2,allowMaterialApproximation);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Import GameCube GJ geometry, optional GVM/GVR textures and one
    /// linear NMDM motion natively. The clip is named "Motion". Original files
    /// are capped at 2 MiB each; parsing runs on the background executor. This
    /// profile has rigid node animation, not weighted Ninja chunk geometry.</summary>
    public static Task<Model> LoadPsoAsync(string path,string? textureArchivePath=null,string? motionPath=null,float motionFramesPerSecond=0,int eulerKeyBytes=16,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if(eulerKeyBytes!=8&&eulerKeyBytes!=16)throw new ArgumentOutOfRangeException("eulerKeyBytes");
        if(motionPath!=null&&!(motionFramesPerSecond>0&&motionFramesPerSecond<=240))throw new ArgumentOutOfRangeException("motionFramesPerSecond");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,3,false,textureArchivePath,motionPath,motionFramesPerSecond,eulerKeyBytes);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Load F-Zero GX GMA/GCMF with optional AV TPL on the background
    /// executor. Supports LZSS and U8/ARC; append #relative/member.gma to select
    /// an archive member. modelIndex=-1 loads every GMA model. Character motion
    /// is unsupported; deformed meshes require explicit static snapshot opt-in.</summary>
    public static Task<Model> LoadFZeroGxAsync(string path,string? textureArchivePath=null,int modelIndex=-1,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false,bool loadDeformedMeshesAsStatic=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(modelIndex < -1)throw new ArgumentOutOfRangeException("modelIndex");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,5,allowMaterialApproximation,textureArchivePath,selection:modelIndex,staticSnapshot:loadDeformedMeshesAsStatic);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Load Super Mario Sunshine J3D2 BMD3/BDL4 models and embedded BTI
    /// textures on the background executor. Optional BCK/BCA skeletal animation
    /// is sampled at 60 Hz into the clip "Motion". Supports Yaz0/Yay0 and RARC;
    /// append #relative/member.bmd or #relative/member.bck to select a member.
    /// Complex TEV materials need explicit approximation. Billboard geometry,
    /// scaled Maya segment compensation and material animations are unsupported.</summary>
    public static Task<Model> LoadJ3dAsync(string path,string? animationPath=null,float animationFramesPerSecond=30,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if(!(animationFramesPerSecond>=1&&animationFramesPerSecond<=240))throw new ArgumentOutOfRangeException("animationFramesPerSecond");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,6,allowMaterialApproximation,motion:animationPath,fps:animationFramesPerSecond);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Load original GameCube Resident Evil 4 BIN geometry, TPL textures
    /// and optional forward-kinematic FCV motion in the background. Accepts raw
    /// files, bare DAT bodies and uncompressed DRS/DAS containers; #N selects a
    /// zero-based archive entry. Motion is named "Motion" and sampled at 60 Hz.
    /// IK, double-joint solvers, facial morphs, SEQ remapping and yz2 compression
    /// are unsupported. Complex materials require explicit approximation.</summary>
    public static Task<Model> LoadResidentEvil4Async(string path,string textureArchivePath,string? animationPath=null,float animationFramesPerSecond=30,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(textureArchivePath==null)throw new ArgumentNullException("textureArchivePath");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if(!(animationFramesPerSecond>=1&&animationFramesPerSecond<=240))throw new ArgumentOutOfRangeException("animationFramesPerSecond");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,7,allowMaterialApproximation,textureArchivePath,animationPath,animationFramesPerSecond);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Load a RenderWare DFF clump, optional GameCube TXD and standard
    /// uncompressed ANM skeleton motion on the background executor. Supports
    /// portable and verified GameCube-native geometry/skins. Vendor containers,
    /// BSP worlds, morph targets and custom animation codecs are unsupported.
    /// Enable nativeVertexMatrixIndices only for lists with an explicit leading
    /// GX position-matrix byte per vertex; the loader does not guess layouts.</summary>
    public static Task<Model> LoadRenderWareAsync(string path,string? textureDictionaryPath=null,string? animationPath=null,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false,bool nativeVertexMatrixIndices=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,8,allowMaterialApproximation,textureDictionaryPath,animationPath,60,staticSnapshot:nativeVertexMatrixIndices);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Load a Melee HSD DAT/USD joint root, embedded textures and optional
    /// skeletal AnimJoint or figatree animation in the background. #symbol selects
    /// a public root; animation #N selects a concatenated AJ archive. Complex
    /// materials require explicit approximation. Scaled segment compensation,
    /// constraints, billboards, shape/material/visibility animation are unsupported.</summary>
    public static Task<Model> LoadMeleeAsync(string path,string? animationPath=null,float animationFramesPerSecond=60,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if(!(animationFramesPerSecond>=1&&animationFramesPerSecond<=240))throw new ArgumentOutOfRangeException("animationFramesPerSecond");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,9,allowMaterialApproximation,motion:animationPath,fps:animationFramesPerSecond);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Load verified Episode III GC GJ/NJCM and pointer-rooted model REL
    /// data, optional GVM/GVR textures, PRS/BML and linear NMDM/REL skeletal motion.
    /// #member selects a BML member, #N a REL model root or skeletal motion entry.
    /// Inline BML textures are used when no texture path is supplied. The clip
    /// is "Motion". fmt2 stages and unverified chunk variants fail explicitly.</summary>
    public static Task<Model> LoadPsoEpisode3Async(string path,string? textureArchivePath=null,string? motionPath=null,float motionFramesPerSecond=30,int eulerKeyBytes=16,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if(!(motionFramesPerSecond>=1&&motionFramesPerSecond<=240))throw new ArgumentOutOfRangeException("motionFramesPerSecond");
        if(eulerKeyBytes!=8&&eulerKeyBytes!=16)throw new ArgumentOutOfRangeException("eulerKeyBytes");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,10,allowMaterialApproximation,textureArchivePath,motionPath,motionFramesPerSecond,eulerKeyBytes);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Validate and load an in-memory prebuilt DNMD model, without
    /// creating any temporary files. A native owned copy is made.</summary>
    public static Model FromPackage(byte[] package,int nativeBudgetBytes=8*1024*1024)
    {
        if(package==null)throw new ArgumentNullException("package");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        int token=NativeModel.ModelBeginBytes(package,nativeBudgetBytes);
        if(token==0)throw new System.IO.IOException("Invalid model package or insufficient memory.");
        try{return new Model(token);}catch{NativeModel.ModelDispose(token);throw;}
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
        private readonly string path;private readonly int budget,format;private readonly CancellationToken cancellation;private readonly bool approximate;private int token;
        private readonly string? textures,motion;private readonly float fps;private readonly int eulerKeyBytes,selection;private readonly bool staticSnapshot;
        internal readonly TaskCompletionSource<Model> Completion=new TaskCompletionSource<Model>();
        internal LoadWork(string path,int budget,CancellationToken cancellation,int format=0,bool approximate=false,string? textures=null,string? motion=null,float fps=0,int eulerKeyBytes=16,int selection=-1,bool staticSnapshot=false){this.path=path;this.budget=budget;this.cancellation=cancellation;this.format=format;this.approximate=approximate;this.textures=textures;this.motion=motion;this.fps=fps;this.eulerKeyBytes=eulerKeyBytes;this.selection=selection;this.staticSnapshot=staticSnapshot;}
        internal void Step(){try{cancellation.ThrowIfCancellationRequested();if(token==0){token=format==10?NativeModel.ModelBeginPsoEpisode3(path,textures,motion,fps,eulerKeyBytes,budget,approximate?1:0):format>=6&&format<=9?NativeModel.ModelBeginGamecube(path,textures,motion,fps,format,budget,(approximate?1:0)|(format==8&&staticSnapshot?2:0)):format==5?NativeModel.ModelBeginFzero(path,textures,selection,budget,(approximate?1:0)|(staticSnapshot?2:0)):format==1?NativeModel.ModelBeginScene(path,0,60,budget,approximate?1:0):format==2?NativeModel.ModelBeginGlb(path,budget,approximate?1:0):format==3?NativeModel.ModelBeginPso(path,textures,motion,fps,eulerKeyBytes,budget):format==4?NativeModel.ModelBeginScene(path,1,fps,budget,approximate?1:0):NativeModel.ModelBegin(path,budget);
                if(token==0)throw new System.IO.IOException("Model input, native budget or handle limit is invalid.");}
                int result=format!=0?1:NativeModel.ModelStep(token);if(result<0)throw new System.IO.IOException("Invalid or truncated model package, or insufficient native memory.");if(result==0){Task.Run(Step);return;}cancellation.ThrowIfCancellationRequested();var model=new Model(token);token=0;Completion.SetResult(model);}catch(Exception error){if(token!=0){try{NativeModel.ModelDispose(token);}catch(Exception closeError){error=closeError;}token=0;}Completion.SetException(error);}}
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
