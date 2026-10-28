using System;
namespace Dolphin.Graphics;
public enum TextAlignment { Left,Center,Right }

/// <summary>Immutable glyph geometry, laid out once with fixed-cell wrapping.
/// Owns native geometry; borrows the font and its texture until disposal.</summary>
public sealed class BitmapTextLayout : IDisposable
{
    private readonly BitmapFont font;
    private readonly Geometry2D? geometry;
    private bool disposed;
    public Vector2 Size {get;}
    public int GlyphCount {get;}
    public int LineCount {get;}
    public int NativeBytes {get{Check();return geometry==null?0:geometry.NativeBytes;}}
    /// <param name="wrapWidth">Zero disables wrapping. Words split at the cell boundary.</param>
    public BitmapTextLayout(BitmapFont font,string text,float wrapWidth=0,
                            TextAlignment alignment=TextAlignment.Left,float scale=1)
    {
        if(font==null||text==null)throw new ArgumentNullException();
        font.Check();GraphicsMath.Finite(wrapWidth);GraphicsMath.Finite(scale);
        if(wrapWidth<0||scale<=0||scale>1024||(int)alignment<0||(int)alignment>2||text.Length>10922)
            throw new ArgumentException("Invalid layout or text exceeds 10922 code units.");
        this.font=font;
        float cellWidth=font.CellWidth*scale,cellHeight=font.CellHeight*scale;
        int columns=wrapWidth==0?int.MaxValue:(int)Math.Min(10923,Math.Floor(wrapWidth/cellWidth));
        if(columns<1)throw new ArgumentException("Wrap width is less than one cell.");
        var lineWidths=new int[text.Length*4+1];
        var xs=new int[text.Length];var lines=new int[text.Length];var visible=new bool[text.Length];
        int line=0,x=0,max=0,glyphs=0;
        for(int i=0;i<text.Length;i++){
            char c=text[i];if(c=='\r')continue;
            if(c=='\n'){lineWidths[line]=x;if(x>max)max=x;line++;x=0;continue;}
            int advance=c=='\t'?4:1;
            for(int step=0;step<advance;step++){
                if(x==columns){lineWidths[line]=x;if(x>max)max=x;line++;x=0;}
                if(c!='\t'){xs[i]=x;lines[i]=line;visible[i]=true;glyphs++;}
                x++;
            }
        }
        lineWidths[line]=x;if(x>max)max=x;
        LineCount=line+1;GlyphCount=glyphs;
        float width=wrapWidth==0?max*cellWidth:wrapWidth;
        Size=new Vector2(width,LineCount*cellHeight);
        if(glyphs==0)return;
        var builder=new Geometry2DBuilder(glyphs*6,font.Texture);
        for(int i=0;i<text.Length;i++)if(visible[i]){
            var source=font.Glyph(text[i]);if(source.Width==0)continue;
            float extra=width-lineWidths[lines[i]]*cellWidth;
            float left=alignment==TextAlignment.Center?extra*.5f:alignment==TextAlignment.Right?extra:0;
            builder.AddSprite(new TextureRegion(font.Texture,source),
                new RectF(left+xs[i]*cellWidth,lines[i]*cellHeight,cellWidth,cellHeight),Color32.White);
        }
        GlyphCount=builder.VertexCount/6;
        if(builder.VertexCount>0)geometry=builder.Build();
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("BitmapTextLayout");font.Check();}
    public void Draw(Draw2D batch,Vector2 position,Color32 color)
    {
        Check();if(batch==null)throw new ArgumentNullException("batch");
        if(geometry!=null)batch.DrawGeometry(geometry,position,color);
    }
    public void Dispose(){if(disposed)return;if(geometry!=null)geometry.Dispose();disposed=true;}
}
