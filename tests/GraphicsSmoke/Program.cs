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
        var textureProgram=new byte[184];textureProgram[0]=68;textureProgram[1]=78;textureProgram[2]=69;textureProgram[3]=70;
        textureProgram[4]=1;textureProgram[8]=1;textureProgram[12]=1;
        textureProgram[176]=15;textureProgram[177]=15;textureProgram[178]=15;textureProgram[179]=8;
        using var textureEffect=GxEffect.FromPackage(textureProgram);
        textureProgram[179]=0; // Native program must retain its own validated copy.
        using var effect=GxEffect.ColorRamp(Color32.Black,Color32.White);
        if(effect.StageCount!=2||effect.GetUniformName(effect.GetUniformSlot("Bright"))!="Bright")return 9;
        effect.SetColor("Bright",new Color32(20,100,200));
        // Native CMPR payloads and streaming-resource replacement.
        using var compressed=Texture2D.CreateNative(8,8,NativeTextureFormat.Cmpr);
        if(compressed.NativeBytes!=32)return 30;
        compressed.ReplaceNative(new byte[32]);
        try{compressed.ReplaceNative(new byte[31]);return 31;}catch(ArgumentOutOfRangeException){}
        using var streamed=new StreamingTexture(8,8);
        if(streamed.IsResident)return 32;
        streamed.UploadNative(new byte[32]);
        if(!streamed.IsResident||streamed.ContentGeneration!=1)return 33;
        var renderer=new Renderer();var camera=new Camera();var transform=new Transform();var material=new Material();
        using var toonPreset=TevMaterialPresets.Toon().Create(loaded);
        using var metalPreset=TevMaterialPresets.MetallicReflection().Create(loaded);
        using var emissionPreset=TevMaterialPresets.Emissive().Create(loaded);
        using var dualMetal=TevMaterialPresets.MetallicReflection(new MetallicMaterialOptions{SeparateBaseTexture=true,ReflectionCoordinates=TextureCoordinateMode.WorldReflection}).Create(loaded,texture);
        using var dualGlow=TevMaterialPresets.Emissive(new EmissiveMaterialOptions{SeparateBaseTexture=true}).Create(loaded,texture);
        metalPreset.Effect.SetColor("ReflectionStrength",new Color32(128,128,128));
        emissionPreset.Effect.SetColor("EmissionColor",new Color32(255,20,100));
        // Texture strategy queue: same material grouped into one logical batch.
        var expiredTexture=Texture2D.CreateNative(8,8,NativeTextureFormat.Cmpr);
        var renderQueue=new RenderQueue(4);
        device.BeginFrame(Color32.Black);
        renderer.Begin3D(camera);
        renderQueue.Submit(mesh,material,transform);
        renderQueue.Submit(mesh,material,transform);
        var queuedStats=renderQueue.Execute(renderer);
        if(queuedStats.Draws!=2||queuedStats.Groups!=1)return 34;
        renderer.DrawMesh(normalMesh,toonPreset.Material,transform);
        renderer.DrawMesh(normalMesh,metalPreset.Material,transform);
        renderer.DrawMesh(normalMesh,emissionPreset.Material,transform);
        renderer.DrawMesh(normalMesh,dualMetal.Material,transform);renderer.DrawMesh(normalMesh,dualGlow.Material,transform);
        try{renderer.DrawMesh(texturedMesh,metalPreset.Material,transform);return 13;}catch(InvalidOperationException){}
        material.Effect=textureEffect;
        try{renderer.DrawMesh(mesh,material,transform);return 12;}catch(ArgumentException){}
        material.Texture=loaded;renderer.DrawMesh(mesh,material,transform);material.Texture=null;material.Effect=effect;renderer.DrawMesh(mesh,material,transform);material.Effect=null;material.Texture=texture;renderer.DrawMesh(texturedMesh,material,transform);material.Lighting=LightingMode.Diffuse;renderer.DrawMesh(normalMesh,material,transform);renderer.End3D();
        device.BeginRaw();GX.GX_SetCopyClear(new GXColor(0x11,0x22,0x33,0x44),0xffffff);GX.GX_SetTevColorS10(0,new GXColorS10(-1,2,-3,4));GU.guMtxIdentity(buffer.Address);GX.GX_Begin((byte)GXConstants.GX_TRIANGLES,(byte)GXConstants.GX_VTXFMT0,3);
        GX.GX_Position3f32(0,1,0);GX.GX_Position3f32(-1,-1,0);GX.GX_Position3f32(1,-1,0);GX.GX_End();device.EndRaw();
        batch.Begin();batch.FillRectangle(new RectF(0,0,10,10),Color32.White);batch.FillRectangle(new RectF(10,0,10,10),Color32.White);
        batch.DrawTexture(texture,new RectF(0,0,4,4),Color32.White);texture.Dispose();batch.End();
        if(batch.DrawCalls!=2)return 1;
        var finishFence=device.InsertFence();
        if(finishFence.IsCompleted)return 35;
        device.RetireAfter(finishFence,expiredTexture);
        device.EndFrame();
        if(!finishFence.IsCompleted)return 36;
        try{expiredTexture.ReplaceNative(new byte[32]);return 37;}catch(ObjectDisposedException){}
        var renderStats=Dolphin.Diagnostics.GpuProfiler.Snapshot();
        if(renderStats.DrawSubmissions<0||renderStats.TextureBindings<0)return 38;
        using var diskTarget=NativeBuffer.Allocate(64);
        var testPath="sd:/graphics_stream_test.bin";
        var testBytes=new byte[96];
        for(int i=0;i<testBytes.Length;i++)testBytes[i]=(byte)(i*13);
        Dolphin.IO.File.WriteAllBytes(testPath,testBytes);
        using(var asset=new Dolphin.Storage.AssetStream(testPath))
        {
            if(await asset.ReadAtAsync(16,diskTarget,0,64)!=64)return 39;
            var verify=new byte[64];diskTarget.Read(verify);
            for(int i=0;i<verify.Length;i++)if(verify[i]!=testBytes[i+16])return 40;
        }
        Dolphin.IO.File.Delete(testPath);
        // Exercise the direct MEM1 staging -> GX-native texture path.
        compressed.ReplaceNative(diskTarget,0);
        if(Dolphin.Memory.Aram.Initialize())
        {
            using var transfer=Dolphin.Memory.Aram.Allocate(64);
            diskTarget.Write(testBytes,0,0,64);
            await AramTransferQueue.CopyToAramAsync(diskTarget,0,transfer,0,64);
            diskTarget.Write(new byte[64]);
            await AramTransferQueue.CopyToMainAsync(transfer,0,diskTarget,0,64);
            var verify=new byte[64];diskTarget.Read(verify);
            for(int i=0;i<verify.Length;i++)if(verify[i]!=testBytes[i])return 41;
        }
        using var queued=GxEffect.ToonBands(65,165,Color32.Black,new Color32(50,100,150),Color32.White);
        device.BeginFrame(Color32.Black);batch.Begin();batch.SetEffect(queued);
        batch.DrawTexture(loaded,new RectF(0,0,4,4),Color32.White);
        try{queued.SetColor("Dark",Color32.White);return 10;}catch(InvalidOperationException){}
        queued.Dispose(); // Queued vertices retain native state until frame end.
        batch.SetEffect(null);batch.FillRectangle(new RectF(0,0,2,2),Color32.White);batch.End();device.EndFrame();
        try{queued.SetColor(0,Color32.White);return 11;}catch(ObjectDisposedException){}
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
