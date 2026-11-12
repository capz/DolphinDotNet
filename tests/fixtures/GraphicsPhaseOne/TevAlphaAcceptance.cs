using Dolphin.Graphics;

// Retained resources: no texture updates or heap allocations in Render.
internal sealed class TevAlphaAcceptance
{
    private readonly Renderer renderer=new Renderer();
    private readonly Camera camera=new Camera();
    private readonly Mesh quad;
    private readonly Texture2D ramp,inverse;
    private readonly TevMaterial same,opposite;
    private readonly Texture2D[] seaSamples=new Texture2D[5];
    private readonly TevMaterial[] seaCases=new TevMaterial[5];
    private readonly Transform[] seaPoses=new Transform[5];
    private readonly Material seaBackground=new Material{Color=new Color32(20,30,40),Cull=CullMode.None,DepthTest=false,DepthWrite=false};
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
        var alphas=new byte[]{0,128,255,35,34};
        for(int i=0;i<alphas.Length;i++){
            var rgba=new byte[64];for(int n=0;n<16;n++){
                rgba[n*4]=rgba[n*4+1]=rgba[n*4+2]=255;rgba[n*4+3]=alphas[i];
            }
            seaSamples[i]=Texture2D.Create(4,4,rgba);
        }
        var sea=new TevProgramBuilder();
        sea.AddColor("Initial",new Color32(252,255,255));sea.AddColor("Multiplier",new Color32(232,228,252));
        sea.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Constant,colorSlot:0);
        sea.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Texture,TevAlphaInput.Raster,TevAlphaInput.Zero);
        sea.AddStage(TevInput.Zero,TevInput.Constant,TevInput.Previous,TevInput.Zero,colorSlot:1,scale:TevScale.Two,clamp:false);
        sea.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.SecondaryTexture,TevAlphaInput.Previous,TevAlphaInput.Zero,scale:TevScale.Two,clamp:false);
        sea.SetAlphaCompare(AlphaComparison.GreaterOrEqual,235,AlphaCompareOperation.Xnor,AlphaComparison.GreaterOrEqual,35);
        sea.SetBlendFactors(TevBlendFactor.SourceAlpha,TevBlendFactor.One);
        var seaDefinition=new TevMaterialDefinition(sea,LightingMode.Unlit);
        var first=new[]{2,0,2,1,1};var second=new[]{2,1,1,3,4};
        for(int i=0;i<seaCases.Length;i++){
            seaCases[i]=seaDefinition.Create(seaSamples[first[i]],seaSamples[second[i]]);
            Configure(seaCases[i].Material);seaCases[i].Material.Blend=BlendMode.Alpha;
            seaPoses[i]=new Transform{LocalPosition=new Vector3(-3.2f+i*1.6f,0,0),LocalScale=new Vector3(.8f,1,1)};
        }
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
        // V5 integer overflow and exact XNOR/additive blending. Each column
        // uses uniform independent texture alphas, prepared outside frames.
        device.SetViewport(0,112,640,16);device.SetScissor(0,112,640,16);
        renderer.Begin3D(camera);renderer.DrawMesh(quad,seaBackground,full);
        for(int i=0;i<seaCases.Length;i++)renderer.DrawMesh(quad,seaCases[i].Material,seaPoses[i]);
        renderer.DrawMesh(quad,normal,marker);renderer.End3D();
    }
}
