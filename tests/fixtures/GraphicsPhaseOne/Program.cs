using Dolphin.Graphics;
using System.Threading.Tasks;
internal static class Program
{
    static VertexPositionNormalColorTexture V(float x,float y)=>new VertexPositionNormalColorTexture(new Vector3(x,y,0),new Vector3(0,0,1),Color32.White,new Vector2(1.2f,.2f));
    static int Main()
    {
        var device=GraphicsDevice.Default;var renderer=new Renderer();
        var camera=new Camera();camera.LookAt(new Vector3(0,0,5),Vector3.Zero,new Vector3(0,1,0));camera.SetOrthographic(8,4,.1f,100);
        using var quad=Mesh.Create(new[]{V(-.5f,-.5f),V(.5f,-.5f),V(.5f,.5f),V(-.5f,-.5f),V(.5f,.5f),V(-.5f,.5f)});
        var pixels=new byte[64];for(int y=0;y<4;y++)for(int x=0;x<4;x++){int at=(y*4+x)*4;pixels[at]=(byte)(x<2?220:20);pixels[at+1]=(byte)(x<2?20:220);pixels[at+2]=20;pixels[at+3]=255;}
        using var texture=Texture2D.Create(4,4,pixels);
        var auxiliaryPixels=new byte[64];for(int y=0;y<4;y++)for(int x=0;x<4;x++){int at=(y*4+x)*4;auxiliaryPixels[at]=(byte)(x<2?20:220);auxiliaryPixels[at+1]=(byte)(x<2?20:220);auxiliaryPixels[at+2]=(byte)(x<2?220:20);}
        using var auxiliary=Texture2D.Create(4,4,auxiliaryPixels);
        using var emission=TevMaterialPresets.Emissive(new EmissiveMaterialOptions{SeparateBaseTexture=true,BaseColor=Color32.White,EmissionColor=Color32.White}).Create(texture,auxiliary);
        emission.Material.Cull=CullMode.None;emission.Material.TextureCoordinates=TextureCoordinateMode.ViewNormal;
        emission.Material.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Clamp,TextureFilter.Nearest);
        emission.Material.SecondarySampler=new SamplerState(TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureFilter.Nearest);
        emission.Material.SecondaryTextureTransform=new UvTransform(new Vector2(-.25f,0),new Vector2(1,1));
        var emissionTransform=new Transform{LocalPosition=new Vector3(0,1.25f,0)};
        var red=new Material{Cull=CullMode.None,Color=new Color32(220,40,40)};
        var diffuse=new Material{Cull=CullMode.None,Lighting=LightingMode.Diffuse};
        var repeat=new Material{Cull=CullMode.None,Texture=texture,Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Clamp,TextureFilter.Nearest)};
        var transforms=new[]{new Transform{LocalPosition=new Vector3(-3,0,0)},new Transform{LocalPosition=new Vector3(-1,0,0)},new Transform{LocalPosition=new Vector3(1,0,0)},new Transform{LocalPosition=new Vector3(3,0,0)}};
        var lighting=new RenderSettings{Ambient=new Color32(16,16,16),LightCount=1};lighting.Lights[0]=new SceneLight(LightKind.Directional,new Color32(120,80,40),Vector3.Zero,new Vector3(0,0,-1));
        var fog=new RenderSettings{Fog=new FogSettings(FogMode.Linear,new Color32(0,0,200),0,10)};
        using var draw=new Draw2D(device,new Draw2DOptions{SpriteCapacity=1});using var logical=new Draw2D(device,new Draw2DOptions{Width=100,Height=40});
        var identity=new Camera2D();var camera2D=new Camera2D{Position=new Vector2(10,0),Zoom=2};
        var sprites=new[]{new SpriteDraw(new TextureRegion(texture,new RectF(0,0,4,4)),new RectF(160,360,80,40),Color32.White){Flip=SpriteFlip.Horizontal}};
        var benchmark=new SubmissionBenchmark(quad,texture);
        var phaseTwo=new PhaseTwoAcceptance(device);
        while(true){
            device.BeginFrame(Color32.Black);benchmark.Render(camera);device.SetViewport(0,128,640,320);device.SetScissor(0,128,640,320);
            renderer.Begin3D(camera,lighting);renderer.DrawMesh(quad,red,transforms[0]);renderer.DrawMesh(quad,diffuse,transforms[1]);renderer.DrawMesh(quad,repeat,transforms[2]);renderer.DrawMesh(quad,emission.Material,emissionTransform);renderer.End3D();
            renderer.Begin3D(camera,fog);renderer.DrawMesh(quad,red,transforms[3]);renderer.End3D();
            draw.Begin(camera2D);draw.PushClip(20,360,20,40);draw.FillRectangle(new RectF(15,180,20,20),new Color32(200,40,200));draw.PopClip();draw.End();
            draw.Begin();draw.DrawSprites(sprites,0,1);draw.End();
            logical.Begin(identity,new Viewport(400,360,200,80));logical.FillRectangle(new RectF(0,0,50,40),new Color32(20,40,220));logical.End();
            phaseTwo.Render(device);
            device.EndFrame();Task.Delay(1).GetAwaiter().GetResult();
        }
    }
}
