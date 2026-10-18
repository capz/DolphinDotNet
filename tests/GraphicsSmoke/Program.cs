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
        using var pngTexture=Texture2D.FromPng(new byte[]{137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,0,1,0,0,0,1,16,6,0,0,0,79,133,24,202,0,0,0,17,73,68,65,84,120,156,99,16,50,9,171,152,181,231,222,7,0,13,169,4,57,83,0,201,186,0,0,0,0,73,69,78,68,174,66,96,130},true);
        if(pngTexture.Width!=1||pngTexture.Height!=1||pngTexture.NativeBytes!=64)return 6;
        try{Texture2D.FromPng(new byte[]{137,80,78,71});return 7;}catch(ArgumentException){}
        try{Texture2D.FromPng(null!);return 8;}catch(ArgumentNullException){}
        using var texture=Texture2D.Create(4,4,new byte[64]);
        using var batch=new Draw2D(device,new Draw2DOptions{VertexCapacity=12});
        using var buffer=NativeBuffer.Allocate(64);
        buffer.Write(new byte[]{1,2,3,4});buffer.Flush();
        using var texturedMesh=Mesh.Create(new[]{new VertexPositionColorTexture(new Vector3(0,1,0),Color32.White,new Vector2(0,0)),new VertexPositionColorTexture(new Vector3(-1,-1,0),Color32.White,new Vector2(0,1)),new VertexPositionColorTexture(new Vector3(1,-1,0),Color32.White,new Vector2(1,1))});
        using var normalMesh=Mesh.Create(new[]{new VertexPositionNormalColorTexture(new Vector3(0,1,0),new Vector3(0,0,1),Color32.White,new Vector2(0,0)),new VertexPositionNormalColorTexture(new Vector3(-1,-1,0),new Vector3(0,0,1),Color32.White,new Vector2(0,1)),new VertexPositionNormalColorTexture(new Vector3(1,-1,0),new Vector3(0,0,1),Color32.White,new Vector2(1,1))});
        var renderer=new Renderer();var camera=new Camera();var transform=new Transform();var material=new Material();
        device.BeginFrame(Color32.Black);
        renderer.Begin3D(camera);renderer.DrawMesh(mesh,material,transform);material.Texture=texture;renderer.DrawMesh(texturedMesh,material,transform);material.Lighting=LightingMode.Diffuse;renderer.DrawMesh(normalMesh,material,transform);renderer.End3D();
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
