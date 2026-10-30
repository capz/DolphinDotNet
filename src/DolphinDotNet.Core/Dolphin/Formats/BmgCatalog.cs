using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Generic big-endian MESGbmg1 INF1/DAT1/MID1 reader. Game-specific
/// INF1 attributes are returned unchanged rather than assigned a guessed schema.
/// String control tags remain byte-exact and are not interpreted as commands.</summary>
public sealed class BmgCatalog
{
    public sealed class ControlTag
    {
        public byte Group { get; }
        public ushort Number { get; }
        public byte[] RawBytes { get; }
        internal ControlTag(byte[] source, int position, int count)
        {
            Group = source[position + 2];
            Number = U16(source, position + 3);
            RawBytes = Slice(source, position, count);
        }
    }

    public sealed class Entry
    {
        public int Index { get; }
        public uint Id { get; }
        public byte[] Attributes { get; }
        public byte[] RawText { get; }
        public string EscapedText { get; }
        public IReadOnlyList<ControlTag> Tags => tags;
        private readonly List<ControlTag> tags;
        internal Entry(int index, uint id, byte[] attributes, byte[] text, byte encoding)
        {
            Index = index; Id = id; Attributes = attributes; RawText = text;
            tags = new List<ControlTag>();
            EscapedText = Format(text, encoding, tags);
        }
    }

    public byte EncodingCode { get; }
    private readonly List<Entry> entries = new List<Entry>();
    public IReadOnlyList<Entry> Entries => entries;

    private BmgCatalog(byte encoding) { EncodingCode = encoding; }

    public static BmgCatalog Read(byte[] input)
    {
        byte[] data = UnwrapYaz0(input);
        Check(data, 0, 0x20);
        if (Ascii(data, 0, 8) != "MESGbmg1")
            throw new InvalidDataException("Not a big-endian MESGbmg1.");
        uint declared = U32(data, 8), count = U32(data, 0xC);
        byte encoding = data[0x10];
        if (declared < 0x20 || declared > data.Length || count < 2 || count > 32)
            throw new InvalidDataException("Invalid BMG length or section count.");
        if (encoding > 4) throw new NotSupportedException("Unknown BMG text encoding.");
        long inf = -1, dat = -1, mid = -1;
        long infLength = 0, datLength = 0, midLength = 0;
        long cursor = 0x20;
        for (int i = 0; i < count; i++)
        {
            if (cursor + 8 > data.Length)
                throw new InvalidDataException("Truncated BMG section.");
            uint size = U32(data, cursor + 4);
            // Some BMGs declare a partial file size excluding FLW/FLI sections.
            // Still require all declared sections to remain inside the input.
            if (size < 8 || (long)size > data.Length - cursor)
                throw new InvalidDataException("BMG section outside file.");
            string kind = Ascii(data, cursor, 4);
            switch (kind)
            {
                case "INF1":
                    if (inf >= 0) throw new InvalidDataException("Duplicate BMG INF1.");
                    inf = cursor; infLength = size; break;
                case "DAT1":
                    if (dat >= 0) throw new InvalidDataException("Duplicate BMG DAT1.");
                    dat = cursor; datLength = size; break;
                case "MID1":
                    if (mid >= 0) throw new InvalidDataException("Duplicate BMG MID1.");
                    mid = cursor; midLength = size; break;
            }
            cursor += size;
        }
        if (inf < 0 || dat < 0 || infLength < 0x10 || datLength < 8)
            throw new InvalidDataException("Missing BMG message sections.");
        int messages = U16(data, inf + 8), stride = U16(data, inf + 0xA);
        if (stride < 4 || stride > 1024 || 0x10L + messages * (long)stride > infLength)
            throw new InvalidDataException("Invalid BMG INF1 record size.");
        if (mid >= 0 && (midLength < 0x10 || U16(data, mid + 8) != messages ||
                         0x10L + messages * 4L > midLength))
            throw new InvalidDataException("BMG MID1 count does not match INF1.");
        var result = new BmgCatalog(encoding);
        var seen = new HashSet<uint>();
        for (int i = 0; i < messages; i++)
        {
            long info = inf + 0x10L + i * (long)stride;
            uint id = mid >= 0 ? U32(data, mid + 0x10L + i * 4L) : (uint)i;
            if (!seen.Add(id)) throw new InvalidDataException("Duplicate BMG message ID.");
            long start = dat + 8L + U32(data, info);
            long end = dat + datLength;
            if (start >= end || start < dat + 8)
                throw new InvalidDataException("Message text offset outside DAT1.");
            long pos = start;
            while (true)
            {
                if (pos - start > 65536 || pos >= end)
                    throw new InvalidDataException("Unterminated or oversized BMG message.");
                if (encoding == 2)
                {
                    if (pos + 1 >= end)
                        throw new InvalidDataException("Truncated UTF-16BE BMG string.");
                    int character = U16(data, pos);
                    if (character == 0) break;
                    if (character == 0x1A)
                    {
                        // In UTF-16BE the substitute character starts with 00,
                        // while the tag itself begins at the following 1A byte.
                        int packet = (int)pos + 1;
                        ValidateTag(data, packet, end);
                        pos = packet + data[packet + 1];
                    }
                    else pos += 2;
                }
                else
                {
                    int current = data[(int)pos];
                    if (current == 0) break;
                    if (current == 0x1A)
                    {
                        ValidateTag(data, (int)pos, end);
                        pos += data[(int)pos + 1];
                    }
                    else pos++;
                }
            }
            result.entries.Add(new Entry(i, id, Slice(data, info + 4, stride - 4),
                Slice(data, start, pos - start), encoding));
        }
        return result;
    }

