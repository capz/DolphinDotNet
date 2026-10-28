using System;
namespace Dolphin.Graphics;
public enum TextureAddressMode { Default,Clamp,Repeat,Mirror }
public enum TextureFilter { Default,Nearest,Linear }
public readonly struct SamplerState
{
    public TextureAddressMode U {get;} public TextureAddressMode V {get;} public TextureFilter Filter {get;}
    public SamplerState(TextureAddressMode u,TextureAddressMode v,TextureFilter filter=TextureFilter.Default)
    {if((int)u<0||(int)u>3||(int)v<0||(int)v>3||(int)filter<0||(int)filter>2)throw new ArgumentException("Invalid sampler.");U=u;V=v;Filter=filter;}
}
public readonly struct UvTransform
{
    private readonly bool set;
    private readonly float a,b,c,d,x,y;
    public static UvTransform Identity=>default;
    public UvTransform(Vector2 offset,Vector2 scale,float rotationDegrees=0)
    {
        GraphicsMath.Finite(offset.X);GraphicsMath.Finite(offset.Y);GraphicsMath.Finite(scale.X);GraphicsMath.Finite(scale.Y);GraphicsMath.Finite(rotationDegrees);
        float angle=rotationDegrees*.01745329252f,co=(float)Math.Cos(angle),si=(float)Math.Sin(angle);
        a=co*scale.X;b=si*scale.X;c=-si*scale.Y;d=co*scale.Y;x=offset.X;y=offset.Y;set=true;
    }
    public Vector2 Transform(Vector2 value)=>set?new Vector2(a*value.X+c*value.Y+x,b*value.X+d*value.Y+y):value;
    internal void Write(float[] values,int at){values[at]=set?a:1;values[at+1]=set?b:0;values[at+2]=set?c:0;values[at+3]=set?d:1;values[at+4]=set?x:0;values[at+5]=set?y:0;}
}
public enum LightKind { Directional,Point,Spot }
public struct SceneLight
{
    public LightKind Kind;public Color32 Color;public Vector3 Position,Direction;
    /// <summary>Point/spot distance at which intensity is one half.</summary>
    public float ReferenceDistance;
    public float SpotCutoffDegrees;
    public SceneLight(LightKind kind,Color32 color,Vector3 position,Vector3 direction,float referenceDistance=10,float spotCutoffDegrees=45)
    {Kind=kind;Color=color;Position=position;Direction=direction;ReferenceDistance=referenceDistance;SpotCutoffDegrees=spotCutoffDegrees;}
}
public enum FogMode { None,Linear,Exponential,ExponentialSquared }
public struct FogSettings
{
    public FogMode Mode;public Color32 Color;public float Start,End;
    public FogSettings(FogMode mode,Color32 color,float start,float end){Mode=mode;Color=color;Start=start;End=end;}
}
/// <summary>Snapshot at Begin3D. Allocate once; lights are world-space and do not own resources.</summary>
public sealed class RenderSettings
{
    public Color32 Ambient {get;set;}=new Color32(64,64,64);
    public FogSettings Fog {get;set;}
    public int LightCount {get;set;}
    public SceneLight[] Lights {get;}=new SceneLight[8];
    private readonly float[] native=new float[136];
    internal float[] Snapshot(float near,float far,bool ortho)
    {
        if(LightCount<0||LightCount>8)throw new ArgumentOutOfRangeException("LightCount");
        native[0]=1;native[1]=Ambient.R;native[2]=Ambient.G;native[3]=Ambient.B;
        var f=Fog;if((int)f.Mode<0||(int)f.Mode>3)throw new ArgumentException("Invalid fog mode.");
        GraphicsMath.Finite(f.Start);GraphicsMath.Finite(f.End);if(f.Mode!=FogMode.None&&(!(f.End>f.Start)||f.Start<0))throw new ArgumentException("Invalid fog range.");
        native[4]=(int)f.Mode;native[5]=f.Start;native[6]=f.End;native[7]=f.Color.R;native[8]=f.Color.G;native[9]=f.Color.B;native[10]=near;native[11]=far;native[12]=ortho?1:0;native[13]=LightCount;
        for(int i=0;i<LightCount;i++){
            var l=Lights[i];if((int)l.Kind<0||(int)l.Kind>2)throw new ArgumentException("Invalid light kind.");
            float n=GraphicsMath.Dot(l.Direction,l.Direction);GraphicsMath.Finite(n);
            GraphicsMath.Finite(l.Position.X);GraphicsMath.Finite(l.Position.Y);GraphicsMath.Finite(l.Position.Z);GraphicsMath.Finite(l.ReferenceDistance);GraphicsMath.Finite(l.SpotCutoffDegrees);
            if(l.Kind!=LightKind.Point&&!(n>0))throw new ArgumentException("Light direction is zero.");
            if(l.Kind!=LightKind.Directional&&!(l.ReferenceDistance>0))throw new ArgumentException("Invalid attenuation distance.");
            if(l.Kind==LightKind.Spot&&(!(l.SpotCutoffDegrees>0)||l.SpotCutoffDegrees>90))throw new ArgumentException("Invalid spot cutoff.");
            int at=16+i*15;native[at]=(int)l.Kind;native[at+1]=l.Color.R;native[at+2]=l.Color.G;native[at+3]=l.Color.B;native[at+4]=l.Position.X;native[at+5]=l.Position.Y;native[at+6]=l.Position.Z;
            native[at+7]=l.Direction.X;native[at+8]=l.Direction.Y;native[at+9]=l.Direction.Z;native[at+10]=l.ReferenceDistance;native[at+11]=l.SpotCutoffDegrees;
        }return native;
    }
}
