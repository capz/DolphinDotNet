using System;
using Dolphin.Graphics;

internal static class ModelEffectChecks
{
    private static float[] Identity()=>new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    internal static void Run()
    {
        using var model=Model.LoadAsync("sd:/animated.dndmodel").Result;
        using var a=model.CreateInstance();
        using var entity=new GameObject(model.CreateInstance());
        using var empty=new GameObject();
        using var untextured=Model.LoadObjAsync("sd:/live.obj").Result;
        using var rigid=untextured.CreateInstance();
        using var ramp=GxEffect.ColorRamp(new Color32(10,20,30),new Color32(180,210,240));
        using var toon=GxEffect.ToonBands(65,165,new Color32(10,20,30),new Color32(60,100,140),new Color32(180,210,240));
        // One-stage TEXC program: the compatibility flag is required by DNEF.
        var package=new byte[184];package[0]=68;package[1]=78;package[2]=69;package[3]=70;
        package[4]=1;package[8]=1;package[12]=1;package[176]=package[177]=package[178]=15;package[179]=8;
        using var textureEffect=GxEffect.FromPackage(package);
        var disposed=GxEffect.ColorRamp(new Color32(0,0,0),new Color32(255,255,255));disposed.Dispose();
        var renderer=new Renderer();var transform=new Transform();var matrix=Identity();
        a.Animator.Play("Walk");a.Update(.5f);
        renderer.BeginFrame(matrix,matrix);
        renderer.DrawModel(a,matrix);renderer.DrawModel(a,matrix,null);
        renderer.DrawModel(a,transform);renderer.DrawModel(a,transform,null);
        renderer.Draw(entity);renderer.Draw(entity,null);renderer.Draw(entity,toon);
        renderer.DrawModel(a,matrix,ramp);renderer.DrawModel(a,transform,toon);
        renderer.DrawModel(rigid,transform,ramp);renderer.DrawModel(rigid,matrix,toon);
        renderer.Draw(empty,disposed); // unused effects are deliberately not inspected
        try{renderer.DrawModel(a,matrix,disposed);throw new Exception("Disposed override accepted.");}catch(ObjectDisposedException){}
        try{renderer.DrawModel(rigid,matrix,textureEffect);throw new Exception("Texture-requiring override accepted an untextured mesh.");}
        catch(ArgumentException error){if(!error.Message.Contains("effectOverride"))throw new Exception("Missing override parameter name.");}
        try{ramp.SetColor(0,new Color32(1,2,3));throw new Exception("Uniform update accepted during frame.");}catch(InvalidOperationException){}
        int graphicsBytes=GraphicsDevice.Default.NativeBytes,modelBytes=model.NativeBytes,instanceBytes=a.NativeBytes;
        for(int i=0;i<1000;i++){
            renderer.DrawModel(a,transform,i%2==0?toon:null);
            renderer.Draw(entity,ramp);
        }
        if(GraphicsDevice.Default.NativeBytes!=graphicsBytes||model.NativeBytes!=modelBytes||a.NativeBytes!=instanceBytes)
            throw new Exception("Override draws grew native budgets.");
        ramp.Dispose(); // encoded draws remain valid until frame completion
        try{renderer.Draw(entity,ramp);throw new Exception("Pending/disposed override accepted.");}catch(ObjectDisposedException){}
        renderer.DrawModel(a,transform);renderer.EndFrame();
        toon.SetColor(1,new Color32(22,33,44));
        model.UnloadToAramAsync().Wait();
        renderer.BeginFrame(matrix,matrix);
        try{renderer.DrawModel(a,transform,toon);throw new Exception("Cold model accepted an override.");}catch(InvalidOperationException){}
        renderer.EndFrame();model.PrepareAsync().Wait();
        renderer.BeginFrame(matrix,matrix);renderer.DrawModel(a,transform,toon);renderer.Draw(entity);renderer.EndFrame();
    }
}
