using System;
namespace Dolphin.Graphics;

/// <summary>Setup-time indexed meshes with normals and UVs. The caller owns the
/// returned mesh. Dimensions are finite and positive; tessellation is bounded.</summary>
public static class PrimitiveMeshes
{
    private static void Dimension(float value)
    {GraphicsMath.Finite(value);if(value<=0)throw new ArgumentOutOfRangeException("dimension");}
    private static VertexPositionNormalColorTexture Vertex(Vector3 p,Vector3 n,float u,float v)
        =>new VertexPositionNormalColorTexture(p,n,Color32.White,new Vector2(u,v));
    /// <summary>XZ plane centered at the origin, facing +Y.</summary>
    public static Mesh Plane(float width=1,float depth=1)
    {
        Dimension(width);Dimension(depth);float x=width*.5f,z=depth*.5f;var n=new Vector3(0,1,0);
        return Mesh.Create(new[]{Vertex(new Vector3(-x,0,-z),n,0,0),Vertex(new Vector3(-x,0,z),n,0,1),
            Vertex(new Vector3(x,0,z),n,1,1),Vertex(new Vector3(x,0,-z),n,1,0)},new ushort[]{0,1,2,0,2,3});
    }
    public static Mesh Box(float width=1,float height=1,float depth=1)
    {
        Dimension(width);Dimension(height);Dimension(depth);
        var vertices=new VertexPositionNormalColorTexture[24];var indices=new ushort[36];
        var normals=new[]{new Vector3(1,0,0),new Vector3(-1,0,0),new Vector3(0,1,0),new Vector3(0,-1,0),new Vector3(0,0,1),new Vector3(0,0,-1)};
        var rights=new[]{new Vector3(0,0,-1),new Vector3(0,0,1),new Vector3(1,0,0),new Vector3(1,0,0),new Vector3(1,0,0),new Vector3(-1,0,0)};
        for(int face=0;face<6;face++){
            var n=normals[face];var right=rights[face];
            var up=new Vector3(n.Y*right.Z-n.Z*right.Y,n.Z*right.X-n.X*right.Z,n.X*right.Y-n.Y*right.X);
            for(int corner=0;corner<4;corner++){
                float sx=corner==0||corner==3?-.5f:.5f,sy=corner<2?-.5f:.5f;
                var q=n*.5f+right*sx+up*sy;
                vertices[face*4+corner]=Vertex(new Vector3(q.X*width,q.Y*height,q.Z*depth),n,sx+.5f,.5f-sy);
            }
            int v=face*4,at=face*6;indices[at]=(ushort)v;indices[at+1]=(ushort)(v+1);indices[at+2]=(ushort)(v+2);
            indices[at+3]=(ushort)v;indices[at+4]=(ushort)(v+2);indices[at+5]=(ushort)(v+3);
        }
        return Mesh.Create(vertices,indices);
    }
    public static Mesh Sphere(float radius=1,int slices=24,int stacks=12)
    {
        Dimension(radius);if(slices<3||slices>128||stacks<2||stacks>64)throw new ArgumentOutOfRangeException("slices");
        var vertices=new VertexPositionNormalColorTexture[(slices+1)*(stacks+1)];
        var indices=new ushort[slices*(stacks-1)*6];int at=0;
        for(int y=0;y<=stacks;y++)for(int x=0;x<=slices;x++){
            float theta=y*3.14159265359f/stacks,phi=x*6.28318530718f/slices;
            var n=new Vector3((float)(Math.Sin(theta)*Math.Cos(phi)),(float)Math.Cos(theta),(float)(Math.Sin(theta)*Math.Sin(phi)));
            vertices[y*(slices+1)+x]=Vertex(n*radius,n,(float)x/slices,(float)y/stacks);
        }
        for(int y=0;y<stacks;y++)for(int x=0;x<slices;x++){
            int a=y*(slices+1)+x,b=a+slices+1;
            if(y>0){indices[at++]=(ushort)a;indices[at++]=(ushort)(a+1);indices[at++]=(ushort)b;}
            if(y<stacks-1){indices[at++]=(ushort)(a+1);indices[at++]=(ushort)(b+1);indices[at++]=(ushort)b;}
        }
        return Mesh.Create(vertices,indices);
    }
}

/// <summary>Reusable screen-aligned quad. Owns immutable geometry and a reusable
/// world matrix; rendering does not update native vertices or allocate arrays.</summary>
public sealed class Billboard : IDisposable
{
    internal readonly Mesh Mesh;
    internal readonly float[] World=new float[16];
    private bool disposed;
    public Billboard()
    {
        var n=new Vector3(0,0,1);
        Mesh=Mesh.Create(new[]{
            new VertexPositionNormalColorTexture(new Vector3(-.5f,-.5f,0),n,Color32.White,new Vector2(0,1)),
            new VertexPositionNormalColorTexture(new Vector3(.5f,-.5f,0),n,Color32.White,new Vector2(1,1)),
            new VertexPositionNormalColorTexture(new Vector3(.5f,.5f,0),n,Color32.White,new Vector2(1,0)),
            new VertexPositionNormalColorTexture(new Vector3(-.5f,.5f,0),n,Color32.White,new Vector2(0,0))},new ushort[]{0,1,2,0,2,3});
    }
    public int NativeBytes {get{Check();return Mesh.NativeBytes;}}
    internal void Check(){if(disposed)throw new ObjectDisposedException("Billboard");Mesh.Check();}
    public void Dispose(){if(disposed)return;Mesh.Dispose();disposed=true;}
}
public sealed partial class Renderer
{
    /// <summary>Use the camera of this pass. Width/height are world units;
    /// transparent billboards require explicit ordering like other mesh draws.</summary>
    public void DrawBillboard(Billboard billboard,Camera camera,Vector3 center,Vector2 size,Material material)
    {
        if(!active)throw new InvalidOperationException("Begin a 3D pass first.");
        if(billboard==null||camera==null||material==null)throw new ArgumentNullException();
        billboard.Check();material.Check();GraphicsMath.Finite(center.X);GraphicsMath.Finite(center.Y);GraphicsMath.Finite(center.Z);
        GraphicsMath.Finite(size.X);GraphicsMath.Finite(size.Y);if(size.X<=0||size.Y<=0)throw new ArgumentException("Billboard size must be positive.");
        var w=billboard.World;var v=camera.View;
        for(int i=0;i<16;i++)w[i]=0;
        w[0]=v[0]*size.X;w[1]=v[4]*size.X;w[2]=v[8]*size.X;
        w[4]=v[1]*size.Y;w[5]=v[5]*size.Y;w[6]=v[9]*size.Y;
        w[8]=v[2];w[9]=v[6];w[10]=v[10];w[12]=center.X;w[13]=center.Y;w[14]=center.Z;w[15]=1;
        ApplyMaterial(material);
        GraphicsDevice.Check(NativeGraphics.MeshDrawMaterial(billboard.Mesh.Token,material.TextureToken,
            material.SecondaryTextureToken,w,material.Color.Packed,material.Flags,material.AlphaCutoff,material.EffectToken),"Invalid billboard material or pass.");
    }
}
