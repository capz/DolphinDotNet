using System;
using System.Collections.Generic;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Read-only BFN font metrics and character mappings, not glyph rasterization.</summary>
public sealed class BfnFont
{
    public ushort EncodingType { get; private set; }
    public ushort Ascent { get; private set; }
    public ushort Descent { get; private set; }
    public ushort DefaultWidth { get; private set; }
    public ushort LineHeight { get; private set; }
    public ushort ReplacementGlyph { get; private set; }
    private readonly Dictionary<int, ushort> mappedGlyphs = new Dictionary<int, ushort>();
    private readonly Dictionary<int, byte> widths = new Dictionary<int, byte>();
    public IReadOnlyDictionary<int, ushort> CharacterMap => mappedGlyphs;

    public ushort GlyphForEncodedCharacter(int ordinal) =>
        mappedGlyphs.TryGetValue(ordinal, out ushort code) ? code : ReplacementGlyph;

    public int WidthForGlyph(int code) =>
        widths.TryGetValue(code, out byte width) ? width : DefaultWidth;

    public static BfnFont Read(byte[] input)
    {
        var data = UnwrapYaz0(input);
        if (Ascii(data, 0, 8) != "FONTbfn1") throw new InvalidDataException("Not a BFN font.");
        uint declared = U32(data, 8), chunks = U32(data, 0xC);
        if (declared < 0x20 || declared > data.Length || chunks > 512)
            throw new InvalidDataException("Invalid BFN header.");
        var font = new BfnFont();
        bool hasInfo = false;
        long offset = 0x20;
        for (int i = 0; i < chunks; i++)
        {
            if (offset + 8 > declared) throw new InvalidDataException("Truncated BFN chunk.");
            string type = Ascii(data, offset, 4);
            uint size = U32(data, offset + 4);
            if (size < 8 || size > declared - offset) throw new InvalidDataException("Invalid BFN chunk bounds.");
            if (type == "INF1")
            {
                if (size < 0x14 || hasInfo) throw new InvalidDataException("Invalid or duplicate BFN INF1.");
                hasInfo = true;
                font.EncodingType = U16(data, offset + 8);
                font.Ascent = U16(data, offset + 0xA);
                font.Descent = U16(data, offset + 0xC);
                font.DefaultWidth = U16(data, offset + 0xE);
                font.LineHeight = U16(data, offset + 0x10);
                font.ReplacementGlyph = U16(data, offset + 0x12);
            }
            else if (type == "MAP1")
            {
                if (size < 0x10) throw new InvalidDataException("Truncated BFN MAP1.");
                int kind = U16(data, offset + 8), first = U16(data, offset + 0xA),
                    last = U16(data, offset + 0xC), count = U16(data, offset + 0xE);
                if (last < first) throw new InvalidDataException("Invalid BFN map interval.");
                if (kind == 0)
                {
                    if (count != 0) throw new InvalidDataException("Invalid linear BFN map.");
                    for (int c = first; c <= last; c++) font.mappedGlyphs[c] = (ushort)(c - first);
                }
                else if (kind == 2)
                {
                    if (count != last - first + 1 || 0x10L + count * 2L > size)
                        throw new InvalidDataException("Invalid BFN table mapping.");
                    for (int j = 0; j < count; j++)
                        font.mappedGlyphs[first + j] = U16(data, offset + 0x10 + j * 2L);
                }
                else if (kind == 3)
                {
                    if (0x10L + count * 4L > size) throw new InvalidDataException("Invalid BFN pair mapping.");
                    for (int j = 0; j < count; j++)
                        font.mappedGlyphs[U16(data, offset + 0x10 + j * 4L)] =
                            U16(data, offset + 0x12 + j * 4L);
                }
                else if (kind == 1)
                {
                    // Nintendo Shift-JIS character-to-glyph index mapping.
                    if (count != 0) throw new InvalidDataException("Invalid BFN Shift-JIS map.");
                    for (int c = first; c <= last; c++)
                    {
                        int lead = (c >> 8) & 255, trail = c & 255, index = trail - 64;
                        if (index >= 64) index--;
                        int value = 796 + index + (lead - 136) * 188 - 94;
                        if (value >= 0 && value <= 65535) font.mappedGlyphs[c] = (ushort)value;
                    }
                }
                else throw new NotSupportedException("Unknown BFN mapping profile.");
            }
            else if (type == "WID1")
            {
                if (size < 0xC) throw new InvalidDataException("Truncated BFN WID1.");
                int first = U16(data, offset + 8), last = U16(data, offset + 0xA);
                if (last < first || 0xCL + (last - first) * 2L > size)
                    throw new InvalidDataException("Invalid BFN glyph widths.");
                for (int j = 0; j < last - first; j++)
                    font.widths[first + j] = data[(int)(offset + 0xD + j * 2L)];
            }
            // GLY1 is retained in the source but texture sheets are not decoded here.
            offset += size;
        }
        if (!hasInfo) throw new InvalidDataException("BFN INF1 is missing.");
        return font;
    }
}
