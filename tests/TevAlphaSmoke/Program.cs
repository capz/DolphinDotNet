using Dolphin.Graphics;

internal static class Program
{
    private static int Main()
    {
        var device=GraphicsDevice.Default;
        using var texture=Texture2D.Create(4,4,new byte[64]);
        using var glints=TevMaterialPresets.DualTextureGlints(Color32.White,Color32.White).Create(texture,texture);
        if(glints.Effect.StageCount!=3||!glints.Effect.RequiresTexture||!glints.Effect.RequiresSecondaryTexture)return 1;
        glints.Effect.SetColor("PrimaryLight",new Color32(17,63,129));
        var builder=new TevProgramBuilder();int slot=builder.AddColor("Alpha",new Color32(17,63,129,231));
        for(int i=0;i<16;i++){
            builder.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.One);
            builder.SetAlphaStage(i,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,
                i==0?TevAlphaInput.Constant:(i&1)==0?TevAlphaInput.Texture:TevAlphaInput.SecondaryTexture,
                colorSlot:slot,component:TevComponent.Alpha);
        }
        builder.SetAlphaCompare(AlphaComparison.LessOrEqual,33,AlphaCompareOperation.Or,AlphaComparison.GreaterOrEqual,235);
        using var sixteen=new TevMaterialDefinition(builder).Create(texture,texture);
        if(sixteen.Effect.StageCount!=16)return 2;
        var compare=new TevProgramBuilder();compare.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Texture);
        compare.SetAlphaCompare(AlphaComparison.Greater,16,AlphaCompareOperation.And,AlphaComparison.Always,0);
        using var cutoff=new TevMaterialDefinition(compare).Create(texture);
        var rgb=new TevProgramBuilder();
        rgb.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.TextureAlpha,TevRegister.Color0,clamp:false);
        rgb.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Texture,TevRegister.Color0);
        rgb.AddStage(TevInput.Zero,TevInput.SecondaryTextureAlpha,TevInput.Color0Alpha,TevInput.Color0,operation:TevOperation.Subtract,bias:TevBias.AddHalf,scale:TevScale.Two,clamp:false);
        rgb.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Color0);
        rgb.SetBlendFactors(TevBlendFactor.SourceAlpha,TevBlendFactor.Color);
        using var rgbMaterial=new TevMaterialDefinition(rgb).Create(texture,texture);
        var indirect=new TevProgramBuilder();indirect.EnableIndirectWarp();
        indirect.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Texture);
        indirect.SetIndirectMatrix(0,0,0,0,.001f,.015f,2,TevIndirectBias.STU);
        var indirectBytes=indirect.BuildPackage();
        if(indirectBytes[indirectBytes.Length-4]!=2||indirectBytes[31]!=7)return 3;
        int matrixTail=indirectBytes.Length-28;
        if(indirectBytes[matrixTail+16]!=0x6f||indirectBytes[matrixTail+17]!=0x12||
           indirectBytes[matrixTail+18]!=0x83||indirectBytes[matrixTail+19]!=0x3a||
           Dolphin.Numerics.BitConverter.SingleToInt32Bits(-0.0f)!=unchecked((int)0x80000000))return 4;
        using var indirectMaterial=new TevMaterialDefinition(indirect).Create(texture,texture);
        var vertices=new[]{V(0,1),V(-1,-1),V(1,-1)};
        using var mesh=Mesh.Create(vertices);
        var renderer=new Renderer();var camera=new Camera();var transform=new Transform();var normal=new Material{Texture=texture};
        device.BeginFrame(Color32.Black);renderer.Begin3D(camera);
        renderer.DrawMesh(mesh,glints.Material,transform);
        renderer.DrawMesh(mesh,sixteen.Material,transform);
        renderer.DrawMesh(mesh,cutoff.Material,transform);
        renderer.DrawMesh(mesh,rgbMaterial.Material,transform);
        renderer.DrawMesh(mesh,indirectMaterial.Material,transform);
        renderer.DrawMesh(mesh,normal,transform);
        renderer.End3D();device.EndFrame();
        return 0;
    }
    private static VertexPositionNormalColorTexture V(float x,float y)=>new VertexPositionNormalColorTexture(new Vector3(x,y,0),new Vector3(0,0,1),Color32.White,new Vector2(0,0));
}
