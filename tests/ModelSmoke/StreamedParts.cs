using System;
using Dolphin.Graphics;

internal static class StreamedPartChecks
{
    internal static void Run()
    {
        using var model=Model.LoadAsync("sd:/streamed-multi.dndmodel").Result;
        if(model.MaterialCount!=3)throw new Exception("Streamed material slot count.");
        using var instance=model.CreateInstance();instance.Animator.Play("Walk");instance.Update(.5f);
        var rgba=new byte[64];for(int i=0;i<16;i++){rgba[i*4]=200;rgba[i*4+1]=40;rgba[i*4+2]=80;rgba[i*4+3]=255;}
        using var texture=Texture2D.Create(4,4,rgba);
        var material=new Material{Texture=texture,SecondaryTexture=texture,Cull=CullMode.None,TextureTransform=new UvTransform(new Vector2(.1f,.2f),new Vector2(.5f,.7f),30)};
        var renderer=new Renderer();var transform=new Transform();
        // An instance retains geometry after its source model's owner closes.
        model.Dispose();var matrix=new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
        renderer.BeginFrame(matrix,matrix);
        renderer.DrawModelMaterialPart(instance,1,transform,material);renderer.DrawModelMaterialPart(instance,2,transform,material);
        renderer.DrawModelMaterialPart(instance,0,transform,material); // unused default slot
        try{renderer.DrawModelMaterialPart(instance,-1,transform,material);throw new Exception("Negative material index accepted.");}catch(ArgumentOutOfRangeException){}
        try{renderer.DrawModelMaterialPart(instance,3,transform,material);throw new Exception("Large material index accepted.");}catch(InvalidOperationException){}
        renderer.EndFrame();
    }
}
