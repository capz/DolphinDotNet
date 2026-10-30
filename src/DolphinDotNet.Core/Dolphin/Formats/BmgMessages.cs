using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Wind Waker MESGbmg1 messages. Retains raw text bytes and control codes.</summary>
public sealed class BmgMessages
{
    public sealed class Message
    {
        public ushort Id { get; }
        public ushort NextId { get; }
        public byte TextBoxType { get; }
        public byte[] RawText { get; }
        /// <summary>One-byte game characters with control codes represented as hex escapes.</summary>
        public string EscapedText { get; }
        internal Message(ushort id, ushort next, byte box, byte[] raw)
        {
            Id = id; NextId = next; TextBoxType = box; RawText = raw;
            var text = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                byte b = raw[i];
                if (b == 0x1A)
                {
                    if (i + 1 >= raw.Length || raw[i + 1] < 2 || i + raw[i + 1] > raw.Length)
                        throw new InvalidDataException("Truncated BMG control sequence.");
                    int size = raw[i + 1];
                    text.Append("\\{");
                    for (int j = 0; j < size; j++)
                    {
                        if (j != 0) text.Append(' ');
                        text.Append(raw[i + j].ToString("X2"));
                    }
                    text.Append('}');
                    i += size - 1;
                }
                else if (b >= 0x20 && b <= 0x7E && b != (byte)'\\') text.Append((char)b);
                else if (b == (byte)'\n') text.Append('\n');
                else text.Append("\\x").Append(b.ToString("X2"));
            }
            EscapedText = text.ToString();
        }
    }

    private readonly List<Message> messages = new List<Message>();
    public IReadOnlyList<Message> Messages => messages;
    public static async Task<BmgMessages> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        byte[] bytes = await Dolphin.IO.File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(bytes);
    }

    public static BmgMessages Read(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length > MaxAssetBytes || Ascii(bytes, 0, 8) != "MESGbmg1")
            throw new InvalidDataException("Not a bounded BMG file.");
        uint declared = U32(bytes, 8);
        uint sections = U32(bytes, 0xC);
        if (declared < 0x20 || declared > bytes.Length || sections > 32)
            throw new InvalidDataException("Invalid BMG header.");
        long off = 0x20, inf = -1, dat = -1, infSize = 0, datSize = 0;
        for (int i = 0; i < sections; i++)
        {
            Check(bytes, off, 8);
            uint size = U32(bytes, off + 4);
            if (size < 8 || size > declared - off) throw new InvalidDataException("BMG section outside file.");
            string tag = Ascii(bytes, off, 4);
            if (tag == "INF1")
            {
                if (inf >= 0) throw new InvalidDataException("Duplicate INF1.");
                inf = off; infSize = size;
            }
            if (tag == "DAT1")
            {
                if (dat >= 0) throw new InvalidDataException("Duplicate DAT1.");
                dat = off; datSize = size;
            }
            off += size;
        }
        if (inf < 0 || dat < 0 || infSize < 0x10) throw new InvalidDataException("Missing BMG message tables.");
        int count = U16(bytes, inf + 8);
        int stride = U16(bytes, inf + 0xA);
        if (stride < 0x18 || 0x10L + (long)count * stride > infSize)
            throw new InvalidDataException("Invalid BMG INF1 entries.");
        var result = new BmgMessages();
        var ids = new HashSet<ushort>();
        for (int i = 0; i < count; i++)
        {
            long entry = inf + 0x10L + (long)i * stride;
            uint stringOffset = U32(bytes, entry);
            ushort id = U16(bytes, entry + 4);
            if (!ids.Add(id)) throw new InvalidDataException("Duplicate BMG message ID.");
            long start = dat + 8L + stringOffset;
            long end = dat + datSize;
            if (start >= end) throw new InvalidDataException("BMG message outside DAT1.");
            // Keep each control byte intact. A control packet is length-prefixed.
            long cursor = start;
            bool terminated = false;
            while (cursor < end)
            {
                byte b = bytes[(int)cursor++];
                if (b == 0) { terminated = true; break; }
                if (b == 0x1A)
                {
                    if (cursor >= end) throw new InvalidDataException("Truncated BMG control length.");
                    int packetLength = bytes[(int)cursor];
                    if (packetLength < 2 || cursor + packetLength - 1 > end)
                        throw new InvalidDataException("Invalid BMG control packet.");
                    cursor += packetLength - 1;
                }
                if (cursor - start > 65536) throw new InvalidDataException("BMG message is too long.");
            }
            if (!terminated) throw new InvalidDataException("Unterminated BMG text.");
            result.messages.Add(new Message(id, U16(bytes, entry + 8), bytes[(int)(entry + 0xC)],
                Slice(bytes, start, cursor - start - 1)));
        }
        return result;
    }
}
