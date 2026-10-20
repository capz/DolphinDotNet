using System;
namespace Dolphin.Graphics;

/// <summary>Immutable reusable program and mesh pipeline defaults. Owns no GPU resources.</summary>
public sealed class TevMaterialDefinition
{
    private readonly byte[] package;
    public LightingMode Lighting {get;}
    public TextureCoordinateMode TextureCoordinates {get;}
    public int StageCount => package[8];
    public bool RequiresTexture => package[12]!=0;
    public TevMaterialDefinition(TevProgramBuilder program,LightingMode lighting=LightingMode.Unlit,
        TextureCoordinateMode textureCoordinates=TextureCoordinateMode.Uv0)
    {
        if(program==null)throw new ArgumentNullException("program");
        if((int)lighting<0||(int)lighting>1||(int)textureCoordinates<0||(int)textureCoordinates>1)throw new ArgumentException("Invalid pipeline defaults.");
        package=program.BuildPackage();Lighting=lighting;TextureCoordinates=textureCoordinates;
    }
    public byte[] GetPackage(){var copy=new byte[package.Length];Array.Copy(package,copy,copy.Length);return copy;}
    /// <summary>Create between frames. The texture is borrowed and remains caller-owned.</summary>
    public TevMaterial Create(Texture2D? texture=null)
    {
        if((RequiresTexture||TextureCoordinates!=TextureCoordinateMode.Uv0)&&texture==null)throw new ArgumentException("This material requires a texture.");
        if(texture!=null)texture.Check();
        return new TevMaterial(GxEffect.FromPackage(package),texture,Lighting,TextureCoordinates);
    }
}

/// <summary>Owns its effect; borrows its texture. Reuse Material for Renderer.DrawMesh.
/// Change named Effect colors between frames. Disposing never disposes the texture.</summary>
public sealed class TevMaterial : IDisposable
{
    public Material Material {get;}
    public GxEffect Effect {get;}
    internal TevMaterial(GxEffect effect,Texture2D? texture,LightingMode lighting,TextureCoordinateMode coordinates)
    {
        Effect=effect;Material=new Material{Effect=effect,Texture=texture,Lighting=lighting,TextureCoordinates=coordinates};
    }
    public void Dispose()=>Effect.Dispose();
}

public sealed class ToonMaterialOptions
{
    public byte Low {get;set;}=65;
    public byte High {get;set;}=165;
    public Color32 Dark {get;set;}=new Color32(24,32,64);
    public Color32 Middle {get;set;}=new Color32(115,155,185);
    public Color32 Bright {get;set;}=Color32.White;
    public bool Textured {get;set;}=true;
}
public sealed class MetallicMaterialOptions
{
    public Color32 BaseColor {get;set;}=new Color32(70,80,90);
    public Color32 ReflectionTint {get;set;}=Color32.White;
    public byte ReflectionStrength {get;set;}=180;
}
public sealed class EmissiveMaterialOptions
{
    public Color32 BaseColor {get;set;}=new Color32(32,48,64);
    public Color32 EmissionColor {get;set;}=new Color32(40,220,255);
    public byte EmissionStrength {get;set;}=255;
}

/// <summary>Original visual approximations; no retail assets or game shader data.
/// All presets fit one texture, four colors and eight TEV stages.</summary>
public static class TevMaterialPresets
{
    public static TevMaterialDefinition WindWakerToon(ToonMaterialOptions? options=null)
    {
        var o=options??new ToonMaterialOptions();
        if(o.Low>=o.High)throw new ArgumentException("Toon thresholds must be ordered.");
        var b=new TevProgramBuilder();
        b.AddColor("Thresholds",new Color32(o.Low,o.High,0));b.AddColor("Dark",o.Dark);
        b.AddColor("Middle",o.Middle);b.AddColor("Bright",o.Bright);
        b.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Constant,TevRegister.Color0,colorSlot:0,component:TevComponent.Red);
        b.AddStage(TevInput.Raster,TevInput.Color0,TevInput.One,TevInput.Zero,TevRegister.Color0,TevOperation.GreaterRed);
        b.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Constant,TevRegister.Color1,colorSlot:1);
        b.AddStage(TevInput.Color1,TevInput.Constant,TevInput.Color0,TevInput.Zero,TevRegister.Color1,colorSlot:2);
        b.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Constant,TevRegister.Color0,colorSlot:0,component:TevComponent.Green);
        b.AddStage(TevInput.Raster,TevInput.Color0,TevInput.One,TevInput.Zero,TevRegister.Color0,TevOperation.GreaterRed);
        b.AddStage(TevInput.Color1,TevInput.Constant,TevInput.Color0,TevInput.Zero,colorSlot:3);
        if(o.Textured)b.AddStage(TevInput.Zero,TevInput.Previous,TevInput.Texture,TevInput.Zero);
        return new TevMaterialDefinition(b,LightingMode.Diffuse);
    }
    public static TevMaterialDefinition MetroidPrimeMetallic(MetallicMaterialOptions? options=null)
    {
        var o=options??new MetallicMaterialOptions();var b=new TevProgramBuilder();
        b.AddColor("BaseColor",o.BaseColor);b.AddColor("ReflectionTint",o.ReflectionTint);
        b.AddColor("ReflectionStrength",new Color32(o.ReflectionStrength,o.ReflectionStrength,o.ReflectionStrength));
        b.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Constant,TevInput.Zero,TevRegister.Color0,colorSlot:0);
        b.AddStage(TevInput.Zero,TevInput.Texture,TevInput.Constant,TevInput.Zero,colorSlot:1);
        b.AddStage(TevInput.Color0,TevInput.Previous,TevInput.Constant,TevInput.Zero,colorSlot:2);
        return new TevMaterialDefinition(b,LightingMode.Diffuse,TextureCoordinateMode.ViewNormal);
    }
    public static TevMaterialDefinition PsoEmissive(EmissiveMaterialOptions? options=null)
    {
        var o=options??new EmissiveMaterialOptions();var b=new TevProgramBuilder();
        b.AddColor("BaseColor",o.BaseColor);b.AddColor("EmissionColor",o.EmissionColor);
        b.AddColor("EmissionStrength",new Color32(o.EmissionStrength,o.EmissionStrength,o.EmissionStrength));
        b.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Constant,TevInput.Zero,TevRegister.Color0,colorSlot:0);
        b.AddStage(TevInput.Zero,TevInput.Texture,TevInput.Constant,TevInput.Zero,colorSlot:1);
        b.AddStage(TevInput.Zero,TevInput.Previous,TevInput.Constant,TevInput.Color0,colorSlot:2);
        return new TevMaterialDefinition(b,LightingMode.Diffuse);
    }
}
