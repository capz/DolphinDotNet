using System;
namespace Dolphin.Graphics;

/// <summary>Immutable native triangles. Owns its mesh; borrows one optional atlas.
/// Replay uses the active batch's camera, clip, sampler, blend and effect.</summary>
public sealed class Geometry2D : IDisposable
{
    internal readonly Mesh Mesh;
    internal readonly Texture2D? Texture;
    private bool disposed;
    internal Geometry2D(Mesh mesh,Texture2D? texture) { Mesh=mesh;Texture=texture; }
    public int NativeBytes {get{Check();return Mesh.NativeBytes;}}
    internal void Check()
    {
        if(disposed)throw new ObjectDisposedException("Geometry2D");
        Mesh.Check();if(Texture!=null)Texture.Check();
    }
    public void Dispose(){if(disposed)return;Mesh.Dispose();disposed=true;}
}

/// <summary>Setup-time bounded tessellation. A builder targets one borrowed atlas;
/// use a separate untextured builder for solid geometry. Build copies its data.</summary>
public sealed class Geometry2DBuilder
{
    private readonly VertexPositionColorTexture[] vertices;
    private readonly Texture2D? texture;
    public int VertexCount {get;private set;}
    public Geometry2DBuilder(int vertexCapacity=4092,Texture2D? texture=null)
    {
        if(vertexCapacity<3||vertexCapacity>65532)throw new ArgumentOutOfRangeException("vertexCapacity");
        if(texture!=null)texture.Check();this.texture=texture;
        vertices=new VertexPositionColorTexture[vertexCapacity];
    }
    public void Clear()=>VertexCount=0;
    private void Reserve(int count)
    {if(count>vertices.Length-VertexCount)throw new InvalidOperationException("Geometry capacity exceeded.");}
    private static void Point(Vector2 p){GraphicsMath.Finite(p.X);GraphicsMath.Finite(p.Y);}
    private void Vertex(Vector2 p,Color32 color,Vector2 uv)
    {vertices[VertexCount++]=new VertexPositionColorTexture(new Vector3(p.X,p.Y,0),color,uv);}
    public void AddTriangle(Vector2 a,Vector2 b,Vector2 c,Color32 color)
    {
        Point(a);Point(b);Point(c);Reserve(3);
        Vertex(a,color,default);Vertex(b,color,default);Vertex(c,color,default);
    }
    private static void Rectangle(RectF r)
    {
        Point(new Vector2(r.X,r.Y));Point(new Vector2(r.Width,r.Height));
        Point(new Vector2(r.X+r.Width,r.Y+r.Height));
        if(r.Width<0||r.Height<0)throw new ArgumentException("Negative rectangle size.");
    }
    public void AddRectangle(RectF destination,Color32 color)
        =>AddGradient(destination,color,color,color,color);
    /// <summary>Corner colors in clockwise order, starting at top left.</summary>
    public void AddGradient(RectF r,Color32 topLeft,Color32 topRight,Color32 bottomRight,Color32 bottomLeft)
    {
        Rectangle(r);Reserve(6);
        var a=new Vector2(r.X,r.Y);var b=new Vector2(r.X+r.Width,r.Y);
        var c=new Vector2(r.X+r.Width,r.Y+r.Height);var d=new Vector2(r.X,r.Y+r.Height);
        Vertex(a,topLeft,default);Vertex(b,topRight,default);Vertex(c,bottomRight,default);
        Vertex(a,topLeft,default);Vertex(c,bottomRight,default);Vertex(d,bottomLeft,default);
    }
    public void AddSprite(TextureRegion region,RectF r,Color32 color,SpriteFlip flip=SpriteFlip.None)
    {
        if(texture==null||region.Texture!=texture)throw new ArgumentException("Region must use the builder atlas.");
        texture.Check();Rectangle(r);if((int)flip<0||(int)flip>3)throw new ArgumentException("Invalid flip.");Reserve(6);
        var source=region.Source;
        float l=source.X/texture.Width,t=source.Y/texture.Height;
        float right=(source.X+source.Width)/texture.Width,bottom=(source.Y+source.Height)/texture.Height;
        if((flip&SpriteFlip.Horizontal)!=0){float v=l;l=right;right=v;}
        if((flip&SpriteFlip.Vertical)!=0){float v=t;t=bottom;bottom=v;}
        var a=new Vector2(r.X,r.Y);var b=new Vector2(r.X+r.Width,r.Y);
        var c=new Vector2(r.X+r.Width,r.Y+r.Height);var d=new Vector2(r.X,r.Y+r.Height);
        Vertex(a,color,new Vector2(l,t));Vertex(b,color,new Vector2(right,t));Vertex(c,color,new Vector2(right,bottom));
        Vertex(a,color,new Vector2(l,t));Vertex(c,color,new Vector2(right,bottom));Vertex(d,color,new Vector2(l,bottom));
    }
    public void AddCircle(Vector2 center,float radius,Color32 color,int segments=32)
    {
        Point(center);GraphicsMath.Finite(radius);
        if(radius<0||segments<3||segments>256)throw new ArgumentOutOfRangeException("segments");
        Point(new Vector2(center.X+radius,center.Y+radius));Point(new Vector2(center.X-radius,center.Y-radius));
        Reserve(segments*3);
        for(int i=0;i<segments;i++){
            float a=i*6.28318530718f/segments,b=(i+1)*6.28318530718f/segments;
            AddTriangle(center,new Vector2(center.X+(float)Math.Cos(a)*radius,center.Y+(float)Math.Sin(a)*radius),
                new Vector2(center.X+(float)Math.Cos(b)*radius,center.Y+(float)Math.Sin(b)*radius),color);
        }
    }
    public void AddRoundedRectangle(RectF r,float radius,Color32 color,int cornerSegments=8)
    {
        Rectangle(r);GraphicsMath.Finite(radius);
        if(radius<0||radius>Math.Min(r.Width,r.Height)*.5f||cornerSegments<1||cornerSegments>64)
            throw new ArgumentOutOfRangeException("radius");
        Reserve((cornerSegments+1)*12);
        var center=new Vector2(r.X+r.Width*.5f,r.Y+r.Height*.5f);
        Vector2 first=default,previous=default;
        for(int corner=0;corner<4;corner++){
            float cx=corner==0||corner==3?r.X+radius:r.X+r.Width-radius;
            float cy=corner<2?r.Y+radius:r.Y+r.Height-radius;
            for(int j=0;j<=cornerSegments;j++){
                float a=(-180+corner*90+j*90f/cornerSegments)*.01745329252f;
                var next=new Vector2(cx+(float)Math.Cos(a)*radius,cy+(float)Math.Sin(a)*radius);
                if(corner==0&&j==0)first=next;else AddTriangle(center,previous,next,color);
                previous=next;
            }
        }
        AddTriangle(center,previous,first,color);
    }
    public Geometry2D Build()
    {
        if(VertexCount==0)throw new InvalidOperationException("Geometry is empty.");
        if(texture!=null)texture.Check();
        var used=new VertexPositionColorTexture[VertexCount];Array.Copy(vertices,used,VertexCount);
        return new Geometry2D(Mesh.Create(used),texture);
    }
}

