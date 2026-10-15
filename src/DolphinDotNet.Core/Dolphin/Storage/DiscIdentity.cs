using System;

namespace Dolphin.Storage;

/// <summary>Language record within a European BNR2 banner (BNR1 has one record).</summary>
public enum DiscBannerLanguage
{
    English = 0, German = 1, French = 2, Spanish = 3, Italian = 4, Dutch = 5
}

/// <summary>Decoded metadata and a 96x32 banner from the game's opening.bnr file.
/// Strings are decoded as Latin-1 for western releases. Japanese BNR1 titles
/// are encoded in Shift-JIS; use RawData to access their original text bytes.</summary>
public sealed class DiscBanner
{
    public const int Width = 96;
    public const int Height = 32;
    public string ShortTitle { get; }
    public string Title { get; }
    public string ShortPublisher { get; }
    public string Publisher { get; }
    public string Description { get; }
    public DiscBannerLanguage Language { get; }
    public bool IsMultilingual { get; }

    private readonly byte[] original;
    private readonly byte[] rgb5a3;
    private readonly byte[] rgba32;
    /// <summary>Defensive copy of the original BNR1/BNR2 payload.</summary>
    public byte[] RawData => Copy(original);
    /// <summary>Original 6144-byte GX 4x4-tiled RGB5A3 pixels, suitable for native GX.</summary>
    public byte[] Rgb5A3 => Copy(rgb5a3);
    /// <summary>Decoded 96x32 linear RGBA8888 pixels; can be used in Texture2D.Create.</summary>
    public byte[] Rgba32 => Copy(rgba32);

    private DiscBanner(byte[] raw, DiscBannerLanguage language, bool multilingual)
    {
        original = Copy(raw);
        Language = language;
        IsMultilingual = multilingual;
        int start = 0x1820 + (multilingual ? (int)language * 0x140 : 0);
        ShortTitle = Text(raw, start, 32);
        ShortPublisher = Text(raw, start + 32, 32);
        Title = Text(raw, start + 64, 64);
        Publisher = Text(raw, start + 128, 64);
        Description = Text(raw, start + 192, 128);
        rgb5a3 = new byte[6144];
        Array.Copy(raw, 0x20, rgb5a3, 0, rgb5a3.Length);
        rgba32 = DecodePixels(rgb5a3);
    }

    /// <summary>Parse an opening.bnr payload. Returns null for an invalid or
    /// truncated banner, or when no corresponding language record exists.</summary>
    public static DiscBanner? TryParse(byte[]? data, DiscBannerLanguage language = DiscBannerLanguage.English)
    {
        if (data == null || (int)language < 0 || (int)language > 5 || data.Length < 0x1960)
            return null;
        bool bnr1 = data[0] == 0x42 && data[1] == 0x4e && data[2] == 0x52 && data[3] == 0x31;
        bool bnr2 = data[0] == 0x42 && data[1] == 0x4e && data[2] == 0x52 && data[3] == 0x32;
        if ((!bnr1 && !bnr2) || (bnr2 && data.Length < 0x1fa0))
            return null;
        return new DiscBanner(data, bnr2 ? language : DiscBannerLanguage.English, bnr2);
    }

    private static byte[] Copy(byte[] src)
    {
        var result = new byte[src.Length];
        Array.Copy(src, result, src.Length);
        return result;
    }

    private static string Text(byte[] data, int offset, int capacity)
    {
        int count = 0;
        while (count < capacity && data[offset + count] != 0) count++;
        while (count > 0 && data[offset + count - 1] == 0x20) count--;
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = (char)data[offset + i]; // western banner text: Latin-1 bytes
        return new string(chars);
    }

    private static int Extend5(int value) => (value << 3) | (value >> 2);
    private static int Extend4(int value) => (value << 4) | value;
    private static byte[] DecodePixels(byte[] src)
    {
        var pixels = new byte[Width * Height * 4];
        int cursor = 0;
        for (int tileY = 0; tileY < Height; tileY += 4)
            for (int tileX = 0; tileX < Width; tileX += 4)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int value = (src[cursor] << 8) | src[cursor + 1];
                        cursor += 2;
                        int r, g, b, a;
                        if ((value & 0x8000) != 0)
                        {
                            r = Extend5((value >> 10) & 31);
                            g = Extend5((value >> 5) & 31);
                            b = Extend5(value & 31);
                            a = 255;
                        }
                        else
                        {
                            a = Extend3((value >> 12) & 7);
                            r = Extend4((value >> 8) & 15);
                            g = Extend4((value >> 4) & 15);
                            b = Extend4(value & 15);
                        }
                        int p = ((tileY + y) * Width + tileX + x) * 4;
                        pixels[p] = (byte)r;
                        pixels[p + 1] = (byte)g;
                        pixels[p + 2] = (byte)b;
                        pixels[p + 3] = (byte)a;
                    }
        return pixels;
    }
    private static int Extend3(int value) => (value << 5) | (value << 2) | (value >> 1);
}

