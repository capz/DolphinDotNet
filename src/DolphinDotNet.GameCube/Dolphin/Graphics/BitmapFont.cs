using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dolphin.Graphics;

/// <summary>Prebuilt fixed-cell bitmap font. Glyphs are BMP code units in row-major atlas order.</summary>
public sealed class BitmapFont : IDisposable
{
    public Texture2D Texture {get;}
    public int CellWidth {get;}
    public int CellHeight {get;}
    public int FirstCharacter {get;}
    public int CharacterCount {get;}
    private readonly bool ownsTexture;
    private bool disposed;
    private void Check(){if(disposed)throw new ObjectDisposedException("BitmapFont");Texture.Check();}
    public BitmapFont(Texture2D texture,int cellWidth,int cellHeight,int firstCharacter=32,int characterCount=96)
        : this(texture,cellWidth,cellHeight,firstCharacter,characterCount,false) { }
    private BitmapFont(Texture2D texture,int w,int h,int first,int count,bool owns)
    {
        if(texture==null)throw new ArgumentNullException("texture");texture.Check();
        if(w<1||h<1||texture.Width%w!=0||texture.Height%h!=0||first<0||first>65535||count<1||count>65536-first||count>(texture.Width/w)*(texture.Height/h))throw new ArgumentException("Invalid bitmap font metrics.");
        Texture=texture;CellWidth=w;CellHeight=h;FirstCharacter=first;CharacterCount=count;ownsTexture=owns;
    }
    private static int Read(byte[] bytes,int offset) => bytes[offset]|bytes[offset+1]<<8|bytes[offset+2]<<16|bytes[offset+3]<<24;
    public static async Task<BitmapFont> LoadAsync(string path,CancellationToken cancellationToken=default)
    {
        var bytes=await System.IO.File.ReadAllBytesAsync(path,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if(bytes.Length<64||Read(bytes,0)!=0x54464e44||Read(bytes,4)!=1||Read(bytes,28)!=0||Read(bytes,24)!=bytes.Length-32)throw new ArgumentException("Invalid DNFT font package.");
        var data=new byte[bytes.Length-32];Array.Copy(bytes,32,data,0,data.Length);
        var texture=Texture2D.FromPackage(data);
        try{return new BitmapFont(texture,Read(bytes,8),Read(bytes,12),Read(bytes,16),Read(bytes,20),true);}
        catch{texture.Dispose();throw;}
    }
    internal RectF Glyph(int character)
    {
        Check();int index=character-FirstCharacter;
        if(index<0||index>=CharacterCount)index='?'-FirstCharacter;
        if(index<0||index>=CharacterCount)return default;
        int columns=Texture.Width/CellWidth;
        return new RectF((index%columns)*CellWidth,(index/columns)*CellHeight,CellWidth,CellHeight);
    }
    public Vector2 MeasureText(string text,float scale=1)
    {
        Check();if(text==null)throw new ArgumentNullException("text");if(!(scale>0&&scale<=1024))throw new ArgumentOutOfRangeException("scale");
        int x=0,max=0,lines=1;
        for(int i=0;i<text.Length;i++){char c=text[i];if(c=='\r')continue;if(c=='\n'){if(x>max)max=x;x=0;lines++;}else x+=c=='\t'?4:1;}
        if(x>max)max=x;return new Vector2(max*CellWidth*scale,lines*CellHeight*scale);
    }
    public void Dispose(){if(disposed)return;if(ownsTexture)Texture.Dispose();disposed=true;}
}
