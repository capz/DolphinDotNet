using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Formats;

namespace Dolphin.Graphics;

public sealed partial class Texture2D
{
    /// <summary>Load a standalone Nintendo BTI texture into the native GX backend.
    /// Native non-indexed payloads retain all mips. Indexed CI4/CI8/CI14X2 mips
    /// are converted to GX RGBA8 tiles, preserving the source mip chain.
    /// Non-power-of-two images use a base-level RGBA8 fallback.
    /// Original wrap, min/mag filter, and LOD bias state is not reproduced.</summary>
    public static Texture2D FromBti(byte[] data, bool linear = false)
    {
        var image = BtiTexture.Read(data);
        if (image.Width < 4 || image.Height < 4)
            throw new InvalidDataException("Native Texture2D requires at least 4x4 pixels.");
        var native = BtiTexture.CreateNativePackage(data);
        // Preserve all supported source mips when DNTX permits. Indexed
        // mips use RGBA8, while non-power-of-two textures fall back to base RGBA.
        return native != null ? FromPackage(native, linear) :
            Create(image.Width, image.Height, image.Rgba, linear);
    }

    /// <summary>Read a BTI from a mounted volume using bounded async storage I/O.</summary>
    public static async Task<Texture2D> LoadBtiAsync(string path,
        CancellationToken cancellationToken = default, bool linear = false)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        var bytes = await Dolphin.IO.File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return FromBti(bytes, linear);
    }
}
