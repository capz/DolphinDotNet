using System;
using System.IO;
using System.Text;

namespace Dolphin.Formats;

/// <summary>Bounded big-endian access for untrusted GameCube asset data.</summary>
public static class GameCubeBinary
{
    public const int MaxAssetBytes = 16 * 1024 * 1024;

    public static void Check(byte[] data, long offset, long length)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
            throw new InvalidDataException("GameCube asset offset or length is out of bounds.");
    }

    public static ushort U16(byte[] data, long offset)
    {
        Check(data, offset, 2);
        int i = (int)offset;
        return (ushort)((data[i] << 8) | data[i + 1]);
    }

    public static short S16(byte[] data, long offset) => unchecked((short)U16(data, offset));

    public static uint U32(byte[] data, long offset)
    {
        Check(data, offset, 4);
        int i = (int)offset;
        return (uint)((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]);
    }

    public static float F32(byte[] data, long offset)
    {
        uint bits = U32(data, offset);
        var bytes = BitConverter.GetBytes(bits);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }

    public static string Ascii(byte[] data, long offset, int length)
    {
        Check(data, offset, length);
        return Encoding.ASCII.GetString(data, (int)offset, length);
    }

    public static string NullTerminated(byte[] data, long offset, long end, int maxLength = 256)
    {
        Check(data, offset, 0);
        Check(data, end, 0);
        if (end < offset) throw new InvalidDataException("Invalid string range.");
        int p = (int)offset;
        int limit = (int)Math.Min(end, offset + maxLength);
        int start = p;
        while (p < limit && data[p] != 0) p++;
        if (p == limit) throw new InvalidDataException("Unterminated or excessively long name.");
        return Encoding.ASCII.GetString(data, start, p - start);
    }

    public static byte[] Slice(byte[] data, long offset, long count)
    {
        Check(data, offset, count);
        if (count > MaxAssetBytes) throw new InvalidDataException("Asset exceeds native size budget.");
        var copy = new byte[(int)count];
        Buffer.BlockCopy(data, (int)offset, copy, 0, copy.Length);
        return copy;
    }

    public static byte[] Yaz0Decode(byte[] input, int maxBytes = MaxAssetBytes)
    {
        if (Ascii(input, 0, 4) != "Yaz0") throw new InvalidDataException("Not Yaz0.");
        uint decoded = U32(input, 4);
        if (decoded > maxBytes || decoded > MaxAssetBytes) throw new InvalidDataException("Yaz0 output exceeds budget.");
        var output = new byte[(int)decoded];
        int src = 16, dst = 0, bits = 0, code = 0;
        while (dst < output.Length)
        {
            if (bits == 0)
            {
                Check(input, src, 1);
                code = input[src++];
                bits = 8;
            }
            if ((code & 0x80) != 0)
            {
                Check(input, src, 1);
                output[dst++] = input[src++];
            }
            else
            {
                Check(input, src, 2);
                int a = input[src++], b = input[src++];
                int count = a >> 4;
                if (count == 0)
                {
                    Check(input, src, 1);
                    count = input[src++] + 0x12;
                }
                else count += 2;
                int back = ((a & 15) << 8 | b) + 1;
                if (back > dst || count > output.Length - dst) throw new InvalidDataException("Invalid Yaz0 backreference.");
                for (int j = 0; j < count; j++) { output[dst] = output[dst - back]; dst++; }
            }
            code <<= 1;
            bits--;
        }
        return output;
    }

    public static byte[] UnwrapYaz0(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length > MaxAssetBytes) throw new InvalidDataException("Input exceeds bounded asset limit.");
        return data.Length >= 4 && Ascii(data, 0, 4) == "Yaz0" ? Yaz0Decode(data) : data;
    }
}
