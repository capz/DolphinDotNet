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
    private ResidencyWork? residency;
    private bool automaticResidencyProgress=true;
    public AnimationCollection Animations {get;}
    public int NodeCount {get;}
    public int MaterialCount { get { Check(); return NativeModel.ModelStat(Token,5); } }
    public int MeshCount { get { Check(); return NativeModel.ModelStat(Token,7); } }
    public bool IsResident {get{Check();return NativeModel.ModelStat(Token,0)==1;}}
    public int NativeBytes {get{Check();return NativeModel.ModelStat(Token,1);}}
    public int AramBytes {get{Check();return cold==null?0:cold.Length;}}
    /// <summary>Immutable source length; ARAM snapshots round this up to 32 bytes.</summary>
    public int PackageBytes {get{Check();return NativeModel.ModelStat(Token,2);}}
    public bool SupportsAramResidency {get{Check();return NativeModel.ModelStat(Token,8)==1;}}
    public bool IsTransitioning=>busy;
    /// <summary>False requires AdvanceResidency calls between frames. Set before starting a transition.</summary>
    public bool AutomaticResidencyProgress
    {get=>automaticResidencyProgress;set{Check();if(busy)throw new InvalidOperationException("A residency transition is active.");automaticResidencyProgress=value;}}
    public int ResidencyBytesPerStep {get;set;}=16384;
    public int ResidencyWorkItemsPerStep {get;set;}=64;
    /// <summary>Grant one bounded residency tick; false means it is waiting for DMA.
    /// May be called while disposing a pending manually driven transition.</summary>
    public bool AdvanceResidency()=>residency!=null&&residency.Step();
    /// <summary>Release a redundant cold snapshot only after its model is resident.</summary>
    public bool ReleaseAramBacking()
    {
        Check();if(busy||!IsResident)throw new InvalidOperationException("Prepare the model and finish its transition before releasing its backing.");
        if(cold==null)return false;cold.Dispose();cold=null;return true;
    }
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
    /// <summary>Load original J3D1 BMD1 or Super Mario Sunshine J3D2 BMD3/BDL4 models and embedded BTI
    /// textures on the background executor. Optional BCK/BCA skeletal animation
    /// is sampled at 60 Hz into the clip "Motion". Supports Yaz0/Yay0 and RARC;
    /// append #relative/member.bmd or #relative/member.bck to select a member.
    /// J3D1 retains its qualified MAT1 state; complex J3D2 TEV materials need explicit approximation. Billboard geometry,
    /// scaled Maya segment compensation and material animations are unsupported.</summary>
    public static Task<Model> LoadJ3dAsync(string path,string? animationPath=null,float animationFramesPerSecond=30,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024,bool allowMaterialApproximation=false)
    {
        if(path==null)throw new ArgumentNullException("path");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if(!(animationFramesPerSecond>=1&&animationFramesPerSecond<=240))throw new ArgumentOutOfRangeException("animationFramesPerSecond");
        var work=new LoadWork(path,nativeBudgetBytes,cancellationToken,6,allowMaterialApproximation,motion:animationPath,fps:animationFramesPerSecond);
        Task.Run(work.Step);return work.Completion.Task;
    }
    /// <summary>Import an owned snapshot of a bounded J3D buffer on the background
    /// executor, without temporary files. Original J3D1/MAT1 retains its authored
    /// GX material profile; the existing strict J3D2 profile is also supported.</summary>
    public static Task<Model> LoadJ3dAsync(byte[] data,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(data==null)throw new ArgumentNullException(nameof(data));
        if(data.Length<32||data.Length>2*1024*1024)throw new global::System.IO.InvalidDataException("J3D memory source exceeds its input bounds.");
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new ArgumentOutOfRangeException(nameof(nativeBudgetBytes));
        cancellationToken.ThrowIfCancellationRequested();
        var work=new MemoryJ3dWork((byte[])data.Clone(),nativeBudgetBytes,cancellationToken);
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
        if(token==0)throw new global::System.IO.IOException("Invalid model package or insufficient memory.");
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
    /// <summary>Commit the ARAM copy before retiring main-RAM payloads. Cancellation
    /// before retirement retains residency; an already committed retirement finishes cold.</summary>
    public Task UnloadToAramAsync(CancellationToken cancellationToken=default)=>Transition(false,cancellationToken);
    /// <summary>Restore all resources from ARAM before drawing; never performs hidden I/O in DrawModel.</summary>
    public Task PrepareAsync(CancellationToken cancellationToken=default)=>Transition(true,cancellationToken);
    public Task PrepareAnimationAsync(string name,CancellationToken cancellationToken=default){Clip(name);return PrepareAsync(cancellationToken);}
    private Task Transition(bool restore,CancellationToken cancellation)
    {
        Check();if(busy)throw new InvalidOperationException("Model residency transition is active.");
        if(restore==IsResident)return Task.CompletedTask;
        if(!SupportsAramResidency)throw new NotSupportedException("This model does not retain a pageable immutable package.");
        if(!restore&&!Aram.IsAvailable)throw new InvalidOperationException("Initialize ARAM first.");
        if(restore&&cold==null)throw new InvalidOperationException("No ARAM backing package.");
        if(ResidencyBytesPerStep<32||ResidencyBytesPerStep>65536||(ResidencyBytesPerStep&31)!=0)throw new ArgumentOutOfRangeException("ResidencyBytesPerStep");
        if(ResidencyWorkItemsPerStep<1||ResidencyWorkItemsPerStep>1024)throw new ArgumentOutOfRangeException("ResidencyWorkItemsPerStep");
        var work=new ResidencyWork(this,restore,cancellation);busy=true;residency=work;
        try{work.Start();}catch{busy=false;residency=null;throw;}return work.Completion.Task;
    }
    internal void Check(){if(disposed)throw new ObjectDisposedException("Model");}
    internal void CheckInstanceUse(){if(busy)throw new InvalidOperationException("Model residency transition is active.");}
    public void Dispose(){if(disposed)return;if(busy){disposed=true;return;}if(NativeModel.ModelDispose(Token)<0)throw new InvalidOperationException("End the render frame before disposing models.");disposed=true;if(cold!=null){cold.Dispose();cold=null;}}
    private void EndWork(){busy=false;residency=null;if(disposed){NativeModel.ModelDispose(Token);if(cold!=null){cold.Dispose();cold=null;}}}
    private sealed class LoadWork
    {
        private readonly string path;private readonly int budget,format;private readonly CancellationToken cancellation;private readonly bool approximate;private int token;
        private readonly string? textures,motion;private readonly float fps;private readonly int eulerKeyBytes,selection;private readonly bool staticSnapshot;
        internal readonly TaskCompletionSource<Model> Completion=new TaskCompletionSource<Model>();
        internal LoadWork(string path,int budget,CancellationToken cancellation,int format=0,bool approximate=false,string? textures=null,string? motion=null,float fps=0,int eulerKeyBytes=16,int selection=-1,bool staticSnapshot=false){this.path=path;this.budget=budget;this.cancellation=cancellation;this.format=format;this.approximate=approximate;this.textures=textures;this.motion=motion;this.fps=fps;this.eulerKeyBytes=eulerKeyBytes;this.selection=selection;this.staticSnapshot=staticSnapshot;}
        internal void Step(){try{cancellation.ThrowIfCancellationRequested();if(token==0){token=format==10?NativeModel.ModelBeginPsoEpisode3(path,textures,motion,fps,eulerKeyBytes,budget,approximate?1:0):format>=6&&format<=9?NativeModel.ModelBeginFormat(path,textures,motion,fps,format,budget,(approximate?1:0)|(format==8&&staticSnapshot?2:0)):format==5?NativeModel.ModelBeginFzero(path,textures,selection,budget,(approximate?1:0)|(staticSnapshot?2:0)):format==1?NativeModel.ModelBeginScene(path,0,60,budget,approximate?1:0):format==2?NativeModel.ModelBeginGlb(path,budget,approximate?1:0):format==3?NativeModel.ModelBeginPso(path,textures,motion,fps,eulerKeyBytes,budget):format==4?NativeModel.ModelBeginScene(path,1,fps,budget,approximate?1:0):NativeModel.ModelBegin(path,budget);
                if(token==0)throw new global::System.IO.IOException("Model input, native budget or handle limit is invalid.");}
                int result=format!=0?1:NativeModel.ModelStep(token);if(result<0)throw new global::System.IO.IOException("Invalid or truncated model package, or insufficient native memory.");if(result==0){Task.Run(Step);return;}cancellation.ThrowIfCancellationRequested();var model=new Model(token);token=0;Completion.SetResult(model);}catch(Exception error){if(token!=0){try{NativeModel.ModelDispose(token);}catch(Exception closeError){error=closeError;}token=0;}Completion.SetException(error);}}
    }
    private sealed class MemoryJ3dWork
    {
        private byte[]? data;private readonly int budget;private readonly CancellationToken cancellation;
        internal readonly TaskCompletionSource<Model> Completion=new TaskCompletionSource<Model>();
        internal MemoryJ3dWork(byte[] data,int budget,CancellationToken cancellation){this.data=data;this.budget=budget;this.cancellation=cancellation;}
        internal void Step()
        {
            int token=0;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                token=NativeModel.ModelJ3dMemory(data!,budget);
                if(token==0)throw new global::System.IO.InvalidDataException("Unsupported or malformed J3D source, or insufficient native budget.");
                cancellation.ThrowIfCancellationRequested();
                var model=new Model(token);token=0;Completion.SetResult(model);
            }
            catch(Exception error)
            {
                if(token!=0)NativeModel.ModelDispose(token);
                Completion.SetException(error);
            }
            finally{data=null;}
        }
    }
    private sealed class ResidencyWork
    {
        private readonly Model model;private readonly bool restore;private readonly CancellationToken cancellation;
        private readonly int bytes,items;private readonly Action tick;
        private AramBuffer? storage;private Dolphin.Timing.Timer? timer;
        private bool begun,finished,manualScheduler;private Exception? failure;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal ResidencyWork(Model model,bool restore,CancellationToken cancellation)
        {this.model=model;this.restore=restore;this.cancellation=cancellation;bytes=model.ResidencyBytesPerStep;items=model.ResidencyWorkItemsPerStep;tick=Tick;}
        internal void Start()
        {
            if(!model.automaticResidencyProgress)return;
            manualScheduler=!Dolphin.Threading.Tasks.NativeAsync.AsyncRunning();
            if(manualScheduler)Scheduler.Post(tick);
            else timer=Dolphin.Timing.Timer.Schedule(tick,0,16);
        }
        private void Tick()
        {
            if(finished)return;
            long deadline=Dolphin.Timing.Clock.Timestamp+1000;
            int grants=0;
            do{if(!Step()||finished)break;}while(++grants<16&&Dolphin.Timing.Clock.Timestamp<deadline);
            if(!finished&&manualScheduler)Scheduler.Post(tick);
        }
        internal bool Step()
        {
            if(finished)return false;
            try
            {
                if(!begun)
                {
                    model.Check();cancellation.ThrowIfCancellationRequested();
                    if(!restore&&model.cold==null)storage=Aram.Allocate((model.PackageBytes+31)&~31);
                    int token=restore?model.cold!.NativeTransferToken:storage==null?0:storage.NativeTransferToken;
                    int started=NativeModel.ModelResidencyBegin(model.Token,token,restore?1:0);
                    if(started<0)throw started==-2?new OutOfMemoryException("Model residency exceeds its native budget."):new InvalidOperationException("Model residency requires an idle frame and a supported immutable package.");
                    begun=true;return true;
                }
                int result=NativeModel.ModelResidencyStep(model.Token,bytes,items,
                    cancellation.IsCancellationRequested||model.disposed||failure!=null?1:0);
                if(result==0||result==2)Aram.WakeTransfers();
                if(result==0)return false;
                if(result==2)return true;
                begun=false;
                if(result<0)throw failure??(model.disposed?new ObjectDisposedException("Model"):result==-3?new OperationCanceledException():result==-2?new OutOfMemoryException("Model residency exceeds its native budget."):new global::System.IO.IOException("Model residency integrity, DMA or reconstruction failed."));
                if(!restore&&storage!=null){model.cold=storage;storage=null;}
                Finish(model.disposed?new ObjectDisposedException("Model"):failure);return true;
            }
            catch(Exception error)
            {
                if(begun){failure=error;return true;} // Drain/rollback on the following granted ticks.
                Finish(error);return true;
            }
        }
        private void Finish(Exception? error)
        {
            finished=true;timer?.Dispose();timer=null;
            storage?.Dispose();storage=null;model.EndWork();
            if(error==null)Completion.SetResult(0);else Completion.SetException(error);
        }
    }
}
