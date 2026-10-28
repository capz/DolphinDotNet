using System;
namespace Dolphin.Graphics;

/// <summary>ViewNormal projects normalized camera-space normals onto a clamped reflection map.
/// WorldReflection samples a world-oriented equirectangular map using the perspective eye ray.
/// Both generated modes require normals and a map. With two textures, only the secondary slot uses generated coordinates.</summary>
public enum TextureCoordinateMode { Uv0, ViewNormal, WorldReflection }

/// <summary>Fixed-function material. Diffuse lighting uses a camera-space headlight.</summary>
public sealed partial class Material
{
    public GxEffect? Effect {get;set;}
    internal int EffectToken => Effect==null?0:Effect.Token;
    public Texture2D? Texture {get;set;}
    /// <summary>Optional independent reflection/emission texture. Coordinate mode applies to this slot when bound.</summary>
    public Texture2D? SecondaryTexture {get;set;}
    internal int SecondaryTextureToken => SecondaryTexture==null?0:SecondaryTexture.Token;
    public TextureCoordinateMode TextureCoordinates {get;set;}
    public Color32 Color {get;set;}=Color32.White;
    public BlendMode Blend {get;set;}
    public LightingMode Lighting {get;set;}
    public CullMode Cull {get;set;}=CullMode.Back;
    public bool DepthTest {get;set;}=true;
    public bool DepthWrite {get;set;}=true;
    public float AlphaCutoff {get;set;}
    internal int TextureToken => Texture==null?0:Texture.Token;
    internal int Flags => (int)Blend|(Cull==CullMode.Back?4:0)|(DepthWrite?0:8)|(DepthTest?0:16)|(Lighting==LightingMode.Diffuse?32:0)|(TextureCoordinates==TextureCoordinateMode.ViewNormal?64:TextureCoordinates==TextureCoordinateMode.WorldReflection?128:0);
    internal void Check()
    {
        if(Texture!=null)Texture.Check();
        if(SecondaryTexture!=null)SecondaryTexture.Check();
        if((int)TextureCoordinates<0||(int)TextureCoordinates>2||(TextureCoordinates!=TextureCoordinateMode.Uv0&&Texture==null&&SecondaryTexture==null))throw new ArgumentException("Invalid texture coordinates or missing reflection map.");
        if(Effect!=null){Effect.Check();if(Effect.RequiresTexture&&Texture==null)throw new ArgumentException("Effect requires a texture.");}
        if(Effect!=null&&Effect.RequiresSecondaryTexture&&SecondaryTexture==null)throw new ArgumentException("Effect requires a secondary texture.");
        if((int)Blend<0||(int)Blend>3||(int)Lighting<0||(int)Lighting>1||(int)Cull<0||(int)Cull>1||!(AlphaCutoff>=0&&AlphaCutoff<=1))throw new ArgumentException("Invalid material.");
    }
}