public sealed partial class Draw2D
{
    private readonly float[] geometryWorld=new float[16];
    /// <summary>One synchronous native draw, without frame-time tessellation or
    /// geometry copies. Offset is in world coordinates before the batch camera.</summary>
    public void DrawGeometry(Geometry2D geometry,Vector2 offset,Color32 tint)
    {
        Require();if(geometry==null)throw new ArgumentNullException("geometry");geometry.Check();
        GraphicsMath.Finite(offset.X);GraphicsMath.Finite(offset.Y);if(effect!=null)effect.Check();
        for(int i=0;i<16;i++)geometryWorld[i]=0;
        geometryWorld[10]=geometryWorld[15]=1;
        if(cameraActive){
            geometryWorld[0]=cameraMatrix[0];geometryWorld[1]=cameraMatrix[1];
            geometryWorld[4]=cameraMatrix[2];geometryWorld[5]=cameraMatrix[3];
            geometryWorld[12]=cameraMatrix[0]*offset.X+cameraMatrix[2]*offset.Y+cameraMatrix[4];
            geometryWorld[13]=cameraMatrix[1]*offset.X+cameraMatrix[3]*offset.Y+cameraMatrix[5];
        }else{geometryWorld[0]=geometryWorld[5]=1;geometryWorld[12]=offset.X;geometryWorld[13]=offset.Y;}
        GraphicsDevice.Check(NativeGraphics.CanvasGeometry(token,geometry.Mesh.Token,
            geometry.Texture==null?0:geometry.Texture.Token,geometryWorld,tint.Packed),"Invalid retained geometry, texture or effect.");
    }
    public void DrawGeometry(Geometry2D geometry)=>DrawGeometry(geometry,default,Color32.White);
}
