using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Formats;

namespace Dolphin.Graphics;

public sealed partial class Texture2D
{
    /// <summary>Decode a standalone Nintendo BTI image into GX-ready RGBA8.
    /// Supports all eleven standard formats and indexed palettes. The base mip level
    /// is uploaded; original wrap/filter/LOD state is not reproduced.</summary>
    public static Texture2D FromBti(byte[] data, bool linear = false)
    {
        var image = BtiTexture.Read(data);
        if (image.Width < 4 || image.Height < 4)
            throw new InvalidDataException("Native Texture2D requires at least 4x4 pixels.");
        return Create(image.Width, image.Height, image.Rgba, linear);
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
