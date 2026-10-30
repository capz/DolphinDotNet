using System;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Standalone Nintendo BTI image decoder, including indexed and CMPR GX tiles.</summary>
public sealed class BtiTexture
{
    public int Width { get; }
    public int Height { get; }
    public int ImageFormat { get; }
    public int PaletteFormat { get; }
    public int MipCount { get; }
    public byte WrapS { get; }
    public byte WrapT { get; }
    public byte MinFilter { get; }
    public byte MagFilter { get; }
    public byte[] Rgba { get; }

    private BtiTexture(int width, int height, int format, int palette, int mips, byte wrapS, byte wrapT,
        byte minFilter, byte magFilter, byte[] rgba)
    {
        Width = width; Height = height; ImageFormat = format; PaletteFormat = palette; MipCount = mips;
        WrapS = wrapS; WrapT = wrapT; MinFilter = minFilter; MagFilter = magFilter; Rgba = rgba;
    }

    public static BtiTexture Read(byte[] data)
    {
        Check(data, 0, 0x20);
        if (data.Length > MaxAssetBytes) throw new InvalidDataException("BTI exceeds asset limit.");
        int fmt = data[0], width = U16(data, 2), height = U16(data, 4);
        int palFmt = data[9], palCount = U16(data, 0xA);
        int mips = data[0x18] == 0 ? 1 : data[0x18];
        if (width < 1 || height < 1 || width > 1024 || height > 1024)
            throw new InvalidDataException("BTI dimensions must be between 1 and 1024.");
        int tileW, tileH, tileBytes;
        switch (fmt)
        {
            case 0: case 8: tileW = 8; tileH = 8; tileBytes = 32; break;
            case 1: case 2: case 9: tileW = 8; tileH = 4; tileBytes = 32; break;
            case 3: case 4: case 5: case 10: tileW = 4; tileH = 4; tileBytes = 32; break;
            case 6: tileW = 4; tileH = 4; tileBytes = 64; break;
            case 14: tileW = 8; tileH = 8; tileBytes = 32; break;
            default: throw new NotSupportedException("Unsupported Nintendo GX texel format " + fmt);
        }
        long blocksWide = (width + tileW - 1) / tileW;
        long blocksHigh = (height + tileH - 1) / tileH;
        long imageBytes = blocksWide * blocksHigh * tileBytes;
        long imageStart = U32(data, 0x1C);
        Check(data, imageStart, imageBytes);
        bool indexed = fmt == 8 || fmt == 9 || fmt == 10;
        long paletteStart = U32(data, 0xC);
        if (indexed)
        {
            if (palFmt > 2 || palCount == 0 || palCount > 16384)
                throw new InvalidDataException("Invalid BTI palette.");
            Check(data, paletteStart, palCount * 2L);
        }
        var rgba = new byte[checked(width * height * 4)];
        for (int by = 0; by < blocksHigh; by++)
        for (int bx = 0; bx < blocksWide; bx++)
        {
            int block = (int)(imageStart + (by * blocksWide + bx) * tileBytes);
            for (int y = 0; y < tileH; y++)
            for (int x = 0; x < tileW; x++)
            {
                int px = bx * tileW + x, py = by * tileH + y;
                if (px >= width || py >= height) continue;
                int t = y * tileW + x;
                int dst = (py * width + px) * 4;
                int r = 0, g = 0, b = 0, a = 255, color = 0;
                switch (fmt)
                {
                    case 0:
                        r = g = b = Expand4((t & 1) == 0 ? data[block + t / 2] >> 4 : data[block + t / 2] & 15);
                        break;
                    case 1: r = g = b = data[block + t]; break;
                    case 2:
                        color = data[block + t]; a = Expand4(color >> 4); r = g = b = Expand4(color & 15); break;
                    case 3:
                        a = data[block + t * 2]; r = g = b = data[block + t * 2 + 1]; break;
                    case 4:
                        Decode565(U16(data, block + t * 2), out r, out g, out b); break;
                    case 5:
                        Decode5A3(U16(data, block + t * 2), out r, out g, out b, out a); break;
                    case 6:
                        a = data[block + t * 2]; r = data[block + t * 2 + 1];
                        g = data[block + 32 + t * 2]; b = data[block + 32 + t * 2 + 1]; break;
                    case 8:
                        color = (t & 1) == 0 ? data[block + t / 2] >> 4 : data[block + t / 2] & 15;
                        goto case 10;
                    case 9:
                        color = data[block + t]; goto case 10;
                    case 10:
                        if (fmt == 10) color = U16(data, block + t * 2) & 0x3FFF;
                        if (color >= palCount) throw new InvalidDataException("BTI palette index outside table.");
                        int raw = U16(data, paletteStart + color * 2L);
                        if (palFmt == 0) { a = raw >> 8; r = g = b = raw & 255; }
                        else if (palFmt == 1) Decode565(raw, out r, out g, out b);
                        else Decode5A3(raw, out r, out g, out b, out a);
                        break;
                    case 14:
                        int quad = (y / 4 * 2 + x / 4) * 8;
                        int sub = block + quad;
                        int c0 = U16(data, sub), c1 = U16(data, sub + 2);
                        int code = (data[sub + 4 + y % 4] >> (6 - (x % 4) * 2)) & 3;
                        int r0, g0, b0, r1, g1, b1;
                        Decode565(c0, out r0, out g0, out b0);
                        Decode565(c1, out r1, out g1, out b1);
                        if (code == 0) { r = r0; g = g0; b = b0; }
                        else if (code == 1) { r = r1; g = g1; b = b1; }
                        else if (code == 2)
                        {
                            int w = c0 > c1 ? 2 : 1, d = c0 > c1 ? 3 : 2;
                            r = (r0 * w + r1) / d; g = (g0 * w + g1) / d; b = (b0 * w + b1) / d;
                        }
                        else if (c0 > c1) { r = (r0 + 2 * r1) / 3; g = (g0 + 2 * g1) / 3; b = (b0 + 2 * b1) / 3; }
                        else { a = 0; }
                        break;
                }
                rgba[dst] = (byte)r; rgba[dst + 1] = (byte)g;
                rgba[dst + 2] = (byte)b; rgba[dst + 3] = (byte)a;
            }
        }
        return new BtiTexture(width, height, fmt, palFmt, mips, data[6], data[7],
            data[0x14], data[0x15], rgba);
    }

