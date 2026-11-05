using Dolphin.Graphics;
using System.Threading;
namespace Dolphin.System;

/// <summary>Adapters from extracted IPL firmware assets to the existing native
/// graphics and audio resources. Returned resources are owned by the caller.</summary>
public static class SystemResources
{
    /// <summary>Import original model geometry/materials through the existing
    /// background model pipeline. The model owns its native data independently
    /// of the catalog. Unknown material profiles fail without approximation.</summary>
    public static global::System.Threading.Tasks.Task<Model> CreateModelAsync(FirmwareCatalog catalog,int resourceIndex,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        return Model.LoadJ3dAsync(catalog.Extract(resourceIndex,cancellationToken),cancellationToken,nativeBudgetBytes);
    }
    public static global::System.Threading.Tasks.Task<Model> CreateModelAsync(FirmwareCatalog catalog,SystemModel name,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        return CreateModelAsync(catalog,catalog.GetModelIndex(name),cancellationToken,nativeBudgetBytes);
    }
    /// <summary>Lossless compressed stereo source. May validate on the background
    /// executor; playback must use the application executor. Cancellation releases native state.</summary>
    public static Dolphin.Audio.Sound CreateStartupSound(FirmwareCatalog catalog,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        cancellationToken.ThrowIfCancellationRequested();
        return Dolphin.Audio.Sound.FromEncoded(catalog.GetStartupAudio().ExtractAfc(),Dolphin.Audio.AudioFormat.Afc,nativeBudgetBytes:nativeBudgetBytes,cancellationToken:cancellationToken);
    }
    /// <summary>The same original compressed stereo source through Music's normal lifetime.</summary>
    public static Dolphin.Audio.Music CreateStartupMusic(FirmwareCatalog catalog,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        cancellationToken.ThrowIfCancellationRequested();
        return Dolphin.Audio.Music.FromEncoded(catalog.GetStartupAudio().ExtractAfc(),Dolphin.Audio.AudioFormat.Afc,nativeBudgetBytes:nativeBudgetBytes,cancellationToken:cancellationToken);
    }
    /// <summary>Publish a caller-owned texture on the graphics executor between frames.</summary>
    public static Texture2D CreateTexture(FirmwareCatalog catalog,SystemTexture name,bool linear=false)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        var texture=catalog.GetTexture(name);
        return CreateTexture(catalog,texture.ResourceIndex,texture.HeaderOffset,linear);
    }
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
