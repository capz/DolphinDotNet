using System;
namespace Dolphin.Graphics;

/// <summary>World-to-logical-screen camera. Rotation is clockwise degrees in Y-down space.</summary>
public sealed class Camera2D
{
    public Vector2 Position { get; set; }
    public Vector2 Origin { get; set; }
    public float Zoom { get; set; } = 1;
    public float RotationDegrees { get; set; }
    internal void Matrix(float[] m)
    {
        GraphicsMath.Finite(Position.X);GraphicsMath.Finite(Position.Y);
        GraphicsMath.Finite(Origin.X);GraphicsMath.Finite(Origin.Y);GraphicsMath.Finite(RotationDegrees);
        if(!(Zoom>0)||float.IsInfinity(Zoom))throw new ArgumentOutOfRangeException("Zoom");
        float a=RotationDegrees*.01745329252f,c=(float)Math.Cos(a)*Zoom,s=(float)Math.Sin(a)*Zoom;
        m[0]=c;m[1]=s;m[2]=-s;m[3]=c;
        m[4]=Origin.X-c*Position.X+s*Position.Y;m[5]=Origin.Y-s*Position.X-c*Position.Y;
        for(int i=0;i<6;i++)GraphicsMath.Finite(m[i]);
    }
    public Vector2 WorldToScreen(Vector2 value)
    {
        Validate();GraphicsMath.Finite(value.X);GraphicsMath.Finite(value.Y);
        float a=RotationDegrees*.01745329252f,c=(float)Math.Cos(a)*Zoom,s=(float)Math.Sin(a)*Zoom;
        return new Vector2(Origin.X+c*(value.X-Position.X)-s*(value.Y-Position.Y),Origin.Y+s*(value.X-Position.X)+c*(value.Y-Position.Y));
    }
    public Vector2 ScreenToWorld(Vector2 value)
    {
        Validate();GraphicsMath.Finite(value.X);GraphicsMath.Finite(value.Y);float a=RotationDegrees*.01745329252f,c=(float)Math.Cos(a)/Zoom,s=(float)Math.Sin(a)/Zoom;
        return new Vector2(Position.X+c*(value.X-Origin.X)+s*(value.Y-Origin.Y),Position.Y-s*(value.X-Origin.X)+c*(value.Y-Origin.Y));
    }
    private void Validate(){GraphicsMath.Finite(Position.X);GraphicsMath.Finite(Position.Y);GraphicsMath.Finite(Origin.X);GraphicsMath.Finite(Origin.Y);GraphicsMath.Finite(RotationDegrees);if(!(Zoom>0)||float.IsInfinity(Zoom))throw new ArgumentOutOfRangeException("Zoom");}
}
public readonly struct Viewport
{
    public int X {get;} public int Y {get;} public int Width {get;} public int Height {get;}
    public Viewport(int x,int y,int width,int height){if(x<0||y<0||width<=0||height<=0||x>int.MaxValue-width||y>int.MaxValue-height)throw new ArgumentOutOfRangeException("viewport");X=x;Y=y;Width=width;Height=height;}
}
internal static class GraphicsMath
{
    internal static void Finite(float x){if(float.IsNaN(x)||float.IsInfinity(x))throw new ArgumentException("Graphics values must be finite.");}
    internal static float Dot(Vector3 a,Vector3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
}
public readonly struct Ray
{
    public Vector3 Origin {get;} public Vector3 Direction {get;}
    public Ray(Vector3 origin,Vector3 direction){Origin=origin;Direction=direction;}
}
public readonly struct BoundingSphere
{
    public Vector3 Center {get;} public float Radius {get;}
    public BoundingSphere(Vector3 center,float radius){GraphicsMath.Finite(center.X);GraphicsMath.Finite(center.Y);GraphicsMath.Finite(center.Z);GraphicsMath.Finite(radius);if(radius<0)throw new ArgumentOutOfRangeException("radius");Center=center;Radius=radius;}
}
public readonly struct BoundingBox
{
    public Vector3 Min {get;} public Vector3 Max {get;}
    public BoundingBox(Vector3 min,Vector3 max){GraphicsMath.Finite(min.X);GraphicsMath.Finite(min.Y);GraphicsMath.Finite(min.Z);GraphicsMath.Finite(max.X);GraphicsMath.Finite(max.Y);GraphicsMath.Finite(max.Z);if(min.X>max.X||min.Y>max.Y||min.Z>max.Z)throw new ArgumentException("Inverted bounds.");Min=min;Max=max;}
}
