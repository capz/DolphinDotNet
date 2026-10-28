using System;
namespace Dolphin.Graphics;
public sealed partial class Renderer
{
    // All renderers share the single device/pass and therefore its sampler state.
    private static bool optionsActive;
    private readonly int[] meshTokens;
    private readonly float[][] meshWorlds;
    public void Begin3D(Camera camera,RenderSettings settings)
    {if(settings==null)throw new ArgumentNullException("settings");if(camera==null)throw new ArgumentNullException("camera");var snapshot=settings.Snapshot(camera.NearPlane,camera.FarPlane,camera.IsOrthographic);Begin3D(camera);GraphicsDevice.Check(NativeGraphics.RenderScene(snapshot),"Invalid render settings.");}
    public void BeginFrame(Camera camera,RenderSettings settings)
    {if(settings==null||camera==null)throw new ArgumentNullException();var snapshot=settings.Snapshot(camera.NearPlane,camera.FarPlane,camera.IsOrthographic);BeginFrame(camera);GraphicsDevice.Check(NativeGraphics.RenderScene(snapshot),"Invalid render settings.");}
    private void ApplyMaterial(Material material)
    {if(material.NativeOptions!=null||optionsActive){GraphicsDevice.Check(NativeGraphics.RenderMaterial(material.NativeOptions),"Invalid material sampling state.");optionsActive=material.NativeOptions!=null;}}
    /// <summary>One native block within the reserved capacity, preserving order and indexed storage. No geometry merging or hardware instancing.</summary>
    public void DrawMeshes(Mesh[] meshes,Transform[] transforms,Material material,int offset,int count)
    {
        if(!active)throw new InvalidOperationException("Begin a 3D pass first.");
        if(meshes==null||transforms==null||material==null)throw new ArgumentNullException();
        if(offset<0||count<0||count>meshTokens.Length||offset>meshes.Length-count||offset>transforms.Length-count)throw new ArgumentOutOfRangeException("count");
        material.Check();
        try {
            for(int i=0;i<count;i++){
                var mesh=meshes[offset+i];var transform=transforms[offset+i];
                if(mesh==null||transform==null)throw new ArgumentException("Null mesh or transform.");
                mesh.Check();meshTokens[i]=mesh.Token;meshWorlds[i]=transform.WorldMatrix;
            }
            if(count>0)DrawMeshTokens(meshTokens,meshWorlds,count,material);
        } finally {for(int i=0;i<count;i++)meshWorlds[i]=null!;}

    }
    internal void DrawMeshTokens(int[] tokens,float[][] worlds,int count,Material material)
    {material.Check();ApplyMaterial(material);GraphicsDevice.Check(NativeGraphics.MeshDrawMany(tokens,worlds,count,material.TextureToken,material.SecondaryTextureToken,material.Color.Packed,material.Flags,material.AlphaCutoff,material.EffectToken),"Invalid mesh batch, transform or material.");}
    internal void DrawModelTokens(int[] tokens,float[] worlds,int count)
    {if(!active)throw new InvalidOperationException("Begin a 3D pass first.");if(optionsActive){GraphicsDevice.Check(NativeGraphics.RenderMaterial(null),"Cannot reset sampling.");optionsActive=false;}GraphicsDevice.Check(NativeModel.ModelDrawModels(tokens,worlds,count),"Invalid model batch.");}
}
