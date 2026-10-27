using System;

internal static class BenchmarkSource
{
    internal static void Fill(byte[] pixels, int id, int group)
    {
        int width = group * 32, ox = (id % (48 / group)) * group, oy = (id / (48 / group)) * group;
        for (int y = 0; y < width; y++) for (int x = 0; x < width; x++)
        {
            int cx = ox + x / 32, cy = oy + y / 32, lx = x % 32, ly = y % 32;
            bool bright = (((lx / 8) ^ (ly / 8) ^ (cx * 3 + cy)) & 1) != 0;
            int at = (y * width + x) * 4;
            pixels[at] = (byte)TextureSource.Red(cx, bright);
            pixels[at + 1] = (byte)TextureSource.Green(cy, bright);
            pixels[at + 2] = (byte)(lx == 0 || ly == 0 ? 245 : bright ? 184 : 84);
            pixels[at + 3] = 255;
        }
    }
}
