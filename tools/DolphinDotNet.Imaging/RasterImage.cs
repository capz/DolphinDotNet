using SkiaSharp;

namespace DolphinDotNet.Imaging;

public enum ResizeMode { Pad, Crop, Stretch }

public readonly record struct Rgba32(byte R, byte G, byte B, byte A);

/// <summary>Desktop-only bounded image decoding and cubic resizing for asset tools.</summary>
public sealed class RasterImage : IDisposable
{
    private const long MaxPixels = 16 * 1024 * 1024;
    private readonly SKBitmap bitmap;
    public int Width => bitmap.Width;
    public int Height => bitmap.Height;

    public RasterImage(int width, int height, Rgba32 fill = default)
    {
        ValidateSize(width, height);
        bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(new SKColor(fill.R, fill.G, fill.B, fill.A));
    }

    private RasterImage(SKBitmap bitmap) => this.bitmap = bitmap;

    public Rgba32 this[int x, int y]
    {
        get { ValidateCoordinates(x, y); var p = bitmap.GetPixel(x, y); return new(p.Red, p.Green, p.Blue, p.Alpha); }
        set { ValidateCoordinates(x, y); bitmap.SetPixel(x, y, new SKColor(value.R, value.G, value.B, value.A)); }
    }

    public static RasterImage Load(byte[] data, bool pngOnly = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > 64 * 1024 * 1024) throw new InvalidDataException("Encoded image exceeds the 64-MiB budget.");
        using var encoded = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(encoded) ?? throw new InvalidDataException("Invalid image.");
        if (pngOnly ? codec.EncodedFormat != SKEncodedImageFormat.Png :
            codec.EncodedFormat != SKEncodedImageFormat.Png && codec.EncodedFormat != SKEncodedImageFormat.Jpeg)
            throw new InvalidDataException(pngOnly ? "Input must be a PNG image." : "Textures must be PNG or JPEG.");
        if (codec.FrameCount > 1) throw new InvalidDataException("Animated images are unsupported.");
        ValidateSize(codec.Info.Width, codec.Info.Height);
        var result = new RasterImage(codec.Info.Width, codec.Info.Height);
        if (codec.GetPixels(result.bitmap.Info, result.bitmap.GetPixels()) != SKCodecResult.Success)
        {
            result.Dispose();
            throw new InvalidDataException("Incomplete or invalid image pixels.");
        }
        return result;
    }

    public RasterImage Resized(int width, int height, ResizeMode mode = ResizeMode.Stretch)
    {
        if (mode != ResizeMode.Pad && mode != ResizeMode.Crop && mode != ResizeMode.Stretch)
            throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateSize(width, height);
        if (width == Width && height == Height) return new RasterImage(bitmap.Copy());
        float scaleX = (float)width / Width, scaleY = (float)height / Height;
        if (mode != ResizeMode.Stretch)
        {
            float scale = mode == ResizeMode.Pad ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
            scaleX = scaleY = scale;
        }
        float drawWidth = Width * scaleX, drawHeight = Height * scaleY;
        var result = new RasterImage(width, height);
        using var canvas = new SKCanvas(result.bitmap);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect((width - drawWidth) / 2, (height - drawHeight) / 2,
            (width + drawWidth) / 2, (height + drawHeight) / 2), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();
        return result;
    }

    public void SaveAsPng(Stream stream)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        encoded.SaveTo(stream);
    }

    public void SaveAsPng(string path)
    {
        using var stream = File.Create(path);
        SaveAsPng(stream);
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            throw new InvalidDataException("Image dimensions exceed the 16-megapixel budget.");
    }

    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel coordinates must lie inside the image.");
    }

    public void Dispose() => bitmap.Dispose();
}
