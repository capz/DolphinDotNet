using System;
namespace Dolphin.Graphics;

/// <summary>Fixed-function material. Diffuse lighting uses a camera-space headlight.</summary>
public sealed class Material
{
    public Texture2D? Texture {get;set;}
    public Color32 Color {get;set;}=Color32.White;
    public BlendMode Blend {get;set;}
    public LightingMode Lighting {get;set;}
    public CullMode Cull {get;set;}=CullMode.Back;
    public bool DepthTest {get;set;}=true;
    public bool DepthWrite {get;set;}=true;
    public float AlphaCutoff {get;set;}
    internal int TextureToken => Texture==null?0:Texture.Token;
    internal int Flags => (int)Blend|(Cull==CullMode.Back?4:0)|(DepthWrite?0:8)|(DepthTest?0:16)|(Lighting==LightingMode.Diffuse?32:0);
    internal void Check()
    {
        if(Texture!=null)Texture.Check();
        if((int)Blend<0||(int)Blend>3||(int)Lighting<0||(int)Lighting>1||(int)Cull<0||(int)Cull>1||!(AlphaCutoff>=0&&AlphaCutoff<=1))throw new ArgumentException("Invalid material.");
    }
}
