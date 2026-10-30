using System;
using System.IO;
using Dolphin.Diagnostics;
using Dolphin.Formats;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;

/// <summary>
/// Real GameCube DOL fixture. The external RSP host chooses one asset per fresh
/// A press and captures its exact presented XFB through PR #141.
/// No retail game data is bundled, copied to external storage or modified.
/// </summary>
internal static class Program
{
    private static readonly GraphicsDevice Device=GraphicsDevice.Default;
    private static readonly Renderer Renderer=new Renderer();
    private static readonly Camera Camera=new Camera();
    private static readonly float[] Identity={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    private static Texture2D? texture;
    private static Model? model;
    private static ModelInstance? instance;
    private static JpcParticleEmitter? particle;
    private static string mountedImage="";
    private static int current=-1, status=AssetCompatibilityProbe.Scanning;
    private static AssetCorpus corpus=new AssetCorpus();
    private static Draw2D? batch;

    private static void Release()
    {
        particle?.Dispose();particle=null;
        instance?.Dispose();instance=null;
        model?.Dispose();model=null;
        texture?.Dispose();texture=null;
    }

    private static void Select(int index)
    {
        Release();
        var item=corpus[index];
        current=index;
        status=AssetCompatibilityProbe.Fail;
        try
        {
            if(item.SourceImage!=mountedImage)
            {
                if(mountedImage.Length>0)Storage.Unmount(StorageDevice.DvdDrive);
                mountedImage="";
                if(item.SourceImage.Length>0)
                {
                    if(!Storage.MountDisc(item.SourceImage))
                        throw new IOException("Could not remount the source disc.");
                    mountedImage=item.SourceImage;
                }
            }
            if(item.Kind==AssetFixturePolicy.Texture)
            {
                var raw=AssetCorpus.ReadAsset(item);
                var meta=BtiTexture.Read(raw);
                if(meta.Width>256||meta.Height>256)
                    throw new NotSupportedException("Large BTI excluded by the hardware fixture memory budget.");
                texture=Texture2D.FromBti(raw,linear:false);
            }
            else if(item.Kind==AssetFixturePolicy.Particle)
            {
                var data=AssetCorpus.ReadAsset(item);
                var archive=JpcArchive.Read(data);
                if(archive.Particles.Count==0)throw new InvalidDataException("Empty JPAC particle archive.");
                for(int i=0;i<archive.Particles.Count&&i<12;i++)
                {
                    try {
                        particle=new JpcParticleEmitter(archive,archive.Particles[i].Id,maxParticles:64,seed:17);
                        break;
                    }catch(NotSupportedException){}
                }
                if(particle==null)throw new NotSupportedException("No particle uses the supported JPA playback profile.");
                // Deterministic simulation step count independent of console speed.
                for(int frame=0;frame<30;frame++)particle.Update(1f/60f);
            }
            else if(item.Kind==AssetFixturePolicy.Model)
            {
                var source=item.Path+(item.Member.Length>0?"#"+item.Member:"");
                model=Model.LoadJ3dAsync(source,allowMaterialApproximation:true,
                    nativeBudgetBytes:4*1024*1024).Result;
                instance=model.CreateInstance();
            }
            else throw new NotSupportedException("Unsupported asset family.");
            status=AssetCompatibilityProbe.Pass;
        }
        catch(NotSupportedException)
        {
            Release();
            status=AssetCompatibilityProbe.Unsupported;
        }
        catch(Exception)
        {
            Release();
            status=AssetCompatibilityProbe.Fail;
        }
        AssetCompatibilityProbe.Report(status,current,corpus.Count,item.Kind,
            corpus.Visited,item.Name);
    }

    private static void Frame()
    {
        // No animation is based on VI wall-clock time: golden hash comparisons
        // sample an identical simulation age for the same selected asset.
        Device.BeginFrame(Color32.Black);
        if(texture!=null && batch!=null)
        {
            batch.Begin();
            int width=Device.Width, height=Device.Height;
            // Show original aspect, entire image, no interpolation imposed
            // beyond the native texture's authored sampler.
            float factor=Math.Min(width/(float)texture.Width,height/(float)texture.Height);
            float w=texture.Width*factor,h=texture.Height*factor;
            batch.DrawTexture(texture,new RectF((width-w)/2,(height-h)/2,w,h),Color32.White);
            batch.End();
        }
        else if(instance!=null || particle!=null)
        {
            Renderer.Begin3D(Camera);
            if(instance!=null)Renderer.DrawModel(instance,Identity);
            if(particle!=null)particle.Draw(Renderer,Camera);
            Renderer.End3D();
        }
        Device.EndFrame();
    }

    private static void Main()
    {
        DebugOverlay.TextVisible=false;
        DebugOverlay.PerformanceVisible=false;
        AssetCompatibilityProbe.Report(AssetCompatibilityProbe.Scanning,-1,0,0,0,"gcloader:/games");
        string? problem=null;
        try{corpus.Scan();}
        catch(Exception error){problem=error.GetType().Name;}

        if(problem==null && corpus.Count>0)
        {
            status=AssetCompatibilityProbe.Ready;
            AssetCompatibilityProbe.Report(status,-1,corpus.Count,0,corpus.Visited,"ready");
        }
        else
        {
            status=problem==null?AssetCompatibilityProbe.Empty:AssetCompatibilityProbe.Fail;
            AssetCompatibilityProbe.Report(status,-1,0,0,corpus.Visited,problem??"no-supported-assets");
        }

        Camera.LookAt(new Vector3(0,1.5f,5),Vector3.Zero,new Vector3(0,1,0));
        batch=new Draw2D(Device,new Draw2DOptions { VertexCapacity=32 });
        // The debugger controls test progression through synthetic A events.
        // Keep reporting the same case until a fresh press, so stops/captures
        // cannot change the sampled asset due to target wall-clock time.
        while(true)
        {
            Controller.Update();
            if(status!=AssetCompatibilityProbe.Fail && corpus.Count>0 &&
                Controller.GetState(0).A.WasPressed)
            {
                if(current+1<corpus.Count)Select(current+1);
                else if(status!=AssetCompatibilityProbe.Finished)
                {
                    status=AssetCompatibilityProbe.Finished;
                    AssetCompatibilityProbe.Report(status,current,corpus.Count,0,
                        corpus.Visited,"finished");
                }
            }
            Frame();
        }
    }
}
