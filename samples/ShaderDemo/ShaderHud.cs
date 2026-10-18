using System;
using Dolphin.Graphics;

internal sealed class ShaderHud : IDisposable
{
    private readonly Draw2D draw;
    private readonly Color32 cyan=new Color32(112,224,240),white=new Color32(231,239,252),
        dim=new Color32(65,103,133),gold=new Color32(255,210,104);
    private static readonly int[] glyphs={
        // A-Z then 0-9, packed row-major as 3 x 5 pixels.
        11245, 27566, 14627, 27502, 31143, 31140, 14699, 23533, 29847, 4714, 23469, 18727, 24557, 24573, 11114, 27556, 11131, 27565, 14478, 29842, 23407, 23402, 23549, 23213, 23186, 29351,
        31599, 11415, 25575, 25294, 23497, 31118, 14831, 29330, 31727, 31694
    };
    internal ShaderHud(GraphicsDevice device)
    { draw=new Draw2D(device,new Draw2DOptions { Width=640,Height=480,VertexCapacity=6144 }); }
    private void Rect(float x,float y,float w,float h,Color32 color)=>draw.FillRectangle(new RectF(x,y,w,h),color);
    private void Glyph(int glyph,float x,float y,float size,Color32 color)
    {
        for(int row=0;row<5;row++)for(int col=0;col<3;col++)
            if((glyph&(1<<(14-row*3-col)))!=0)Rect(x+col*size,y+row*size,size,size,color);
    }
    private void Text(string text,float x,float y,float size,Color32 color)
    {
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(c>='A' && c<='Z')Glyph(glyphs[c-'A'],x,y,size,color);
            else if(c>='0' && c<='9')Glyph(glyphs[26+c-'0'],x,y,size,color);
            x+=size*4;
        }
    }
    internal void Present(bool paused)
    {
        draw.Begin();
        Text("TEV SHADER LAB",28,52,5,white);
        Text("GAMECUBE PIXEL COMBINERS",28,88,2,dim);
        Text("REFERENCE",42,132,3,white);
        Text("COLOR RAMP",246,132,3,cyan);
        Text("TOON BANDS",450,132,3,gold);
        Text("1 STAGE",74,370,2,dim);
        Text("1 STAGE",282,370,2,dim);
        Text("2 STAGES",486,370,2,dim);
        Text("SAME GEOMETRY AND GRAYSCALE INPUT",28,410,3,white);
        Text(paused?"PAUSED":"ANIMATED REGISTERS",28,438,2,cyan);
        Text("START PAUSE   B EXIT   Z STATS",28,460,2,dim);
        draw.End();
    }
    public void Dispose()=>draw.Dispose();
}