/// <summary>A stable snapshot of the mounted GameCube game disc boot header and
/// its optional on-disc opening.bnr metadata. Works for physical/ODE media and
/// mounted ISO/GCM files using the same dvd:/ filesystem.</summary>
public sealed class DiscIdentity
{
    public const int HeaderSize = 0x440;
    private readonly byte[] rawHeader;
    public string GameCode { get; }
    public string MakerCode { get; }
    /// <summary>Six-character on-disc ID: four-character game code + two-character maker code.</summary>
    public string GameId => GameCode + MakerCode;
    /// <summary>Zero-based disc index (0 indicates disc one).</summary>
    public int DiscNumber { get; }
    public int Revision { get; }
    public bool AudioStreaming { get; }
    public int StreamBufferSize { get; }
    /// <summary>Internal header name (ASCII-compatible display; Japanese text
    /// may require decoding the raw bytes at 0x20 using Shift-JIS).</summary>
    public string InternalName { get; }
    /// <summary>Selected on-disc BNR1/BNR2 language record, or null if missing/invalid.</summary>
    public DiscBanner? Banner { get; }
    /// <summary>Full banner title; falls back to short title and then internal header name.</summary>
    public string Title => Banner == null ? InternalName :
        Banner.Title.Length != 0 ? Banner.Title :
        Banner.ShortTitle.Length != 0 ? Banner.ShortTitle : InternalName;
    public string ShortTitle => Banner == null ? InternalName : Banner.ShortTitle;
    /// <summary>Game description from opening.bnr (blank if no valid banner).</summary>
    public string ShortDescription => Banner == null ? "" : Banner.Description;
    public string Publisher => Banner == null ? "" : Banner.Publisher;
    /// <summary>Defensive copy of the full 0x440-byte GameCube boot header.</summary>
    public byte[] RawHeader
    {
        get
        {
            var result = new byte[rawHeader.Length];
            Array.Copy(rawHeader, result, result.Length);
            return result;
        }
    }

    private DiscIdentity(byte[] header, DiscBanner? banner)
    {
        rawHeader = new byte[HeaderSize];
        Array.Copy(header, rawHeader, HeaderSize);
        GameCode = Printable(header, 0, 4);
        MakerCode = Printable(header, 4, 2);
        DiscNumber = header[6];
        Revision = header[7];
        AudioStreaming = header[8] != 0;
        StreamBufferSize = header[9];
        int count = 0;
        while (count < 0x3e0 && header[0x20 + count] != 0) count++;
        InternalName = Printable(header, 0x20, count);
        Banner = banner;
    }

    /// <summary>Useful for inspecting the first 0x440 bytes of an unmounted ISO
    /// as well as the mounted device. Banner data must be loaded separately.</summary>
    public static DiscIdentity ParseHeader(byte[] header) => ParseHeader(header, null);
    public static DiscIdentity ParseHeader(byte[] header, DiscBanner? banner)
    {
        if (header == null) throw new ArgumentNullException("header");
        if (header.Length < HeaderSize)
            throw new ArgumentException("GameCube header is shorter than 0x440 bytes.", "header");
        if (header[0x1c] != 0xc2 || header[0x1d] != 0x33 ||
            header[0x1e] != 0x9f || header[0x1f] != 0x3d)
            throw new ArgumentException("Not an original GameCube disc header.", "header");
        return new DiscIdentity(header, banner);
    }

    private static string Printable(byte[] data, int offset, int count)
    {
        var chars = new char[count];
        for (int i = 0; i < count; i++)
        {
            byte value = data[offset + i];
            chars[i] = value >= 32 && value <= 126 ? (char)value : '?';
        }
        return new string(chars);
    }
}
