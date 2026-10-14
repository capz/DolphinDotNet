using System;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Graphics.Raw;
internal static class Program
{
    static async Task<int> Main()
    {
        if(!Dolphin.Storage.Storage.Mount(Dolphin.Storage.StorageDevice.SdSerialPort2))return 5;
        using var loaded=await Texture2D.LoadAsync("sd:/graphics.dndtex");
        using var loadedFont=await BitmapFont.LoadAsync("sd:/graphics.dndfont");
        if(loaded.Width!=4||loaded.Height!=4||loadedFont.MeasureText("!").X!=1)return 4;
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
        device.BeginRaw();GX.GX_SetCopyClear(new GXColor(0x11,0x22,0x33,0x44),0xffffff);GX.GX_SetTevColorS10(0,new GXColorS10(-1,2,-3,4));GU.guMtxIdentity(buffer.Address);GX.GX_Begin((byte)GXConstants.GX_TRIANGLES,(byte)GXConstants.GX_VTXFMT0,3);
        GX.GX_Position3f32(0,1,0);GX.GX_Position3f32(-1,-1,0);GX.GX_Position3f32(1,-1,0);GX.GX_End();device.EndRaw();
        batch.Begin();batch.FillRectangle(new RectF(0,0,10,10),Color32.White);batch.FillRectangle(new RectF(10,0,10,10),Color32.White);
        batch.DrawTexture(texture,new RectF(0,0,4,4),Color32.White);texture.Dispose();batch.End();
        if(batch.DrawCalls!=2)return 1;
        device.EndFrame();
        using var atlas=Texture2D.Create(4,4,new byte[64]);
        using var font=new BitmapFont(atlas,1,1,32,16);
        var measured=font.MeasureText(" !\n!",2);if(measured.X!=4||measured.Y!=4)return 3;
        device.BeginFrame(Color32.Black);batch.Begin();
        batch.SetClip(0,0,100,100);batch.DrawText(font," !",new Vector2(2,2),Color32.White);
        batch.DrawNineSlice(atlas,new RectF(0,0,4,4),new RectF(0,0,20,20),new Border(1,1,1,1),Color32.White);
        batch.DrawLine(new Vector2(0,0),new Vector2(10,10),Color32.White,2);
        batch.DrawRectangle(new RectF(1,1,10,10),Color32.White);
        batch.FillTriangle(new Vector2(0,0),new Vector2(0,1),new Vector2(1,0),Color32.White);
        batch.SetBlend(BlendMode.Additive);batch.ResetClip();batch.End();device.EndFrame();
        try{renderer.End3D();return 2;}catch(InvalidOperationException){}
        return 0;
    }
}
