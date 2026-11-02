using System;
namespace Dolphin.Graphics;

/// <summary>Printable ASCII from the console's IPL firmware, loaded through
/// libogc. No firmware font bytes are embedded in the application.</summary>
public sealed class IplFont : IDisposable
{
    public Texture2D Texture {get;}
    public int CellWidth=>metrics[0];
    public int CellHeight=>metrics[1];
    public int Ascent=>metrics[2];
    public int Descent=>metrics[3];
    public int LineHeight=>metrics[4];
    /// <summary>libogc encoding: 0 ANSI, 1 Shift-JIS. This API exposes ASCII only.</summary>
    public int FirmwareEncoding=>metrics[5];
    /// <summary>Shared SYS_InitFont backing storage, retained until graphics shutdown.</summary>
    public int SharedFirmwareBytes=>metrics[6];
    public int NativeBytes {get{Check();return Texture.NativeBytes;}}
    private readonly int[] metrics;
    private bool disposed;
    private IplFont(Texture2D texture,int[] metrics){Texture=texture;this.metrics=metrics;}
    /// <summary>Call between frames on the application executor. Throws when
    /// the firmware font is unavailable; never substitutes an unrelated font.</summary>
    public static IplFont Load(bool linear=true)
    {
        var metrics=new int[8+95*5];var texture=Texture2D.CreateIplFont(metrics,linear);
        return new IplFont(texture,metrics);
    }
    internal void Check(){if(disposed)throw new ObjectDisposedException("IplFont");Texture.Check();}
    private static int Index(char c)=>c>=32&&c<=126?c-32:'?'-32;
    internal RectF Glyph(char c,out int advance)
    {Check();int at=8+Index(c)*5;advance=metrics[at+4];return new RectF(metrics[at],metrics[at+1],metrics[at+2],metrics[at+3]);}
    public Vector2 MeasureText(string text,float scale=1)
    {
        Check();if(text==null)throw new ArgumentNullException("text");if(!(scale>0&&scale<=1024))throw new ArgumentOutOfRangeException("scale");
        int x=0,max=0,lines=1;
        for(int i=0;i<text.Length;i++){char c=text[i];if(c=='\r')continue;if(c=='\n'){if(x>max)max=x;x=0;lines++;}else{x+=metrics[8+Index(c=='\t'?' ':c)*5+4]*(c=='\t'?4:1);}}
        if(x>max)max=x;return new Vector2(max*scale,((lines-1)*LineHeight+CellHeight)*scale);
    }
    public void Dispose(){if(disposed)return;Texture.Dispose();disposed=true;}
}
