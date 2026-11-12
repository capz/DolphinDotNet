using Dolphin.Graphics;

// Retained resources: no texture updates or heap allocations in Render.
internal sealed class TevAlphaAcceptance
{
    private readonly Renderer renderer=new Renderer();
    private readonly Camera camera=new Camera();
    private readonly Mesh quad;
    private readonly Texture2D ramp,inverse;
    private readonly TevMaterial same,opposite;
    private readonly Material background=new Material{Color=new Color32(220,40,40),Cull=CullMode.None,DepthTest=false,DepthWrite=false};
    // Alpha 128 must survive the next ordinary draw, proving OR-test restoration.
    private readonly Material normal=new Material{Color=new Color32(20,200,40,128),Cull=CullMode.None,DepthTest=false,DepthWrite=false};
    private readonly Transform full=new Transform{LocalScale=new Vector3(4,1,1)};
    private readonly Transform left=new Transform{LocalPosition=new Vector3(-2,0,0),LocalScale=new Vector3(2,1,1)};
    private readonly Transform right=new Transform{LocalPosition=new Vector3(2,0,0),LocalScale=new Vector3(2,1,1)};
    private readonly Transform marker=new Transform{LocalPosition=new Vector3(3.875f,0,0),LocalScale=new Vector3(.125f,1,1)};
    internal TevAlphaAcceptance()
    {
        camera.SetOrthographic(8,2,.1f,100);
        quad=Mesh.Create(new[]{V(-1,-1,0,1),V(1,-1,1,1),V(1,1,1,0),V(-1,-1,0,1),V(1,1,1,0),V(-1,1,0,0)});
        var pixels=new byte[256*4*4];var other=new byte[pixels.Length];
        for(int y=0;y<4;y++)for(int x=0;x<256;x++){
            int at=(y*256+x)*4;
            pixels[at]=pixels[at+1]=pixels[at+2]=other[at]=other[at+1]=other[at+2]=255;
            pixels[at+3]=(byte)x;other[at+3]=(byte)(255-x);
        }
        ramp=Texture2D.Create(256,4,pixels);inverse=Texture2D.Create(256,4,other);
        var program=new TevProgramBuilder();
        program.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.One);
        program.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Texture,TevRegister.Color0);
        program.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.One);
        program.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.SecondaryTexture,TevAlphaInput.One,TevAlphaInput.Color0,scale:TevScale.Half);
        program.SetAlphaCompare(AlphaComparison.LessOrEqual,33,AlphaCompareOperation.Or,AlphaComparison.GreaterOrEqual,235);
        var definition=new TevMaterialDefinition(program,LightingMode.Unlit);
        same=definition.Create(ramp,ramp);opposite=definition.Create(ramp,inverse);
        Configure(same.Material);Configure(opposite.Material);
    }
    private static VertexPositionNormalColorTexture V(float x,float y,float u,float v)=>new VertexPositionNormalColorTexture(new Vector3(x,y,0),new Vector3(0,0,1),Color32.White,new Vector2(u,v));
    private static void Configure(Material material)
    {
        material.Cull=CullMode.None;material.DepthTest=false;material.DepthWrite=false;
        material.Sampler=new SamplerState(TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureFilter.Nearest);
        material.SecondarySampler=material.Sampler;
    }
    internal void Render(GraphicsDevice device)
    {
        device.SetViewport(0,80,640,32);device.SetScissor(0,80,640,32);
        renderer.Begin3D(camera);
        renderer.DrawMesh(quad,background,full);
        renderer.DrawMesh(quad,same.Material,left);
        renderer.DrawMesh(quad,opposite.Material,right);
        renderer.DrawMesh(quad,normal,marker);
        renderer.End3D();
    }
}
