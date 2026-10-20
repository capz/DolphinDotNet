using System;
using Dolphin.Graphics;

// Deterministic geometry for software-Dolphin pixel acceptance. No controls or UI.
internal sealed class PresetRenderProof : IDisposable
{
    private readonly Texture2D albedo=Solid(80,120,160,255),reflection=Solid(200,80,40,0),emission=Solid(40,80,120,0);
    private readonly Texture2D worldMap;
    private readonly TevMaterial toon,metal,glow,world,modelGlow;
    private readonly Mesh[] patches=new Mesh[5];
    private readonly Material plain;
    private readonly Transform identity=new Transform(),modelTransform=new Transform();
    private readonly Model model=Model.FromPackage(ImportedPanels.Package);
    private readonly ModelInstance instance;
    internal PresetRenderProof()
    {
        var pixels=new byte[8*4*4];
        for(int y=0;y<4;y++)for(int x=0;x<8;x++){int i=(y*8+x)*4;pixels[i]=(byte)(x<4?220:20);pixels[i+1]=40;pixels[i+2]=(byte)(x<4?20:220);pixels[i+3]=255;}
        worldMap=Texture2D.Create(8,4,pixels);
        toon=TevMaterialPresets.Toon(new ToonMaterialOptions{Dark=Color32.Black,Middle=new Color32(128,128,128),Bright=Color32.White}).Create(albedo);
        metal=TevMaterialPresets.MetallicReflection(new MetallicMaterialOptions{SeparateBaseTexture=true,BaseColor=Color32.White,ReflectionStrength=128}).Create(albedo,reflection);
        glow=TevMaterialPresets.Emissive(new EmissiveMaterialOptions{SeparateBaseTexture=true,BaseColor=Color32.White,EmissionColor=Color32.White,EmissionStrength=128}).Create(albedo,emission);
        world=TevMaterialPresets.MetallicReflection(new MetallicMaterialOptions{SeparateBaseTexture=true,BaseColor=Color32.Black,ReflectionStrength=255,ReflectionCoordinates=TextureCoordinateMode.WorldReflection}).Create(albedo,worldMap);
        modelGlow=TevMaterialPresets.Emissive(new EmissiveMaterialOptions{SeparateBaseTexture=true,BaseColor=Color32.Black,EmissionColor=Color32.White}).Create(albedo,emission);
        Configure(toon.Material);Configure(metal.Material);Configure(glow.Material);Configure(world.Material);Configure(modelGlow.Material);
        plain=new Material{Texture=albedo};Configure(plain);
        for(int i=0;i<5;i++)patches[i]=Patch(36+i*80,i==4?new Vector3(-.70710678f,0,.70710678f):new Vector3(0,0,1),i==0?(byte)128:(byte)255);
        instance=model.CreateInstance();modelTransform.LocalScale=new Vector3(1,.5f,1);modelTransform.LocalPosition=new Vector3(208f/240,-184f/240,0);
    }
    private static void Configure(Material m){m.Lighting=LightingMode.Unlit;m.Cull=CullMode.None;m.DepthTest=false;m.DepthWrite=false;m.Blend=BlendMode.Alpha;}
    private static Texture2D Solid(byte r,byte g,byte b,byte a){var p=new byte[64];for(int i=0;i<64;i+=4){p[i]=r;p[i+1]=g;p[i+2]=b;p[i+3]=a;}return Texture2D.Create(4,4,p);}
    private static Mesh Patch(float x,Vector3 normal,byte intensity)
    {
        var c=new Color32(intensity,intensity,intensity);
        var a=new VertexPositionNormalColorTexture(new Vector3((x-320)/240,(240-356)/240f,0),normal,c,new Vector2(.5f,.5f));
        var b=new VertexPositionNormalColorTexture(new Vector3((x+56-320)/240,(240-356)/240f,0),normal,c,new Vector2(.5f,.5f));
        var d=new VertexPositionNormalColorTexture(new Vector3((x-320)/240,(240-368)/240f,0),normal,c,new Vector2(.5f,.5f));
        var e=new VertexPositionNormalColorTexture(new Vector3((x+56-320)/240,(240-368)/240f,0),normal,c,new Vector2(.5f,.5f));
        return Mesh.Create(new[]{a,b,e,a,e,d});
    }
    internal void Draw(Renderer renderer)
    {
        renderer.DrawMesh(patches[0],toon.Material,identity);renderer.DrawMesh(patches[1],metal.Material,identity);
        renderer.DrawMesh(patches[2],glow.Material,identity);renderer.DrawMesh(patches[3],plain,identity);
        renderer.DrawMesh(patches[4],world.Material,identity);renderer.DrawModelWithMaterial(instance,modelTransform,modelGlow.Material);
    }
    public void Dispose(){instance.Dispose();model.Dispose();foreach(var p in patches)p.Dispose();modelGlow.Dispose();world.Dispose();glow.Dispose();metal.Dispose();toon.Dispose();worldMap.Dispose();emission.Dispose();reflection.Dispose();albedo.Dispose();}
}