    /// <summary>Build an immutable DNTX v2 package directly from the source's GX
    /// tiled bytes (including all mip levels). Indexed CI formats return null
    /// because the native DNTX sampler has no palette table.</summary>
    public static byte[]? CreateNativePackage(byte[] data)
    {
        var image = Read(data);
        int fmt = image.ImageFormat;
        // DNTX does not accept palette indices; convert each indexed mip into
        // GX-tiled RGBA8 instead of discarding the source mip chain.
        if (fmt == 8 || fmt == 9 || fmt == 10)
            return CreateIndexedMipPackage(data, image);
        // Non-power-of-two BTIs are valid retail assets, but DNTX mip packages
        // require power-of-two dimensions; callers use the RGBA base fallback.
        if (!PowerOfTwo(image.Width) || !PowerOfTwo(image.Height)) return null;
        int mips = image.MipCount;
        if (mips < 1 || mips > 11) throw new InvalidDataException("Invalid BTI mip count.");
        int tileW, tileH, tileBytes;
        switch (fmt)
        {
            case 0: tileW = 8; tileH = 8; tileBytes = 32; break;
            case 1: case 2: tileW = 8; tileH = 4; tileBytes = 32; break;
            case 3: case 4: case 5: tileW = 4; tileH = 4; tileBytes = 32; break;
            case 6: tileW = 4; tileH = 4; tileBytes = 64; break;
            case 14: tileW = 8; tileH = 8; tileBytes = 32; break;
            default: return null;
        }
        long start = U32(data, 0x1C);
        int width = image.Width, height = image.Height;
        int payload = 0;
        for (int level = 0; level < mips; level++)
        {
            int byteCount = checked((int)((long)((width + tileW - 1) / tileW) *
                    ((height + tileH - 1) / tileH) * tileBytes));
            Check(data, start + payload, byteCount);
            payload = checked(payload + byteCount);
            if (level + 1 < mips && width == 1 && height == 1)
                throw new InvalidDataException("BTI has more mip levels than image dimensions allow.");
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        if (payload > MaxAssetBytes - 32)
            throw new InvalidDataException("BTI texture package exceeds budget.");
        var output = new byte[payload + 32];
        output[0] = (byte)'D'; output[1] = (byte)'N';
        output[2] = (byte)'T'; output[3] = (byte)'X';
        WriteLittle32(output, 4, 2); WriteLittle32(output, 8, fmt);
        WriteLittle32(output, 12, image.Width); WriteLittle32(output, 16, image.Height);
        WriteLittle32(output, 20, payload); WriteLittle32(output, 24, mips);
        Buffer.BlockCopy(data, (int)start, output, 32, payload);
        return output;
    }

    private static bool PowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static void WriteBig16(byte[] destination, int offset, int value)
    {
        destination[offset] = (byte)(value >> 8);
        destination[offset + 1] = (byte)value;
    }

    private static void WriteBig32(byte[] destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }

    /// <summary>Preserve each indexed CI4/CI8/CI14X2 level by expanding it into
    /// a native GX RGBA8 4x4-tiled mip chain, with the original BTI palette.</summary>
    private static byte[]? CreateIndexedMipPackage(byte[] source, BtiTexture image)
    {
        if (!PowerOfTwo(image.Width) || !PowerOfTwo(image.Height)) return null;
        int tilesW = image.ImageFormat == 8 ? 8 : image.ImageFormat == 9 ? 8 : 4;
        int tilesH = image.ImageFormat == 8 ? 8 : image.ImageFormat == 9 ? 4 : 4;
        int mips = image.MipCount;
        if (mips < 1 || mips > 11) throw new InvalidDataException("Invalid indexed BTI mip count.");
        int width = image.Width, height = image.Height;
        long sourceStart = U32(source, 0x1C);
        int sourceBytes = 0, destinationBytes = 0;
        for (int i = 0; i < mips; i++)
        {
            int sourceLength = checked(((width + tilesW - 1) / tilesW) *
                ((height + tilesH - 1) / tilesH) * 32);
            Check(source, sourceStart + sourceBytes, sourceLength);
            sourceBytes = checked(sourceBytes + sourceLength);
            int nativeLength = checked(((width + 3) / 4) * ((height + 3) / 4) * 64);
            destinationBytes = checked(destinationBytes + nativeLength);
            if (destinationBytes > MaxAssetBytes - 32)
                throw new InvalidDataException("Indexed BTI mip chain exceeds memory budget.");
            if (i + 1 < mips && width == 1 && height == 1)
                throw new InvalidDataException("Invalid indexed BTI mip count for source dimensions.");
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        byte[] output = new byte[destinationBytes + 32];
        output[0] = (byte)'D'; output[1] = (byte)'N';
        output[2] = (byte)'T'; output[3] = (byte)'X';
        WriteLittle32(output, 4, 2); WriteLittle32(output, 8, 6);
        WriteLittle32(output, 12, image.Width); WriteLittle32(output, 16, image.Height);
        WriteLittle32(output, 20, destinationBytes); WriteLittle32(output, 24, mips);

        // Header offset changes only in this detached copy, never in caller
        // input. Palette metadata stays stable across all mip levels.
        byte[] detached = (byte[])source.Clone();
        width = image.Width; height = image.Height;
        int srcOffset = 0, dstOffset = 32;
        for (int level = 0; level < mips; level++)
        {
            WriteBig16(detached, 2, width); WriteBig16(detached, 4, height);
            WriteBig32(detached, 0x1C, (uint)(sourceStart + srcOffset));
            BtiTexture decoded = Read(detached);
            byte[] pixels = decoded.Rgba;
            int cols = (width + 3) / 4, rows = (height + 3) / 4;
            for (int by = 0; by < rows; by++)
            for (int bx = 0; bx < cols; bx++)
            {
                int tile = dstOffset + (by * cols + bx) * 64;
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int px = bx * 4 + x, py = by * 4 + y;
                    int element = (y * 4 + x) * 2;
                    if (px >= width || py >= height) continue;
                    int sourcePixel = (py * width + px) * 4;
                    output[tile + element] = pixels[sourcePixel + 3];   // A
                    output[tile + element + 1] = pixels[sourcePixel];   // R
                    output[tile + 32 + element] = pixels[sourcePixel + 1]; // G
                    output[tile + 32 + element + 1] = pixels[sourcePixel + 2]; // B
                }
            }
            dstOffset = checked(dstOffset + cols * rows * 64);
            srcOffset = checked(srcOffset + ((width + tilesW - 1) / tilesW) *
                ((height + tilesH - 1) / tilesH) * 32);
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        return output;
    }

    private static void WriteLittle32(byte[] destination, int offset, int value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        destination[offset + 3] = (byte)(value >> 24);
    }

    private static int Expand4(int v) => (v << 4) | v;
    private static int Expand5(int v) => (v << 3) | (v >> 2);
    private static int Expand6(int v) => (v << 2) | (v >> 4);
    private static void Decode565(int raw, out int r, out int g, out int b)
    {
        r = Expand5((raw >> 11) & 31); g = Expand6((raw >> 5) & 63); b = Expand5(raw & 31);
    }
    private static void Decode5A3(int raw, out int r, out int g, out int b, out int a)
    {
        if ((raw & 0x8000) != 0)
        {
            a = 255; r = Expand5((raw >> 10) & 31);
            g = Expand5((raw >> 5) & 31); b = Expand5(raw & 31);
        }
        else
        {
            a = (raw >> 12 & 7) * 255 / 7;
            r = Expand4((raw >> 8) & 15); g = Expand4((raw >> 4) & 15); b = Expand4(raw & 15);
        }
    }
}
