using System;
using Dolphin.Graphics;

internal sealed class ShaderHud : IDisposable
{
    private readonly Draw2D draw;
    private int oldLow=-1,oldHigh=-1;private bool oldGraph;private string parameters="";
    private readonly Color32 cyan=new Color32(112,224,240),white=new Color32(231,239,252),
        dim=new Color32(65,103,133),gold=new Color32(255,210,104);
    private readonly SampleText text=new SampleText();
    internal ShaderHud(GraphicsDevice device)
    { draw=new Draw2D(device,new Draw2DOptions { Width=640,Height=480,VertexCapacity=6144 }); }
    private void Rect(float x,float y,float w,float h,Color32 color)=>draw.FillRectangle(new RectF(x,y,w,h),color);

    private void Text(string value,float x,float y,float size,Color32 color)
    { text.Draw(draw,value,x,y,size,color); }
    internal void Present(bool paused,byte low,byte high,bool graph)
    {
        draw.Begin();
        Text("TEV SHADER LAB",28,52,5,white);
        Text("IMPORTED MODEL: DEFAULT / TOON / DEFAULT",28,88,2,dim);
        Text("REFERENCE",42,132,3,white);
        Text("COLOR RAMP",246,132,3,cyan);
        Text("TOON BANDS",450,132,3,gold);
        Text("1 STAGE",74,370,2,dim);
        Text("2 STAGES",282,370,2,dim);
        Text("PRESET GRAPH",478,370,2,dim);
        if(low!=oldLow||high!=oldHigh||graph!=oldGraph)
        {oldLow=low;oldHigh=high;oldGraph=graph;parameters=$"LOW {low:D3} HIGH {high:D3}  {(graph?"GRAPH":"PRESET")}";}
        Text(parameters,28,390,2,gold);
        Text("SAME GEOMETRY AND GRAYSCALE INPUT",28,410,3,white);
        Text(paused?"PAUSED":"ANIMATED REGISTERS",28,438,2,cyan);
        Text("STICK THRESHOLDS  CSTICK COLOR  A GRAPH  Y RESET",28,460,2,dim);
        draw.End();
    }
    public void Dispose(){text.Dispose();draw.Dispose();}
}
