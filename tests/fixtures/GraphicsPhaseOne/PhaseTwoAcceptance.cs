using Dolphin.Graphics;
using Dolphin.Timing;
internal sealed class PhaseTwoAcceptance
{
    private readonly Geometry2D geometry;
    private readonly BitmapFont font;
    private readonly BitmapTextLayout text;
    private readonly SceneCapture capture;
    private readonly Texture2D mip;
    private readonly Draw2D draw;
    private readonly Draw2D downscaled;
    private readonly Billboard billboard=new Billboard();
    private readonly Renderer renderer=new Renderer();
    private readonly Camera camera=new Camera();
    private readonly Material billboardMaterial=new Material{Cull=CullMode.None,Color=new Color32(220,160,20)};
    private readonly Material mipMaterial;
    private readonly Mesh plane;
    private readonly Mesh box=PrimitiveMeshes.Box(3,3,3),sphere=PrimitiveMeshes.Sphere(1.5f,16,8);
    private readonly Material primitiveMaterial=new Material{Cull=CullMode.None,Color=new Color32(40,180,220)};
    private readonly Transform transform=new Transform();
    private readonly GxEffect captureEffect;
    private long individualUs,cachedUs;private int frame,samples;
    internal PhaseTwoAcceptance(GraphicsDevice device)
    {
        draw=new Draw2D(device);downscaled=new Draw2D(device,new Draw2DOptions{Width=1280,Height=960});
        var builder=new Geometry2DBuilder(512);
        builder.AddRoundedRectangle(new RectF(0,0,56,56),12,new Color32(20,200,120),8);
        builder.AddCircle(new Vector2(100,28),24,new Color32(200,40,160),24);
        builder.AddGradient(new RectF(144,4,48,48),new Color32(240,0,0),new Color32(0,0,240),new Color32(0,0,240),new Color32(240,0,0));
        geometry=builder.Build();
        var pixels=new byte[64];for(int i=0;i<16;i++){pixels[i*4]=240;pixels[i*4+1]=240;pixels[i*4+2]=40;pixels[i*4+3]=255;}
        font=new BitmapFont(Texture2D.Create(4,4,pixels),1,1,64,16);
        text=new BitmapTextLayout(font,"AB\nCD",24,TextAlignment.Center,8);
        capture=new SceneCapture(device,32,32);
        var effect=new TevProgramBuilder();
        effect.AddStage(TevInput.One,TevInput.Zero,TevInput.Texture,TevInput.Zero);
        captureEffect=GxEffect.FromPackage(effect.BuildPackage());
        camera.SetOrthographic(8,4,.1f,100);
        // Mip zero red; subsequent levels blue. Strong minification must sample blue.
        var package=new byte[32+256+64+64+64];
        Put(package,0,0x58544e44);Put(package,4,2);Put(package,8,6);Put(package,12,8);Put(package,16,8);Put(package,20,448);Put(package,24,4);
        for(int block=32;block<package.Length;block+=64)for(int i=0;i<16;i++){
            package[block+i*2]=255;package[block+i*2+1]=(byte)(block<288?220:20);
            package[block+32+i*2]=20;package[block+33+i*2]=(byte)(block<288?20:220);
        }
        mip=Texture2D.FromPackage(package,true);
        mipMaterial=new Material{Texture=mip,Cull=CullMode.None,Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear),TextureTransform=new UvTransform(default,new Vector2(128,128))};
        plane=Mesh.Create(new[]{V(-.5f,-.5f,0,1),V(.5f,-.5f,1,1),V(.5f,.5f,1,0),V(-.5f,-.5f,0,1),V(.5f,.5f,1,0),V(-.5f,.5f,0,0)});
    }
    private static VertexPositionNormalColorTexture V(float x,float y,float u,float v)=>new VertexPositionNormalColorTexture(new Vector3(x,y,0),new Vector3(0,0,1),Color32.White,new Vector2(u,v));
    private static void Put(byte[] a,int at,int value){for(int i=0;i<4;i++)a[at+i]=(byte)(value>>(8*i));}
    internal void Render(GraphicsDevice device)
    {
        device.SetViewport(0,0,640,480);device.SetScissor(0,0,640,480);
        draw.Begin();draw.DrawGeometry(geometry,new Vector2(8,8),Color32.White);text.Draw(draw,new Vector2(216,40),Color32.White);draw.End();
        // A 64x64 source copied to a 32x32 target proves half-size GX capture.
        capture.Capture(new Viewport(0,0,64,64));
        draw.Begin();draw.SetEffect(captureEffect);capture.Draw(draw,new RectF(280,8,64,64),Color32.White);draw.End();
        device.SetViewport(352,8,64,64);device.SetScissor(352,8,64,64);
        renderer.Begin3D(camera);renderer.DrawBillboard(billboard,camera,Vector3.Zero,new Vector2(6,3),billboardMaterial);renderer.End3D();
        device.SetViewport(432,8,64,64);device.SetScissor(432,8,64,64);
        renderer.Begin3D(camera);renderer.DrawMesh(plane,mipMaterial,transform);renderer.End3D();
        device.SetViewport(520,8,48,48);device.SetScissor(520,8,48,48);
        renderer.Begin3D(camera);renderer.DrawMesh(box,primitiveMaterial,transform);renderer.End3D();
        device.SetViewport(584,8,48,48);device.SetScissor(584,8,48,48);
        renderer.Begin3D(camera);renderer.DrawMesh(sphere,primitiveMaterial,transform);renderer.End3D();
        downscaled.Begin();downscaled.PushClip(0,0,1,2);downscaled.FillRectangle(new RectF(0,0,1280,960),Color32.White);downscaled.PopClip();downscaled.End();
        // Cached layout versus immediate text, both outside view. Include Flush
        // in immediate window so both paths submit geometry before timing ends.
        if(frame++<300){
            draw.Begin();
            if((frame&1)==0){Text(false);Text(true);}else{Text(true);Text(false);}
            draw.End();if(frame>60)samples++;
        }
        draw.Begin();
        for(int field=0;field<4;field++){
            int value=field==0?0x4752:field==1?samples:samples==0?0:(int)((field==2?individualUs:cachedUs)/samples);
            for(int bit=0;bit<16;bit++){int index=field*16+bit;byte shade=(byte)((value&(1<<bit))!=0?240:16);draw.FillRectangle(new RectF(index*10,464,10,4),new Color32(shade,shade,shade));}
        }draw.End();
    }
    private void Text(bool cached)
    {
        long start=Clock.Timestamp;
        if(cached)text.Draw(draw,new Vector2(1000,0),Color32.White);
        else{draw.DrawText(font,"AB\nCD",new Vector2(1000,0),Color32.White,8);draw.Flush();}
        if(frame>60){if(cached)cachedUs+=Clock.Timestamp-start;else individualUs+=Clock.Timestamp-start;}
    }
}
