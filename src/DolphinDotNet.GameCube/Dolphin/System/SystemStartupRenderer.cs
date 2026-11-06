using System;
using System.Threading;
using Dolphin.Graphics;
namespace Dolphin.System;

/// <summary>Optional original-startup rendition through the existing GX model
/// renderer. Owns its extracted resources; retains no catalog or ROM. Motion,
/// lighting and tile placement are reconstructed, not original animation tracks.</summary>
public sealed class SystemStartupRenderer : IDisposable
{
    private readonly SystemStartupSequence sequence=new SystemStartupSequence();
    public double ElapsedSeconds=>sequence.ElapsedSeconds;
    public SystemStartupStage Stage=>sequence.Stage;
    public bool IsComplete=>sequence.IsComplete;
    private readonly GraphicsDevice device;
    private readonly Renderer renderer;
    private readonly Camera camera=new Camera(40,0,.1f,100);
    private readonly Model?[] models=new Model?[5];
    private readonly ModelInstance?[] instances=new ModelInstance?[5];
    private readonly Transform[] transforms=new Transform[5];
    private readonly Transform tileTransform=new Transform();
    private readonly Material tileMaterial=new Material{Blend=BlendMode.Alpha,Cull=CullMode.None,DepthWrite=false};
    private Texture2D? tiles;
    private Mesh? quad;
    private bool disposed;
    private readonly int[] letterNodes=new int[13];
    private static readonly float[] path={
        -1.5f,2.01f,1.5f, -1.5f,2.01f,.5f, -.5f,2.01f,.5f, .5f,2.01f,.5f, 1.5f,2.01f,.5f,
        2.01f,1.5f,.5f, 2.01f,.5f,.5f, 2.01f,-.5f,.5f, 2.01f,-1.5f,.5f, 2.01f,-1.5f,1.5f,
        1.5f,-1.5f,2.01f, .5f,-1.5f,2.01f, -.5f,-1.5f,2.01f, -.5f,-.5f,2.01f, .5f,-.5f,2.01f};
    private SystemStartupRenderer(GraphicsDevice device){this.device=device;renderer=new Renderer(device);for(int i=0;i<5;i++)transforms[i]=new Transform();}
    /// <summary>Prepare between frames on the application executor. Imports use
    /// the existing background service and are awaited before GX publication.
    /// No sound starts implicitly. Cancellation/failure releases partial assets.</summary>
    public static SystemStartupRenderer Load(FirmwareCatalog catalog,CancellationToken cancellationToken=default,int nativeModelBudgetBytes=2*1024*1024)
    {
        if(catalog==null)throw new ArgumentNullException(nameof(catalog));
        CheckApplication();
        var device=GraphicsDevice.Default;
        if(device.IsFrameActive)throw new InvalidOperationException("Prepare startup resources between frames.");
        var result=new SystemStartupRenderer(device);
        try
        {
            var names=new[]{SystemModel.BaseCube,SystemModel.CoverCube,SystemModel.SmallCube,SystemModel.Logo,SystemModel.Wordmark};
            for(int i=0;i<names.Length;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.models[i]=SystemResources.CreateModelAsync(catalog,names[i],cancellationToken,nativeModelBudgetBytes).Result;
                result.instances[i]=result.models[i]!.CreateInstance();
            }
            var materialCounts=new[]{2,2,1,8,21};var nodeCounts=new[]{3,3,1,3,17};
            for(int i=0;i<5;i++)if(result.models[i]!.MaterialCount!=materialCounts[i]||result.models[i]!.NodeCount!=nodeCounts[i])
                throw new NotSupportedException("Unknown original startup model profile.");
            cancellationToken.ThrowIfCancellationRequested();
            result.tiles=SystemResources.CreateTexture(catalog,SystemTexture.Tiles,true);result.tileMaterial.Texture=result.tiles;
            result.quad=Mesh.Create(new[]{
                new VertexPositionColorTexture(new Vector3(-.5f,-.5f,0),Color32.White,new Vector2(0,1)),
                new VertexPositionColorTexture(new Vector3(.5f,-.5f,0),Color32.White,new Vector2(1,1)),
                new VertexPositionColorTexture(new Vector3(.5f,.5f,0),Color32.White,new Vector2(1,0)),
                new VertexPositionColorTexture(new Vector3(-.5f,.5f,0),Color32.White,new Vector2(0,0))},new ushort[]{0,1,2,0,2,3});
            for(int i=0;i<13;i++)
            {
                string name=i<8?"nintendo"+(i+1):"gamecube"+(i-7);int found=-1;
                for(int n=0;n<result.models[4]!.NodeCount;n++)if(result.models[4]!.GetNodeName(n)==name)found=n;
                if(found<0)throw new NotSupportedException("Unknown original wordmark joint profile.");
                result.letterNodes[i]=found;
            }
            result.Seek(0);cancellationToken.ThrowIfCancellationRequested();return result;
        }
        catch{result.Dispose();throw;}
    }
    /// <summary>Sum of per-resource native budget charges. Shared model textures
    /// can be charged more than once; this is not a unique-allocation peak.</summary>
    public int NativeBytes
    {
        get{Check();int bytes=(quad?.NativeBytes??0)+(tiles?.NativeBytes??0);for(int i=0;i<5;i++)bytes+=models[i]!.NativeBytes+instances[i]!.NativeBytes;return bytes;}
    }
    private static void CheckApplication(){if(!Dolphin.Threading.Tasks.Scheduler.IsApplicationThread)throw new InvalidOperationException("Use the application executor for startup preparation and rendering.");}
    private void Check(){if(disposed)throw new ObjectDisposedException(nameof(SystemStartupRenderer));CheckApplication();}
    public void Update(double deltaSeconds){Check();if(device.IsFrameActive)throw new InvalidOperationException("Update between frames.");sequence.Update(deltaSeconds);ApplyPose();}
    public void Seek(double seconds){Check();if(device.IsFrameActive)throw new InvalidOperationException("Seek between frames.");sequence.Seek(seconds);ApplyPose();}
    private static Quaternion Rotation(float x,float y,float z)
    {
        double hx=x*Math.PI/360,hy=y*Math.PI/360,hz=z*Math.PI/360;
        double a=Math.Sin(hx),b=Math.Cos(hx),c=Math.Sin(hy),d=Math.Cos(hy),e=Math.Sin(hz),f=Math.Cos(hz);
        return new Quaternion((float)(a*d*f+b*c*e),(float)(b*c*f-a*d*e),(float)(b*d*e+a*c*f),(float)(b*d*f-a*c*e));
    }
    private void ApplyPose()
    {
        float progress=sequence.CameraProgress;
        camera.LookAt(new Vector3(7*(1-progress),6*(1-progress),11+2*progress),new Vector3(0,-.6f,0),new Vector3(0,1,0));
        transforms[0].LocalScale=new Vector3(.01754f,.01754f,.01754f);
        transforms[1].LocalScale=new Vector3(.02387f,.02387f,.02387f);
        transforms[3].LocalScale=new Vector3(.02f,.02f,.02f);
        transforms[3].LocalPosition=new Vector3(0,.5f,0);
        transforms[3].LocalRotation=Rotation(30*progress,-45*progress,0);
        transforms[4].LocalScale=new Vector3(.02f,.02f,.02f);transforms[4].LocalPosition=new Vector3(-.2375f,.25f,0);
        float travel=sequence.TraceProgress;int step=Math.Min(14,(int)travel);float u=Math.Min(1,travel-step);int next=Math.Min(14,step+1);
        float x=path[step*3]+(path[next*3]-path[step*3])*u;
        float y=path[step*3+1]+(path[next*3+1]-path[step*3+1])*u;
        float z=path[step*3+2]+(path[next*3+2]-path[step*3+2])*u;
        float hop=(float)Math.Sin(u*Math.PI)*.2f;
        transforms[2].LocalPosition=new Vector3(x+(step>=5&&step<10?.5f+hop:0),y+(step<5?.5f+hop:0)+sequence.CubeRise*5,z+(step>=10?.5f+hop:0));
        transforms[2].LocalScale=new Vector3(.0188f,.0188f,.0188f);
        transforms[2].LocalRotation=step<5?Rotation(travel*90,0,0):step<10?Rotation(0,0,-travel*90):Rotation(0,travel*90,0);
        SetOpacity(0,sequence.CoverOpacity);SetOpacity(1,sequence.CoverOpacity);SetOpacity(2,sequence.CubeOpacity);SetOpacity(3,sequence.LogoOpacity);
        // Absolute alpha activates original wordmark materials whose rest alpha
        // is zero. Preserve their authored gray/white channel colors.
        for(int i=0;i<models[4]!.MaterialCount;i++)
        {
            instances[4]!.SetOpacity(i,sequence.Opacity*sequence.LetterProgress(i<8?i:8));
        }
        for(int i=0;i<13;i++)
        {
            float p=sequence.LetterProgress(i);
            instances[4]!.SetNodeOffset(letterNodes[i],new Vector3(0,(1-p)*45,0),Rotation(i<8?p*90:(1-p)*-96,0,0),new Vector3(1,1,1));
        }
    }
    private void SetOpacity(int index,float alpha)
    {for(int i=0;i<models[index]!.MaterialCount;i++)instances[index]!.SetOpacity(i,alpha);}
    /// <summary>Draw inside an application-owned device frame. Does not begin,
    /// clear or present that frame. Call Seek/Update before BeginFrame.</summary>
    public void Draw()
    {
        Check();if(!device.IsFrameActive)throw new InvalidOperationException("Begin a device frame first.");
        renderer.Begin3D(camera);
        try
        {
            if(sequence.CubeOpacity>0)renderer.DrawModel(instances[2]!,transforms[2]);
            for(int i=0;i<15;i++)
            {
                float alpha=sequence.TileOpacity(i);if(alpha<=0)continue;
                tileTransform.LocalPosition=new Vector3(path[i*3],path[i*3+1],path[i*3+2]);
                tileTransform.LocalRotation=i<5?Rotation(-90,0,0):i<10?Rotation(0,90,0):Quaternion.Identity;
                tileMaterial.Color=new Color32(99,79,188,(byte)(alpha*255));renderer.DrawMesh(quad!,tileMaterial,tileTransform);
            }
            if(sequence.CoverOpacity>0){renderer.DrawModel(instances[0]!,transforms[0]);renderer.DrawModel(instances[1]!,transforms[1]);}
            if(sequence.LogoOpacity>0)renderer.DrawModel(instances[3]!,transforms[3]);
            if(sequence.ElapsedSeconds>=3.65&&sequence.Opacity>0)renderer.DrawModel(instances[4]!,transforms[4]);
        }
        finally{renderer.End3D();}
    }
    public void Dispose()
    {
        if(disposed)return;CheckApplication();if(device.IsFrameActive)throw new InvalidOperationException("Dispose startup resources between frames.");
        for(int i=0;i<5;i++){instances[i]?.Dispose();models[i]?.Dispose();}
        quad?.Dispose();tiles?.Dispose();disposed=true;
    }
}
