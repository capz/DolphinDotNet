using System;
using System.Collections.Generic;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>
/// BFN1 GLY1 glyph sheet metadata and GX texture decoding. This is not a
/// replacement for the device's UI/text renderer: it exposes original glyph
/// pixel sheets for explicit caller-owned texture creation.
/// </summary>
public sealed class BfnGlyphSheets
{
    public sealed class Sheet
    {
        private readonly byte[] pixels;
        public int Width { get; }
        public int Height { get; }
        public int ImageFormat { get; }
        public byte[] Rgba => (byte[])pixels.Clone();

        internal Sheet(byte[] data, int format, int width, int height)
        {
            Width = width; Height = height; ImageFormat = format;
            var bti = new byte[checked(0x20 + data.Length)];
            bti[0] = (byte)format;
            bti[2] = (byte)(width >> 8);
            bti[3] = (byte)width;
            bti[4] = (byte)(height >> 8);
            bti[5] = (byte)height;
            bti[0x18] = 1;
            bti[0x1F] = 0x20;
            Buffer.BlockCopy(data, 0, bti, 0x20, data.Length);
            pixels = BtiTexture.Read(bti).Rgba;
        }

        public byte[] CopyGlyph(int x, int y, int width, int height)
        {
            if (width < 1 || height < 1 || x < 0 || y < 0 ||
                x > Width - width || y > Height - height)
                throw new ArgumentOutOfRangeException(nameof(x), "Glyph bounds are outside the sheet.");
            var result = new byte[checked(width * height * 4)];
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(pixels, ((y + row) * Width + x) * 4,
                    result, row * width * 4, width * 4);
            return result;
        }
    }

    public readonly struct Glyph
    {
        public readonly int SheetIndex, X, Y, Width, Height;
        internal Glyph(int sheet, int x, int y, int w, int h)
        { SheetIndex = sheet; X = x; Y = y; Width = w; Height = h; }
    }

    private sealed class GlyphRange
    {
        public int First, Last, CellWidth, CellHeight, Rows, Columns, StartingSheet;
    }

    private readonly List<Sheet> sheets = new List<Sheet>();
    private readonly List<GlyphRange> ranges = new List<GlyphRange>();
    public IReadOnlyList<Sheet> Sheets => sheets;

    public bool TryGetGlyph(int glyphCode, out Glyph glyph)
    {
        foreach (var range in ranges)
        {
            if (glyphCode < range.First || glyphCode > range.Last) continue;
            int index = glyphCode - range.First;
            int cells = checked(range.Rows * range.Columns);
            int sheet = range.StartingSheet + index / cells;
            int local = index % cells;
            glyph = new Glyph(sheet, local % range.Columns * range.CellWidth,
                local / range.Columns * range.CellHeight, range.CellWidth, range.CellHeight);
            return true;
        }
        glyph = default;
        return false;
    }

    public byte[] CopyGlyph(int code)
    {
        if (!TryGetGlyph(code, out var glyph))
            throw new KeyNotFoundException("No BFN glyph for code " + code);
        return sheets[glyph.SheetIndex].CopyGlyph(glyph.X, glyph.Y, glyph.Width, glyph.Height);
    }

    public static BfnGlyphSheets Read(byte[] input)
    {
        byte[] data = UnwrapYaz0(input);
        if (Ascii(data, 0, 8) != "FONTbfn1") throw new InvalidDataException("Not a BFN1 font.");
        uint declared = U32(data, 8), count = U32(data, 0xC);
        if (declared < 0x20 || declared > data.Length || count > 512)
            throw new InvalidDataException("Invalid BFN header.");
        var result = new BfnGlyphSheets();
        long cursor = 0x20;
        long decodedBytes = 0;
        for (int i = 0; i < count; i++)
        {
            if (cursor + 8 > declared) throw new InvalidDataException("Truncated BFN chunk header.");
            string type = Ascii(data, cursor, 4);
            uint length = U32(data, cursor + 4);
            if (length < 8 || length > declared - cursor)
                throw new InvalidDataException("Invalid BFN chunk bounds.");
            if (type == "GLY1")
            {
                if (length < 0x20) throw new InvalidDataException("Truncated BFN GLY1 header.");
                int first = U16(data, cursor + 8), last = U16(data, cursor + 0xA);
                int cellW = U16(data, cursor + 0xC), cellH = U16(data, cursor + 0xE);
                uint sheetBytes = U32(data, cursor + 0x10);
                int format = data[(int)(cursor + 0x15)];
                int rows = U16(data, cursor + 0x16), columns = U16(data, cursor + 0x18);
                int width = U16(data, cursor + 0x1A), height = U16(data, cursor + 0x1C);
                if (first > last || cellW < 1 || cellH < 1 || rows < 1 || columns < 1 ||
                    width < 1 || height < 1 || width > 1024 || height > 1024 ||
                    ((width & (width - 1)) != 0) || ((height & (height - 1)) != 0) ||
                    (long)rows * cellH > height || (long)columns * cellW > width ||
                    sheetBytes == 0 || sheetBytes > MaxAssetBytes)
                    throw new InvalidDataException("BFN glyph dimensions or ranges are invalid.");
                int tileWidth, tileHeight, tileSize;
                switch (format)
                {
                    case 0: tileWidth = 8; tileHeight = 8; tileSize = 32; break;
                    case 1: case 2: tileWidth = 8; tileHeight = 4; tileSize = 32; break;
                    case 3: case 4: case 5: tileWidth = 4; tileHeight = 4; tileSize = 32; break;
                    case 6: tileWidth = 4; tileHeight = 4; tileSize = 64; break;
                    case 14: tileWidth = 8; tileHeight = 8; tileSize = 32; break;
                    default: throw new NotSupportedException("Indexed or unsupported BFN glyph image format.");
                }
                long expected = (long)((width + tileWidth - 1) / tileWidth) *
                    ((height + tileHeight - 1) / tileHeight) * tileSize;
                if (sheetBytes != expected) throw new InvalidDataException("BFN GLY1 sheet byte length mismatch.");
                long cells = (long)rows * columns;
                long sheetCount = ((long)last - first) / cells + 1;
                if (sheetCount > 256 || 0x20L + sheetCount * sheetBytes != length)
                    throw new InvalidDataException("BFN GLY1 sheet table is malformed.");
                for (int other = 0; other < result.ranges.Count; other++)
                    if (!(last < result.ranges[other].First || first > result.ranges[other].Last))
                        throw new InvalidDataException("Overlapping BFN GLY1 glyph ranges.");
                var range = new GlyphRange
                {
                    First = first, Last = last, CellWidth = cellW, CellHeight = cellH,
                    Rows = rows, Columns = columns, StartingSheet = result.sheets.Count
                };
                for (int sheet = 0; sheet < sheetCount; sheet++)
                {
                    decodedBytes += (long)width * height * 4;
                    if (decodedBytes > MaxAssetBytes)
                        throw new InvalidDataException("Decoded BFN sheets exceed memory budget.");
                    byte[] raw = Slice(data, cursor + 0x20L + sheet * sheetBytes, sheetBytes);
                    result.sheets.Add(new Sheet(raw, format, width, height));
                }
                result.ranges.Add(range);
            }
            cursor += length;
        }
        return result;
    }
}
