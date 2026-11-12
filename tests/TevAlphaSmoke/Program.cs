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
        var vertices=new[]{V(0,1),V(-1,-1),V(1,-1)};
        using var mesh=Mesh.Create(vertices);
        var renderer=new Renderer();var camera=new Camera();var transform=new Transform();var normal=new Material{Texture=texture};
        device.BeginFrame(Color32.Black);renderer.Begin3D(camera);
        renderer.DrawMesh(mesh,glints.Material,transform);
        renderer.DrawMesh(mesh,sixteen.Material,transform);
        renderer.DrawMesh(mesh,cutoff.Material,transform);
        renderer.DrawMesh(mesh,normal,transform);
        renderer.End3D();device.EndFrame();
        return 0;
    }
    private static VertexPositionNormalColorTexture V(float x,float y)=>new VertexPositionNormalColorTexture(new Vector3(x,y,0),new Vector3(0,0,1),Color32.White,new Vector2(0,0));
}
