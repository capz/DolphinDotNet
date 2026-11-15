using Dolphin.Graphics;
using System.Threading;
namespace Dolphin.System;

/// <summary>Adapters from extracted IPL firmware assets to the existing native
/// graphics and audio resources. Returned resources are owned by the caller.</summary>
public static class SystemResources
{
    /// <summary>Prepare a self-contained native IPL score without starting it.
    /// Requires IncludeSystemAudio and a qualified complete-audio profile.</summary>
    public static SystemAudioSession CreateAudioSession(FirmwareCatalog catalog,
        CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        ValidateSessionRequest(catalog,nativeBudgetBytes);
        using(var snapshot=catalog.SnapshotSystemAudio(cancellationToken))
            return SystemAudioSession.Prepare(snapshot,nativeBudgetBytes,cancellationToken);
    }
    /// <summary>Capture an owned snapshot before returning the task. The image
    /// and catalog can then be disposed. Cancellation releases partial native state.</summary>
    public static global::System.Threading.Tasks.Task<SystemAudioSession> CreateAudioSessionAsync(FirmwareCatalog catalog,
        CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        ValidateSessionRequest(catalog,nativeBudgetBytes);
        var snapshot=catalog.SnapshotSystemAudio(cancellationToken);
        try
        {
            return global::System.Threading.Tasks.Task.Run(()=>
            {
                try{return SystemAudioSession.Prepare(snapshot,nativeBudgetBytes,cancellationToken);}
                finally{snapshot.Dispose();}
            });
        }
        catch{snapshot.Dispose();throw;}
    }
    private static void ValidateSessionRequest(FirmwareCatalog catalog,int budget)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        if(budget<1024||budget>16*1024*1024)throw new global::System.ArgumentOutOfRangeException("nativeBudgetBytes");
    }
    /// <summary>Create an owned original sample whose Play automatically uses
    /// embedded loop metadata. The catalog/image can be disposed after return.</summary>
    public static SystemAudioSample CreateAudioSample(FirmwareCatalog catalog,FirmwareAudioSampleInfo sample,
        CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        ValidateSampleRequest(catalog,sample,nativeBudgetBytes);
        using(var source=catalog.SnapshotAudioSample(sample,cancellationToken))
            return CreateSample(sample,source,cancellationToken,nativeBudgetBytes);
    }
    /// <summary>Snapshot the selected source before returning, then prepare on
    /// the background executor. It is safe to dispose the catalog once this
    /// method returns its task. Cancellation releases partial native state.</summary>
    public static global::System.Threading.Tasks.Task<SystemAudioSample> CreateAudioSampleAsync(FirmwareCatalog catalog,FirmwareAudioSampleInfo sample,
        CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        ValidateSampleRequest(catalog,sample,nativeBudgetBytes);
        var source=catalog.SnapshotAudioSample(sample,cancellationToken);
        try
        {
            return global::System.Threading.Tasks.Task.Run(()=>
            {
                try{return CreateSample(sample,source,cancellationToken,nativeBudgetBytes);}
                finally{source.Dispose();}
            });
        }
        catch {source.Dispose();throw;}
    }
    private static void ValidateSampleRequest(FirmwareCatalog catalog,FirmwareAudioSampleInfo sample,int budget)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        if(sample==null)throw new global::System.ArgumentNullException(nameof(sample));
        if(budget<1024||budget>16*1024*1024)throw new global::System.ArgumentOutOfRangeException("nativeBudgetBytes");
    }
    private static SystemAudioSample CreateSample(FirmwareAudioSampleInfo info,FirmwareMenuWaveSource source,CancellationToken cancellation,int budget)
    {
        var sound=Dolphin.Audio.Sound.FromEncoded(source.Header,Dolphin.Audio.AudioFormat.JAudioWaveBank,source.Body,
            nativeBudgetBytes:budget,cancellationToken:cancellation);
        return new SystemAudioSample(info,sound);
    }
    /// <summary>Create a caller-owned, prepared IPL menu sound without playing
    /// it. Unknown semantic profiles fail explicitly; no raw-wave substitution.
    /// Preparation may use the background executor; play on the application executor.</summary>
    public static Dolphin.Audio.Sound CreateMenuSound(FirmwareCatalog catalog,SystemMenuSound sound,
        CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        FirmwareMenuAudio.Validate(sound);
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)
            throw new global::System.ArgumentOutOfRangeException(nameof(nativeBudgetBytes));
        cancellationToken.ThrowIfCancellationRequested();
        var source=catalog.SnapshotMenuSound(sound,cancellationToken);
        try {return Dolphin.Audio.Sound.FromMenuRecipe(source,nativeBudgetBytes,cancellationToken);}
        finally {global::System.Array.Clear(source,0,source.Length);}
    }
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
        return CreateModelAsync(catalog,catalog.GetModelIndex(name,cancellationToken),cancellationToken,nativeBudgetBytes);
    }
    /// <summary>Lossless compressed stereo source. May validate on the background
    /// executor; playback must use the application executor. Cancellation releases native state.</summary>
    public static Dolphin.Audio.Sound CreateStartupSound(FirmwareCatalog catalog,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        cancellationToken.ThrowIfCancellationRequested();
        return Dolphin.Audio.Sound.FromEncoded(catalog.GetStartupAudio().ExtractAfc(),Dolphin.Audio.AudioFormat.Afc,nativeBudgetBytes:nativeBudgetBytes,cancellationToken:cancellationToken);
    }
    /// <summary>Create an owned original stereo source for a verified startup
    /// selection. Includes the original samples; ambience handover is separate.
    /// Requires IncludeSystemAudio. The existing CreateStartupSound is unchanged.</summary>
    public static Dolphin.Audio.Sound CreateStartupVariantSound(FirmwareCatalog catalog,SystemStartupSoundVariant variant,
        CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new global::System.ArgumentOutOfRangeException(nameof(nativeBudgetBytes));
        var packet=catalog.SnapshotStartupVariant(variant,cancellationToken);
        try{return Dolphin.Audio.Sound.FromEncoded(packet,Dolphin.Audio.AudioFormat.Afc,nativeBudgetBytes:nativeBudgetBytes,cancellationToken:cancellationToken);}
        finally{global::System.Array.Clear(packet,0,packet.Length);}
    }
    /// <summary>The verified paired startup source through Music's normal
    /// ownership and explicit Play lifetime. Requires IncludeSystemAudio.</summary>
    public static Dolphin.Audio.Music CreateStartupVariantMusic(FirmwareCatalog catalog,SystemStartupSoundVariant variant,
        CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        if(catalog==null)throw new global::System.ArgumentNullException(nameof(catalog));
        if(nativeBudgetBytes<1024||nativeBudgetBytes>16*1024*1024)throw new global::System.ArgumentOutOfRangeException(nameof(nativeBudgetBytes));
        var packet=catalog.SnapshotStartupVariant(variant,cancellationToken);
        try{return Dolphin.Audio.Music.FromEncoded(packet,Dolphin.Audio.AudioFormat.Afc,nativeBudgetBytes:nativeBudgetBytes,cancellationToken:cancellationToken);}
        finally{global::System.Array.Clear(packet,0,packet.Length);}
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
