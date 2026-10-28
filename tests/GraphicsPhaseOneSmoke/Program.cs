using System;
using Dolphin.Graphics;
internal static class Program
{
    private static void Near(float a,float b){if(Math.Abs(a-b)>.002f)throw new Exception("Graphics math mismatch.");}
    static int Main()
    {
        var camera2D=new Camera2D{Position=new Vector2(20,-10),Origin=new Vector2(100,50),Zoom=2,RotationDegrees=90};
        var p=camera2D.WorldToScreen(new Vector2(23,-6));var q=camera2D.ScreenToWorld(p);Near(q.X,23);Near(q.Y,-6);
        camera2D.Zoom=0;try{camera2D.WorldToScreen(default);return 20;}catch(ArgumentOutOfRangeException){}camera2D.Zoom=2;
        try{new SamplerState((TextureAddressMode)4,TextureAddressMode.Clamp);return 21;}catch(ArgumentException){}
        try{new RenderQueue(4,129);return 22;}catch(ArgumentOutOfRangeException){}
        var uv=new UvTransform(new Vector2(.2f,.3f),new Vector2(2,3),90);p=uv.Transform(new Vector2(1,0));Near(p.X,.2f);Near(p.Y,2.3f);
        var camera=new Camera(55,4f/3f,.1f,100);camera.LookAt(new Vector3(0,0,5),Vector3.Zero,new Vector3(0,1,0));var viewport=new Viewport(32,24,320,240);
        try{camera.SetPerspective(float.NaN,1,.1f,100);return 23;}catch(ArgumentException){}
        var screen=camera.Project(Vector3.Zero,viewport);Near(screen.X,192);Near(screen.Y,144);q=default;
        var back=camera.Unproject(screen,viewport);Near(back.X,0);Near(back.Y,0);Near(back.Z,0);
        var repeated=camera.Project(Vector3.Zero,viewport);Near(repeated.X,192);Near(repeated.Y,144);
        var ray=camera.CreatePickingRay(new Vector2(192,144),viewport);Near(ray.Direction.Z,-1);
        if(!camera.Intersects(new BoundingSphere(Vector3.Zero,1))||camera.Intersects(new BoundingSphere(new Vector3(10000,0,0),1)))return 1;
        if(!camera.Intersects(new BoundingBox(new Vector3(-1,-1,-1),Vector3.One))||camera.Intersects(new BoundingBox(new Vector3(1000,0,0),new Vector3(1001,1,1))))return 2;
        camera.SetOrthographic(8,6,.1f,100);screen=camera.Project(new Vector3(2,0,0),viewport);Near(screen.X,272);back=camera.Unproject(screen,viewport);Near(back.X,2);Near(back.Z,0);
        camera.SetPerspective(55,4f/3f,.1f,100);
        using var texture=Texture2D.Create(4,4,new byte[64]);using var draw=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{VertexCapacity=12,SpriteCapacity=128,Width=320,Height=240});
        using var mesh=Mesh.Create(new[]{new VertexPositionNormalColorTexture(Vector3.Zero,new Vector3(0,0,1),Color32.White,default),new VertexPositionNormalColorTexture(new Vector3(1,0,0),new Vector3(0,0,1),Color32.White,default),new VertexPositionNormalColorTexture(new Vector3(0,1,0),new Vector3(0,0,1),Color32.White,default)});
        var material=new Material{Texture=texture,Lighting=LightingMode.Diffuse,Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Mirror,TextureFilter.Nearest),TextureTransform=uv};
        var transform=new Transform();var moved=new Transform{LocalPosition=new Vector3(2,0,0)};
        var renderer=new Renderer(GraphicsDevice.Default,128);var settings=new RenderSettings{Ambient=new Color32(20,30,40),LightCount=3,Fog=new FogSettings(FogMode.Linear,new Color32(50,60,70),1,80)};
        settings.Lights[0]=new SceneLight(LightKind.Directional,Color32.White,Vector3.Zero,new Vector3(0,0,-1));settings.Lights[1]=new SceneLight(LightKind.Point,Color32.White,new Vector3(0,2,3),default);settings.Lights[2]=new SceneLight(LightKind.Spot,Color32.White,new Vector3(0,0,3),new Vector3(0,0,-1));
        var queue=new RenderQueue(4);var sprites=new[]{new SpriteDraw(new TextureRegion(texture,new RectF(0,0,4,4)),new RectF(10,10,20,20),Color32.White){Flip=SpriteFlip.Horizontal}};
        var device=GraphicsDevice.Default;device.BeginFrame(Color32.Black);
        settings.LightCount=9;try{renderer.Begin3D(camera,settings);return 24;}catch(ArgumentOutOfRangeException){}settings.LightCount=3;
        renderer.Begin3D(camera,settings);
        renderer.DrawMeshes(new[]{mesh,mesh},new[]{transform,moved},material,0,2);queue.Submit(mesh,material,transform);queue.Submit(mesh,material,moved);var stats=queue.Execute(renderer);if(stats.Draws!=1||stats.MeshesMerged!=0)return 3;
        renderer.End3D();draw.Begin(camera2D,viewport);draw.SetSampler(new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Clamp));try{draw.Begin(new Camera2D());return 25;}catch(InvalidOperationException){}
        var invalidSprite=sprites[0];invalidSprite.Flip=(SpriteFlip)4;var invalidSprites=new[]{sprites[0],invalidSprite};
        try{draw.DrawSprites(invalidSprites,0,2);return 26;}catch(ArgumentException){}
        if(draw.DrawCalls!=0)return 27;
        draw.PushClip(0,0,100,100);draw.PushClip(200,200,10,10);draw.DrawSprites(sprites,0,1);draw.PopClip();draw.DrawSprites(sprites,0,1);draw.PopClip();draw.End();device.EndFrame();
        device.BeginFrame(Color32.Black);renderer.Begin3D(camera);renderer.DrawMesh(mesh,new Material(),transform);renderer.End3D();draw.Begin();draw.FillRectangle(new RectF(1,2,3,4),Color32.White);draw.End();device.EndFrame();
        return 0;
    }
}
