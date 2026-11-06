using System;
namespace Dolphin.Graphics;
/// <summary>Prepared ordered color/UV variants of one single-texture, unlit,
/// uniform-color mesh. The mesh is borrowed; configure between frames.</summary>
public sealed class MeshPasses
{
    internal readonly Mesh Geometry;
    internal readonly Color32[] Colors;
    internal readonly float[] Uv;
    public int Count {get;}
    public MeshPasses(Mesh geometry,int count)
    {
        if(geometry==null)throw new ArgumentNullException("geometry");
        if(count<1||count>256)throw new ArgumentOutOfRangeException("count");
        geometry.Check();GraphicsDevice.Check(NativeGraphics.MeshPreparePasses(geometry.Token),"Prepare passes between frames for ordinary mesh geometry (up to 65535 indices).");
        Geometry=geometry;Count=count;Colors=new Color32[count];Uv=new float[count*6];
        for(int i=0;i<count;i++){Colors[i]=Color32.White;Uv[i*6]=Uv[i*6+3]=1;}
    }
    /// <summary>Pass color replaces Material.Color; transform replaces primary UV0.</summary>
    public void Set(int index,Color32 color,UvTransform transform)
    {
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Configure passes between frames.");
        if(index<0||index>=Count)throw new ArgumentOutOfRangeException("index");
        Colors[index]=color;transform.Write(Uv,index*6);
    }
}
