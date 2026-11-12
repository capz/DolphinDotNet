using Dolphin.Graphics;

// Half-resolution RGB565 EFB copy followed by projective sampling of the same
// scene. Deliberately unrelated UVs prove the coordinates come from positions.
internal sealed class ScreenCaptureAcceptance
{
    private readonly SceneCapture capture;
    private readonly Draw2D draw;
    private readonly Renderer renderer=new Renderer();
    private readonly Camera camera=new Camera();
    private readonly Mesh quad;
    private readonly Material material;
    private readonly Transform first=new Transform{LocalPosition=new Vector3(-3.55f,2.55f,0)};
    private readonly Transform second=new Transform{LocalPosition=new Vector3(-2.55f,2.55f,0)};
    internal ScreenCaptureAcceptance(GraphicsDevice device)
    {
        capture=new SceneCapture(device,320,240,NativeTextureFormat.Rgb565);
        draw=new Draw2D(device);camera.SetOrthographic(8,6,.1f,100);
        material=new Material{Texture=capture.Texture,TextureCoordinates=TextureCoordinateMode.ScreenProjection,
            DepthTest=false,DepthWrite=false,Cull=CullMode.None,
            Sampler=new SamplerState(TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureFilter.Nearest)};
        quad=Mesh.Create(new[]{V(-.2f,-.2f),V(.2f,-.2f),V(.2f,.2f),V(-.2f,-.2f),V(.2f,.2f),V(-.2f,.2f)});
    }
    private static VertexPositionColorTexture V(float x,float y)=>new VertexPositionColorTexture(
        new Vector3(x,y,0),Color32.White,new Vector2(1,1));
    internal void Render(GraphicsDevice device)
    {
        device.SetViewport(0,0,640,480);device.SetScissor(0,0,640,480);
        capture.Capture(new Viewport(0,0,640,480));
        draw.Begin();
        draw.FillRectangle(new RectF(20,20,32,32),Color32.Black);
        draw.FillRectangle(new RectF(100,20,32,32),Color32.Black);
        draw.End();
        renderer.Begin3D(camera);
        renderer.DrawMesh(quad,material,first);
        // Same cached material, different world matrix. A stale capture matrix
        // would restore the first green patch instead of this purple circle.
        renderer.DrawMesh(quad,material,second);
        renderer.End3D();
    }
}
