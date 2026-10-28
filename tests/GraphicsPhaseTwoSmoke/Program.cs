using System;
using Dolphin.Graphics;
internal static class Program
{
    private static void Equal(float a,float b)
    {if(float.IsNaN(a)||float.IsInfinity(a)||Math.Abs(a-b)>.001f)throw new Exception("Layout measurement mismatch.");}
    static int Main()
    {
        var device=GraphicsDevice.Default;
        using var texture=Texture2D.Create(4,4,new byte[64]);
        var package=new byte[224];package[0]=68;package[1]=78;package[2]=84;package[3]=88;
        package[4]=2;package[8]=6;package[12]=package[16]=4;package[20]=192;package[24]=3;
        using var mip=Texture2D.FromPackage(package,true);if(mip.MipLevels!=3||mip.NativeBytes!=192)return 15;
        using var font=new BitmapFont(texture,1,1,64,16);
        using var layout=new BitmapTextLayout(font,"AB\tC\nD",4,TextAlignment.Center);
        Equal(layout.Size.X,4);Equal(layout.Size.Y,3);
        if(layout.LineCount!=3||layout.GlyphCount!=4||layout.NativeBytes!=4*6*36)return 1;
        using var tabs=new BitmapTextLayout(font,"\t\t",1);if(tabs.LineCount!=8)return 2;
        using var empty=new BitmapTextLayout(font,"");if(empty.GlyphCount!=0||empty.NativeBytes!=0)return 3;
        try{new BitmapTextLayout(font,"A",.5f);return 4;}catch(ArgumentException){}
        var builder=new Geometry2DBuilder(1024);
        builder.AddGradient(new RectF(0,0,20,10),Color32.White,Color32.Black,Color32.White,Color32.Black);
        builder.AddCircle(new Vector2(30,30),5,Color32.White,8);
        builder.AddRoundedRectangle(new RectF(50,10,20,30),4,Color32.White,4);
        if(builder.VertexCount!=90)return 5;
        using var geometry=builder.Build();builder.Clear();if(geometry.NativeBytes!=90*36)return 6;
        var tiny=new Geometry2DBuilder(3);
        try{tiny.AddCircle(default,1,Color32.White,8);return 7;}catch(InvalidOperationException){}
        if(tiny.VertexCount!=0)return 8;
        try{builder.AddCircle(default,float.PositiveInfinity,Color32.White);return 9;}catch(ArgumentException){}
        using var plane=PrimitiveMeshes.Plane(2,3);using var box=PrimitiveMeshes.Box(2,3,4);using var sphere=PrimitiveMeshes.Sphere(1,8,4);
        if(plane.NativeBytes!=4*36+6*2||box.NativeBytes!=24*36+36*2||sphere.NativeBytes!=45*36+144*2)return 10;
        using var billboard=new Billboard();using var capture=new SceneCapture(device,4,4);
        var camera=new Camera();var renderer=new Renderer();var material=new Material{Cull=CullMode.None};var transform=new Transform();
        using var draw=new Draw2D(device);
        device.BeginFrame(Color32.Black);
        renderer.Begin3D(camera);renderer.DrawMesh(plane,material,transform);renderer.DrawMesh(box,material,transform);
        renderer.DrawMesh(sphere,material,transform);renderer.DrawBillboard(billboard,camera,Vector3.Zero,new Vector2(2,3),material);renderer.End3D();
        capture.Capture(new Viewport(0,0,8,8));
        try{capture.Capture(new Viewport(1,0,4,4));return 11;}catch(ArgumentException){}
        try{capture.Capture(new Viewport(0,0,12,12));return 12;}catch(InvalidOperationException){}
        draw.Begin(new Camera2D{Zoom=2});draw.DrawGeometry(geometry,new Vector2(2,3),Color32.White);
        layout.Draw(draw,new Vector2(5,5),Color32.White);empty.Draw(draw,default,Color32.White);
        draw.PushClip(0,0,0,0);draw.DrawGeometry(geometry);draw.PopClip();capture.Draw(draw,new RectF(0,0,4,4),Color32.White);draw.End();device.EndFrame();
        layout.Dispose();try{layout.Draw(draw,default,Color32.White);return 13;}catch(ObjectDisposedException){}
        geometry.Dispose();device.BeginFrame(Color32.Black);draw.Begin();
        try{draw.DrawGeometry(geometry);return 14;}catch(ObjectDisposedException){}
        draw.End();device.EndFrame();
        using var borrowed=new BitmapTextLayout(font,"AB");font.Dispose();
        try{borrowed.Draw(draw,default,Color32.White);return 16;}catch(ObjectDisposedException){}
        return 0;
    }
}
