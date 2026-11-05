using Dolphin.Graphics;
namespace Dolphin.System;

/// <summary>Native graphics publication for extracted IPL firmware resources.
/// Call on the graphics application executor between frames. Caller owns the texture.</summary>
public static class SystemResources
{
    public static Texture2D CreateTexture(FirmwareCatalog catalog,int resourceIndex,int headerOffset,bool linear=false)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        return Texture2D.FromBti(FirmwareTextures.Extract(catalog.Extract(resourceIndex),headerOffset),linear);
    }
}

/// <summary>Firmware font facade retaining the existing font's native lifetime and
/// ASCII glyph behavior. No firmware bytes are embedded.</summary>
public sealed class SystemFont : global::System.IDisposable
{
    private readonly IplFont font;
    private SystemFont(IplFont value){font=value;}
    public static SystemFont Load(bool linear=true)=>new SystemFont(IplFont.Load(linear));
    public Texture2D Texture=>font.Texture;
    public int CellWidth=>font.CellWidth;
    public int CellHeight=>font.CellHeight;
    public int LineHeight=>font.LineHeight;
    public Vector2 MeasureText(string text,float scale=1)=>font.MeasureText(text,scale);
    public void Draw(Draw2D draw,string text,Vector2 position,Color32 color,float scale=1)
    {
        if(draw==null)throw new global::System.ArgumentNullException(nameof(draw));
        draw.DrawText(font,text,position,color,scale);
    }
    public void Dispose()=>font.Dispose();
}
