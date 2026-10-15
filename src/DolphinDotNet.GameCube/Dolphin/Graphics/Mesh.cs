using System;
namespace Dolphin.Graphics;

public class Mesh : IDisposable
{
    internal readonly int Token;
    private bool disposed;
    protected Mesh(int token){if(token==0)throw new ArgumentException("Invalid geometry, indices, memory budget or active frame.");Token=token;}
    public int NativeBytes {get{Check();return NativeGraphics.DrawBytes(Token);}}
    public static Mesh Create(VertexPositionColor[] vertices,ushort[]? indices=null) => new Mesh(NativeGraphics.MeshCreateColor(vertices,indices,0,0));
    public static Mesh Create(VertexPositionColorTexture[] vertices,ushort[]? indices=null) => new Mesh(NativeGraphics.MeshCreateTexture(vertices,indices,1,0));
    public static Mesh Create(VertexPositionNormalColorTexture[] vertices,ushort[]? indices=null) => new Mesh(NativeGraphics.MeshCreateNormal(vertices,indices,2,0));
    internal void Check(){if(disposed)throw new ObjectDisposedException("Mesh");}
    public void Dispose(){if(disposed)return;GraphicsDevice.Check(NativeGraphics.DrawRelease(Token),"Cannot dispose mesh during a raw stream.");disposed=true;}
}
public sealed class DynamicMesh : Mesh
{
    private DynamicMesh(int token) : base(token) { }
    public static new DynamicMesh Create(VertexPositionColor[] vertices,ushort[]? indices=null) => new DynamicMesh(NativeGraphics.MeshCreateColor(vertices,indices,0,1));
    public static new DynamicMesh Create(VertexPositionColorTexture[] vertices,ushort[]? indices=null) => new DynamicMesh(NativeGraphics.MeshCreateTexture(vertices,indices,1,1));
    public static new DynamicMesh Create(VertexPositionNormalColorTexture[] vertices,ushort[]? indices=null) => new DynamicMesh(NativeGraphics.MeshCreateNormal(vertices,indices,2,1));
    public void SetVertices(VertexPositionColor[] vertices,int destinationOffset=0,int count=-1)
    {Check();if(vertices==null)throw new ArgumentNullException("vertices");if(count<0)count=vertices.Length;GraphicsDevice.Check(NativeGraphics.MeshUpdateColor(Token,vertices,destinationOffset,count,0),"Invalid dynamic geometry update or active frame.");}
    public void SetVertices(VertexPositionColorTexture[] vertices,int destinationOffset=0,int count=-1)
    {Check();if(vertices==null)throw new ArgumentNullException("vertices");if(count<0)count=vertices.Length;GraphicsDevice.Check(NativeGraphics.MeshUpdateTexture(Token,vertices,destinationOffset,count,1),"Invalid dynamic geometry update or active frame.");}
    public void SetVertices(VertexPositionNormalColorTexture[] vertices,int destinationOffset=0,int count=-1)
    {Check();if(vertices==null)throw new ArgumentNullException("vertices");if(count<0)count=vertices.Length;GraphicsDevice.Check(NativeGraphics.MeshUpdateNormal(Token,vertices,destinationOffset,count,2),"Invalid dynamic geometry update or active frame.");}
}
