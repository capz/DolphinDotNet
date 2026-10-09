using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DolphinDotNet.SaveImage;

public static class Rgb5A3
{
    /// <summary>Encodes scanline pixels in GX 4x4 tile order, most significant byte first.</summary>
    public static byte[] Encode(Image<Rgba32> image)
    {
        if ((image.Width != 32 && image.Width != 96) || image.Height != 32)
            throw new ArgumentException("Save images must be 32x32 (icon) or 96x32 (banner).", nameof(image));
        var output = new byte[image.Width * image.Height * 2];
        int offset = 0;
        for (int tileY = 0; tileY < image.Height; tileY += 4)
            for (int tileX = 0; tileX < image.Width; tileX += 4)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        var pixel = image[tileX + x, tileY + y];
                        // Opaque: 1RRRRRGGGGGBBBBB. Translucent: 0AAARRRRGGGGBBBB.
                        // Use RGB555 only for fully opaque pixels so alpha survives conversion.
                        ushort packed = pixel.A == 255
                            ? (ushort)(0x8000 | (pixel.R >> 3) << 10 | (pixel.G >> 3) << 5 | pixel.B >> 3)
                            : (ushort)((pixel.A >> 5) << 12 | (pixel.R >> 4) << 8 | (pixel.G >> 4) << 4 | pixel.B >> 4);
                        output[offset++] = (byte)(packed >> 8);
                        output[offset++] = (byte)packed;
                    }
        return output;
    }
}
