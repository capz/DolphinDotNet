using System;
namespace Dolphin.Graphics;

/// <summary>Immutable reusable program and mesh pipeline defaults. Owns no GPU resources.</summary>
public sealed class TevMaterialDefinition
{
    private readonly byte[] package;
    public LightingMode Lighting {get;}
    public TextureCoordinateMode TextureCoordinates {get;}
    public int StageCount => package[8];
    public bool RequiresTexture => (package[12]&1)!=0;
    public bool RequiresSecondaryTexture => (package[12]&2)!=0;
    public TevMaterialDefinition(TevProgramBuilder program,LightingMode lighting=LightingMode.Unlit,
        TextureCoordinateMode textureCoordinates=TextureCoordinateMode.Uv0)
    {
        if(program==null)throw new ArgumentNullException("program");
        if((int)lighting<0||(int)lighting>1||(int)textureCoordinates<0||(int)textureCoordinates>3)throw new ArgumentException("Invalid pipeline defaults.");
        package=program.BuildPackage();Lighting=lighting;TextureCoordinates=textureCoordinates;
    }
    public byte[] GetPackage(){var copy=new byte[package.Length];Array.Copy(package,copy,copy.Length);return copy;}
    /// <summary>Create between frames. The texture is borrowed and remains caller-owned.</summary>
    public TevMaterial Create(Texture2D? texture=null,Texture2D? secondaryTexture=null)
    {
        if(RequiresTexture&&texture==null)throw new ArgumentException("This material requires a base texture.");
        if(RequiresSecondaryTexture&&secondaryTexture==null)throw new ArgumentException("This material requires a secondary texture.");
        if(TextureCoordinates!=TextureCoordinateMode.Uv0&&texture==null&&secondaryTexture==null)throw new ArgumentException("This material requires a reflection map.");
        if(TextureCoordinates==TextureCoordinateMode.ScreenProjection&&texture==null)throw new ArgumentException("Screen projection requires a primary capture texture.");
        if(texture!=null)texture.Check();
        if(secondaryTexture!=null)secondaryTexture.Check();
        return new TevMaterial(GxEffect.FromPackage(package),texture,Lighting,TextureCoordinates,secondaryTexture);
    }
}

