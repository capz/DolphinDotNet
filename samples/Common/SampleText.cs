using System;
using Dolphin.Graphics;

// One generated fixed-cell atlas per UI owner; no file or font tool required.
internal sealed class SampleText : IDisposable
{
    private readonly Texture2D texture;
    private readonly BitmapFont font;
    private readonly string[] numbers=new string[32],formats=new string[32];
    private readonly int[] values=new int[32],digitsCache=new int[32];
    private readonly float[] xs=new float[32],ys=new float[32];
    private int count,next;
    private static readonly int[] glyphs={11245,27566,14627,27502,31143,31140,14699,23533,29847,4714,23469,18727,24557,24573,11114,27556,11131,27565,14478,29842,23407,23402,23549,23213,23186,29351,31599,11415,25575,25294,23497,31118,14831,29330,31727,31694};
    private static int Glyph(int c)
    {
        if(c>=97&&c<=122)c-=32;
        if(c>=65&&c<=90)return glyphs[c-65];
        if(c>=48&&c<=57)return glyphs[26+c-48];
        if(c==46)return 2;if(c==58)return 1040;if(c==45)return 448;
        if(c==47)return 4680;if(c==62)return 17428;if(c==33)return 9362;
        if(c==63)return 25218;if(c==37)return 21157;if(c==43)return 1488;
        return 0;
    }
    internal SampleText()
    {
        var pixels=new byte[64*36*4];
        for(int c=32;c<128;c++)
        {
            int bits=Glyph(c),ox=(c-32)%16*4,oy=(c-32)/16*6;
            for(int y=0;y<5;y++)for(int x=0;x<3;x++)if((bits&(1<<(14-y*3-x)))!=0)
            {int at=((oy+y)*64+ox+x)*4;pixels[at]=pixels[at+1]=pixels[at+2]=pixels[at+3]=255;}
        }
        texture=Texture2D.Create(64,36,pixels);
        try{font=new BitmapFont(texture,4,6);}catch{texture.Dispose();throw;}
    }
    internal void Draw(Draw2D draw,string text,float x,float y,float size,Color32 color)
        =>draw.DrawText(font,text,new Vector2(x,y),color,size);
    internal Vector2 Measure(string text,float size=1)=>font.MeasureText(text,size);
    internal void Number(Draw2D draw,int value,int digits,float x,float y,float size,Color32 color)
    {
        int slot=-1;
        for(int i=0;i<count;i++)if(xs[i]==x&&ys[i]==y&&digitsCache[i]==digits){slot=i;break;}
        if(slot<0)
        {
            slot=count<32?count++:next++%32;xs[slot]=x;ys[slot]=y;digitsCache[slot]=digits;
            formats[slot]="D"+digits.ToString();numbers[slot]="";values[slot]=int.MinValue;
        }
        if(values[slot]!=value){values[slot]=value;numbers[slot]=value.ToString(formats[slot]);}
        Draw(draw,numbers[slot],x,y,size,color);
    }
    public void Dispose(){font.Dispose();texture.Dispose();}
}
