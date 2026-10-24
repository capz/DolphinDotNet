using System;
using Dolphin.Graphics;
using Dolphin.Diagnostics;

// Identical procedural texels in two layouts. No new UI: use standard debug overlay.
// Pages: 144 distinct RGBA8 GX textures, one draw each.
// Atlases: 9 RGBA8 textures each covering 4x4 cells, one draw each.
internal sealed class StrategyScene : IDisposable
{
    private const int Count=12, CellPixels=32, Group=4;
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Renderer renderer=new Renderer();
    private readonly Camera camera=new Camera(55,4f/3f,.1f,100f);
    private readonly Mesh unitQuad;
    private readonly Mesh atlasQuad;
    private readonly Texture2D[] pages=new Texture2D[Count*Count];
    private readonly Texture2D[] atlases=new Texture2D[9];
    private readonly Material[] pageMaterials=new Material[Count*Count];
    private readonly Material[] atlasMaterials=new Material[9];
    private readonly Transform[] pageTransforms=new Transform[Count*Count];
    private readonly Transform[] atlasTransforms=new Transform[9];
    internal StrategyScene()
    {
        DebugOverlay.WriteLine("TEXTURE STRATEGY: MESHES");
        camera.LookAt(new Vector3(6,6,23),new Vector3(6,6,0),new Vector3(0,1,0));
        unitQuad=Mesh.Create(Quad(1,1));
        atlasQuad=Mesh.Create(Quad(Group,Group));
        // Texture2D.Create copies pixels into native storage synchronously.
        // Reuse scratch arrays rather than fragmenting the 256 KiB managed heap.
        DebugOverlay.WriteLine("TEXTURE STRATEGY: PAGES");
        var pagePixels=new byte[CellPixels*CellPixels*4];
        var atlasPixels=new byte[Group*CellPixels*Group*CellPixels*4];
        for(int y=0;y<Count;y++)for(int x=0;x<Count;x++)
        {
            int id=y*Count+x;
            var pixels=pagePixels;
            for(int py=0;py<CellPixels;py++)for(int px=0;px<CellPixels;px++)
                Pixel(pixels,CellPixels,px,py,x,y);
            pages[id]=Texture2D.Create(CellPixels,CellPixels,pixels,true);
            pageMaterials[id]=new Material { Texture=pages[id],Cull=CullMode.None,Lighting=LightingMode.Unlit };
            pageTransforms[id]=new Transform { LocalPosition=new Vector3(x,y,0) };
        }
        DebugOverlay.WriteLine("TEXTURE STRATEGY: ATLASES");
        for(int gy=0;gy<3;gy++)for(int gx=0;gx<3;gx++)
        {
            int id=gy*3+gx;
            var pixels=atlasPixels;
            for(int y=0;y<Group;y++)for(int x=0;x<Group;x++)
                for(int py=0;py<CellPixels;py++)for(int px=0;px<CellPixels;px++)
                    Pixel(pixels,Group*CellPixels,x*CellPixels+px,y*CellPixels+py,gx*Group+x,gy*Group+y);
            atlases[id]=Texture2D.Create(Group*CellPixels,Group*CellPixels,pixels,true);
            atlasMaterials[id]=new Material { Texture=atlases[id],Cull=CullMode.None,Lighting=LightingMode.Unlit };
            atlasTransforms[id]=new Transform { LocalPosition=new Vector3(gx*Group,gy*Group,0) };
        }
        DebugOverlay.WriteLine("TEXTURE STRATEGY: SCENE READY");
    }
    private static VertexPositionColorTexture V(float x,float y,float u,float v)
        =>new VertexPositionColorTexture(new Vector3(x,y,0),Color32.White,new Vector2(u,v));
    private static VertexPositionColorTexture[] Quad(int width,int height)
    {
        var vertices=new VertexPositionColorTexture[width*height*6];
        int i=0;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            float u0=x/(float)width,u1=(x+1)/(float)width,v0=y/(float)height,v1=(y+1)/(float)height;
            vertices[i++]=V(x,y,u0,v0);vertices[i++]=V(x+1,y,u1,v0);vertices[i++]=V(x+1,y+1,u1,v1);
            vertices[i++]=V(x,y,u0,v0);vertices[i++]=V(x+1,y+1,u1,v1);vertices[i++]=V(x,y+1,u0,v1);
        }
        return vertices;
    }
    private static void Pixel(byte[] data,int stride,int px,int py,int cellX,int cellY)
    {
        int localX=px%CellPixels,localY=py%CellPixels;
        int i=(py*stride+px)*4;
        int checker=((localX/8)^(localY/8)^(cellX*3+cellY))&1;
        data[i]=(byte)(checker!=0?45+cellX*12:18+cellX*8);
        data[i+1]=(byte)(checker!=0?65+cellY*11:26+cellY*8);
        data[i+2]=(byte)(localX==0||localY==0?245:checker!=0?184:84);
        data[i+3]=255;
    }
    internal void Present(bool atlas)
    {
        device.BeginFrame(new Color32(8,10,19));
        renderer.Begin3D(camera);
        if(atlas)
        {
            for(int i=0;i<atlases.Length;i++) renderer.DrawMesh(atlasQuad,atlasMaterials[i],atlasTransforms[i]);
        }
        else
        {
            for(int i=0;i<pages.Length;i++) renderer.DrawMesh(unitQuad,pageMaterials[i],pageTransforms[i]);
        }
        renderer.End3D();
        device.EndFrame();
    }
    public void Dispose()
    {
        for(int i=0;i<pages.Length;i++)if(pages[i]!=null)pages[i].Dispose();
        for(int i=0;i<atlases.Length;i++)if(atlases[i]!=null)atlases[i].Dispose();
        unitQuad.Dispose();atlasQuad.Dispose();
    }
}
