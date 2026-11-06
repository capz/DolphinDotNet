using System;
namespace Dolphin.Graphics;
/// <summary>Shares positions, normals, UVs and indices with a mesh; owns only
/// independent colors. Geometry updates remain visible. The native allocation
/// is retained even when the original managed mesh is disposed.</summary>
public sealed class MeshColorView : Mesh
{
    public MeshColorView(Mesh geometry,Color32[] colors):base(Create(geometry,colors)){}
    private static int Create(Mesh geometry,Color32[] colors)
    {if(geometry==null||colors==null)throw new ArgumentNullException();geometry.Check();return NativeGraphics.MeshColorView(geometry.Token,colors);}
    /// <summary>Replace all colors between frames, without uploading geometry.</summary>
    public void SetColors(Color32[] colors)
    {Check();if(colors==null)throw new ArgumentNullException();GraphicsDevice.Check(NativeGraphics.MeshColorUpdate(Token,colors),"Invalid color stream or active frame.");}
}
