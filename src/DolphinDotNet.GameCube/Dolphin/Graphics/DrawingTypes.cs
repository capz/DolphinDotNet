namespace Dolphin.Graphics;

public struct Color32
{
    public byte R,G,B,A;
    public Color32(byte r,byte g,byte b,byte a=255){R=r;G=g;B=b;A=a;}
    public uint Packed => (uint)R<<24|(uint)G<<16|(uint)B<<8|A;
    public static Color32 White => new Color32(255,255,255);
    public static Color32 Black => new Color32(0,0,0);
    public static Color32 Transparent => new Color32(0,0,0,0);
}
public struct Vector2
{
    public float X,Y;
    public Vector2(float x,float y){X=x;Y=y;}
    public static Vector2 Zero => default;
}
public struct RectF
{
    public float X,Y,Width,Height;
    public RectF(float x,float y,float width,float height){X=x;Y=y;Width=width;Height=height;}
}
public struct Border
{
    public float Left,Top,Right,Bottom;
    public Border(float left,float top,float right,float bottom){Left=left;Top=top;Right=right;Bottom=bottom;}
}
public enum PrimitiveType { Triangles=0x90, Lines=0xa8, Points=0xb8 }
public enum BlendMode { Opaque,Alpha,Additive,PremultipliedAlpha }
public enum LightingMode { Unlit,Diffuse }
public enum CullMode { None,Back }
public struct VertexPositionColor
{
    public Vector3 Position;
    public Color32 Color;
    public VertexPositionColor(Vector3 position,Color32 color){Position=position;Color=color;}
}
public struct VertexPositionColorTexture
{
    public Vector3 Position;
    public Color32 Color;
    public Vector2 TextureCoordinate;
    public VertexPositionColorTexture(Vector3 position,Color32 color,Vector2 textureCoordinate){Position=position;Color=color;TextureCoordinate=textureCoordinate;}
}
public struct VertexPositionNormalColorTexture
{
    public Vector3 Position,Normal;
    public Color32 Color;
    public Vector2 TextureCoordinate;
    public VertexPositionNormalColorTexture(Vector3 position,Vector3 normal,Color32 color,Vector2 textureCoordinate){Position=position;Normal=normal;Color=color;TextureCoordinate=textureCoordinate;}
}