/// <summary>Owns its effect; borrows its texture. Reuse Material for Renderer.DrawMesh.
/// Change named Effect colors between frames. Disposing never disposes the texture.</summary>
public sealed class TevMaterial : IDisposable
{
    public Material Material {get;}
    public GxEffect Effect {get;}
    internal TevMaterial(GxEffect effect,Texture2D? texture,LightingMode lighting,TextureCoordinateMode coordinates,Texture2D? secondaryTexture=null)
    {
        Effect=effect;Material=new Material{Effect=effect,Texture=texture,SecondaryTexture=secondaryTexture,Lighting=lighting,TextureCoordinates=coordinates};
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
    public bool SeparateBaseTexture {get;set;}
    public TextureCoordinateMode ReflectionCoordinates {get;set;}=TextureCoordinateMode.ViewNormal;
    public Color32 BaseColor {get;set;}=new Color32(70,80,90);
    public Color32 ReflectionTint {get;set;}=Color32.White;
    public byte ReflectionStrength {get;set;}=180;
}
public sealed class EmissiveMaterialOptions
{
    public bool SeparateBaseTexture {get;set;}
    public Color32 BaseColor {get;set;}=new Color32(32,48,64);
    public Color32 EmissionColor {get;set;}=new Color32(40,220,255);
    public byte EmissionStrength {get;set;}=255;
}

/// <summary>Original visual approximations; no retail assets or game shader data.
/// Presets use at most two textures, four colors and eight total TEV stages.</summary>
public static class TevMaterialPresets
{
    /// <summary>Two independently scrolling glint samples. Alpha is raster
    /// alpha times the mean of both texture alphas. GX keeps alpha outside the
    /// open low/high band (endpoints pass), using exact byte thresholds and no extra texture.</summary>
    public static TevMaterialDefinition DualTextureGlints(Color32 primaryLight,Color32 reflectedLight,
        byte lowReference=33,byte highReference=235)
    {
        if(lowReference>=highReference)throw new ArgumentException("Glint alpha thresholds must be ordered.");
        var p=new TevProgramBuilder();p.AddColor("PrimaryLight",primaryLight);p.AddColor("ReflectedLight",reflectedLight);
        p.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
        p.AddStage(TevInput.Zero,TevInput.Constant,TevInput.Texture,TevInput.Previous,colorSlot:0);
        p.AddStage(TevInput.Zero,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Previous,colorSlot:1);
        p.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Texture,TevAlphaInput.Raster,TevAlphaInput.Zero,TevRegister.Color0);
        p.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Color0);
        p.SetAlphaStage(2,TevAlphaInput.Zero,TevAlphaInput.SecondaryTexture,TevAlphaInput.Raster,TevAlphaInput.Color0,scale:TevScale.Half);
        p.SetAlphaCompare(AlphaComparison.LessOrEqual,lowReference,AlphaCompareOperation.Or,AlphaComparison.GreaterOrEqual,highReference);
        return new TevMaterialDefinition(p);
    }
    /// <summary>GameCube-style moving foam: two independent UV samples of one
    /// color/foam mask, displaced by the second texture as a GX indirect map.
    /// The distortion map is never used as a visible RGB texture.</summary>
    public static TevMaterialDefinition WaterWarp(Color32 foam,Color32 secondaryFoam,byte strength=76)
    {
        var program=new TevProgramBuilder();
        program.EnableIndirectWarp(strength);
        program.AddColor("Foam",foam);program.AddColor("Foam2",secondaryFoam);
        program.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
        program.AddStage(TevInput.Previous,TevInput.Constant,TevInput.Texture,TevInput.Zero,colorSlot:0);
        program.AddStage(TevInput.Previous,TevInput.Constant,TevInput.Texture,TevInput.Zero,colorSlot:1,alternateUv:true);
        return new TevMaterialDefinition(program,LightingMode.Unlit);
    }
    public static TevMaterialDefinition Toon(ToonMaterialOptions? options=null)
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
    public static TevMaterialDefinition MetallicReflection(MetallicMaterialOptions? options=null)
    {
        var o=options??new MetallicMaterialOptions();var b=new TevProgramBuilder();
        b.AddColor("BaseColor",o.BaseColor);b.AddColor("ReflectionTint",o.ReflectionTint);
        b.AddColor("ReflectionStrength",new Color32(o.ReflectionStrength,o.ReflectionStrength,o.ReflectionStrength));
        b.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Constant,TevInput.Zero,TevRegister.Color0,colorSlot:0);
        if(o.SeparateBaseTexture)b.AddStage(TevInput.Zero,TevInput.Color0,TevInput.Texture,TevInput.Zero,TevRegister.Color0);
        b.AddStage(TevInput.Zero,o.SeparateBaseTexture?TevInput.SecondaryTexture:TevInput.Texture,TevInput.Constant,TevInput.Zero,colorSlot:1);
        b.AddStage(TevInput.Color0,TevInput.Previous,TevInput.Constant,TevInput.Zero,colorSlot:2);
        return new TevMaterialDefinition(b,LightingMode.Diffuse,o.ReflectionCoordinates);
    }
    public static TevMaterialDefinition Emissive(EmissiveMaterialOptions? options=null)
    {
        var o=options??new EmissiveMaterialOptions();var b=new TevProgramBuilder();
        b.AddColor("BaseColor",o.BaseColor);b.AddColor("EmissionColor",o.EmissionColor);
        b.AddColor("EmissionStrength",new Color32(o.EmissionStrength,o.EmissionStrength,o.EmissionStrength));
        b.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Constant,TevInput.Zero,TevRegister.Color0,colorSlot:0);
        if(o.SeparateBaseTexture)b.AddStage(TevInput.Zero,TevInput.Color0,TevInput.Texture,TevInput.Zero,TevRegister.Color0);
        b.AddStage(TevInput.Zero,o.SeparateBaseTexture?TevInput.SecondaryTexture:TevInput.Texture,TevInput.Constant,TevInput.Zero,colorSlot:1);
        b.AddStage(TevInput.Zero,TevInput.Previous,TevInput.Constant,TevInput.Color0,colorSlot:2);
        return new TevMaterialDefinition(b,LightingMode.Diffuse);
    }
}