    private static void ValidateTag(byte[] data, int start, long end)
    {
        if ((long)start + 2 > end || data[start] != 0x1A)
            throw new InvalidDataException("Truncated BMG tag header.");
        int size = data[start + 1];
        if (size < 5 || (long)start + size > end)
            throw new InvalidDataException("Invalid or truncated BMG tag.");
    }

    private static string Format(byte[] bytes, byte encoding, List<ControlTag> tags)
    {
        var result = new StringBuilder();
        int start = 0;
        for (int i = 0; i < bytes.Length;)
        {
            bool utf16 = encoding == 2;
            int tagPosition = utf16 && i + 1 < bytes.Length &&
                bytes[i] == 0 && bytes[i + 1] == 0x1A ? i + 1 :
                !utf16 && bytes[i] == 0x1A ? i : -1;
            if (tagPosition >= 0)
            {
                AppendPlain(result, bytes, start, i - start, encoding);
                ValidateTag(bytes, tagPosition, bytes.Length);
                int length = bytes[tagPosition + 1];
                tags.Add(new ControlTag(bytes, tagPosition, length));
                result.Append(@"\{");
                for (int j = 0; j < length; j++)
                {
                    if (j != 0) result.Append(' ');
                    result.Append(bytes[tagPosition + j].ToString("X2"));
                }
                result.Append('}');
                i = tagPosition + length;
                start = i;
            }
            else i += utf16 ? 2 : 1;
        }
        AppendPlain(result, bytes, start, bytes.Length - start, encoding);
        return result.ToString();
    }

    private static void AppendPlain(StringBuilder output, byte[] data, int offset,
        int count, byte encoding)
    {
        if (count == 0) return;
        if (encoding == 2)
        {
            if ((count & 1) != 0) throw new InvalidDataException("Misaligned UTF-16BE text.");
            for (int i = offset; i < offset + count; i += 2)
            {
                int cp = U16(data, i);
                if (cp >= 0xD800 && cp <= 0xDBFF)
                {
                    if (i + 3 >= offset + count)
                        throw new InvalidDataException("Unpaired BMG UTF-16 surrogate.");
                    int low = U16(data, i + 2);
                    if (low < 0xDC00 || low > 0xDFFF)
                        throw new InvalidDataException("Invalid BMG UTF-16 surrogate.");
                    output.Append((char)cp).Append((char)low);
                    i += 2;
                }
                else if (cp >= 0xDC00 && cp <= 0xDFFF)
                    throw new InvalidDataException("Unpaired BMG UTF-16 surrogate.");
                else output.Append((char)cp);
            }
        }
        else if (encoding == 3)
        {
            output.Append(ShiftJisCodec.Decode(data, offset, count));
        }
        else if (encoding == 4)
        {
            try { output.Append(new UTF8Encoding(false, true).GetString(data, offset, count)); }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("Invalid UTF-8 BMG message payload.", error);
            }
        }
        else
        {
            // Single-byte legacy profiles preserve unassigned byte escapes.
            for (int i = offset; i < offset + count; i++)
            {
                int c = data[i];
                if (c >= 0x20 && c <= 0x7E && c != '\\') output.Append((char)c);
                else if (c == '\n') output.Append('\n');
                else if (encoding <= 1 && c >= 0xA0) output.Append((char)c);
                else if (encoding <= 1 && c >= 0x80 && c <= 0x9F)
                {
                    char decoded = Cp1252Extended(c);
                    if (decoded == 0) output.Append(@"\x").Append(c.ToString("X2"));
                    else output.Append(decoded);
                }
                else output.Append(@"\x").Append(c.ToString("X2"));
            }
        }
    }

    private static char Cp1252Extended(int code)
    {
        // Undefined Windows-1252 byte values remain escaped, not U+FFFD.
        const string upper = "\u20AC\0\u201A\u0192\u201E\u2026\u2020\u2021" +
            "\u02C6\u2030\u0160\u2039\u0152\0\u017D\0" +
            "\0\u2018\u2019\u201C\u201D\u2022\u2013\u2014" +
            "\u02DC\u2122\u0161\u203A\u0153\0\u017E\u0178";
        return upper[code - 0x80];
    }

}
