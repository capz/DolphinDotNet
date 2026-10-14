using System;
using Dolphin.Graphics;
using Dolphin.Graphics.Raw;
internal static class Program
{
    static int Main()
    {
        var device=GraphicsDevice.Default;
        var vertices=new[]{new VertexPositionColor(new Vector3(0,1,0),Color32.White),new VertexPositionColor(new Vector3(-1,-1,0),Color32.White),new VertexPositionColor(new Vector3(1,-1,0),Color32.White)};
        using var mesh=DynamicMesh.Create(vertices);
        mesh.SetVertices(vertices);
        using var texture=Texture2D.Create(4,4,new byte[64]);
        using var batch=new Draw2D(device,new Draw2DOptions{VertexCapacity=12});
        using var buffer=NativeBuffer.Allocate(64);
        buffer.Write(new byte[]{1,2,3,4});buffer.Flush();
        var renderer=new Renderer();var camera=new Camera();var transform=new Transform();var material=new Material();
        device.BeginFrame(Color32.Black);
        renderer.Begin3D(camera);renderer.DrawMesh(mesh,material,transform);renderer.End3D();
        device.BeginRaw();GX.GX_Begin((byte)GXConstants.GX_TRIANGLES,(byte)GXConstants.GX_VTXFMT0,3);
        GX.GX_Position3f32(0,1,0);GX.GX_Position3f32(-1,-1,0);GX.GX_Position3f32(1,-1,0);GX.GX_End();device.EndRaw();
        batch.Begin();batch.FillRectangle(new RectF(0,0,10,10),Color32.White);batch.FillRectangle(new RectF(10,0,10,10),Color32.White);
        batch.DrawTexture(texture,new RectF(0,0,4,4),Color32.White);texture.Dispose();batch.End();
        if(batch.DrawCalls!=2)return 1;
        device.EndFrame();
        try{renderer.End3D();return 2;}catch(InvalidOperationException){}
        return 0;
    }
}
