using System;
using System.IO;

// A bounded procedural source: actual CPU work on the worker, not disk/ARAM I/O.
// The caller retains one scratch array until the task completes and GX copies it.
internal static class TextureSource
{
    internal const int WorldWidth = 48, WorldHeight = 32, CellPixels = 32, Group = 4;
    internal static int Red(int x, bool bright) => bright ? 45 + (x % 12) * 12 : 18 + (x % 12) * 8;
    internal static int Green(int y, bool bright) => bright ? 65 + (y % 12) * 11 : 26 + (y % 12) * 8;
    internal static void Fill(byte[] pixels, int id, bool atlas, bool fail)
    {
        if (fail) throw new IOException("Injected procedural-source failure.");
        int cells = atlas ? Group : 1, width = cells * CellPixels;
        int originX = atlas ? (id % (WorldWidth / Group)) * Group : id % WorldWidth;
        int originY = atlas ? (id / (WorldWidth / Group)) * Group : id / WorldWidth;
        for (int y = 0; y < width; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cx = originX + x / CellPixels, cy = originY + y / CellPixels;
                int localX = x % CellPixels, localY = y % CellPixels;
                bool bright = (((localX / 8) ^ (localY / 8) ^ (cx * 3 + cy)) & 1) != 0;
                int at = (y * width + x) * 4;
                pixels[at] = (byte)Red(cx, bright);
                pixels[at + 1] = (byte)Green(cy, bright);
                pixels[at + 2] = (byte)(localX == 0 || localY == 0 ? 245 : bright ? 184 : 84);
                pixels[at + 3] = 255;
            }
        }
    }
    internal static byte[] Coarse()
    {
        var pixels = new byte[WorldWidth * WorldHeight * 4];
        for (int y = 0; y < WorldHeight; y++) for (int x = 0; x < WorldWidth; x++)
        {
            int at = (y * WorldWidth + x) * 4;
            pixels[at] = (byte)((Red(x, false) + Red(x, true)) / 2);
            pixels[at + 1] = (byte)((Green(y, false) + Green(y, true)) / 2);
            pixels[at + 2] = 134; pixels[at + 3] = 255;
        }
        return pixels;
    }
    internal static bool Matches(byte[] pixels, int id, bool atlas)
    {
        int cells = atlas ? Group : 1, width = cells * CellPixels;
        int ox = atlas ? (id % (WorldWidth / Group)) * Group : id % WorldWidth;
        int oy = atlas ? (id / (WorldWidth / Group)) * Group : id / WorldWidth;
        for (int pixel = 0; pixel < width * width; pixel++)
        {
            int x = pixel % width, y = pixel / width, at = pixel * 4;
            int cx = ox + x / CellPixels, cy = oy + y / CellPixels;
            bool bright = (((x % CellPixels / 8) ^ (y % CellPixels / 8) ^ (cx * 3 + cy)) & 1) != 0;
            int blue = x % CellPixels == 0 || y % CellPixels == 0 ? 245 : bright ? 184 : 84;
            if (pixels[at] != Red(cx, bright) || pixels[at + 1] != Green(cy, bright) ||
                pixels[at + 2] != blue || pixels[at + 3] != 255) return false;
        }
        return true;
    }
}
