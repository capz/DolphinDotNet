using Dolphin.Graphics;
using Dolphin.Timing;
internal sealed class SubmissionBenchmark
{
    private const int Count=64;
    private readonly Mesh[] meshes=new Mesh[Count];
    private readonly Transform[] transforms=new Transform[Count];
    private readonly SpriteDraw[] sprites=new SpriteDraw[Count];
    private readonly Material material=new Material{Cull=CullMode.None};
    private readonly Renderer renderer=new Renderer(GraphicsDevice.Default,Count);
    private readonly Draw2D draw=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{SpriteCapacity=Count});
    private readonly long[] elapsed=new long[4];private int frame,samples;
    internal SubmissionBenchmark(Mesh mesh,Texture2D texture)
    {for(int i=0;i<Count;i++){meshes[i]=mesh;transforms[i]=new Transform{LocalPosition=new Vector3(100+i,0,0)};sprites[i]=new SpriteDraw(new TextureRegion(texture,new RectF(0,0,4,4)),new RectF(1000+i*4,0,4,4),Color32.White);}}
    internal void Render(Camera camera)
    {
        if(frame++<300){
            // Reverse test order on alternating frames to expose order bias.
            renderer.Begin3D(camera);
            if((frame&1)==0){Meshes(false);Meshes(true);}else{Meshes(true);Meshes(false);}
            renderer.End3D();draw.Begin();
            if((frame&1)==0){Sprites(false);Sprites(true);}else{Sprites(true);Sprites(false);}
            draw.End();if(frame>60)samples++;
        }
        draw.Begin();
        for(int field=0;field<8;field++){
            int value=field==0?0x4750:field==1?1:field==2?samples:field==3?Count:samples==0?0:(int)(elapsed[field-4]/samples);
            if(value>65535)value=65535;
            for(int bit=0;bit<16;bit++){int index=field*16+bit;byte shade=(byte)((value&(1<<bit))!=0?240:16);draw.FillRectangle(new RectF((index%64)*10,448+(index/64)*4,10,4),new Color32(shade,shade,shade));}
        }draw.End();
    }
    private void Meshes(bool bulk){long at=Clock.Timestamp;if(bulk)renderer.DrawMeshes(meshes,transforms,material,0,Count);else for(int i=0;i<Count;i++)renderer.DrawMesh(meshes[i],material,transforms[i]);if(frame>60)elapsed[bulk?1:0]+=Clock.Timestamp-at;}
    private void Sprites(bool bulk){long at=Clock.Timestamp;if(bulk)draw.DrawSprites(sprites,0,Count);else for(int i=0;i<Count;i++)draw.DrawSprite(sprites[i]);if(frame>60)elapsed[bulk?3:2]+=Clock.Timestamp-at;}
}
