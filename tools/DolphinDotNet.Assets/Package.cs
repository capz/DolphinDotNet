using System.Buffers.Binary;
using System.Text;
namespace DolphinDotNet.Assets;

internal sealed class BigWriter : IDisposable
{
    private readonly MemoryStream stream = new();
    public void U(uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); stream.Write(b); }
    public void I(int value) => U(unchecked((uint)value));
    public void F(float value) { if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite model scalar."); I(BitConverter.SingleToInt32Bits(value)); }
    public void Floats(IEnumerable<float> values) { foreach (var v in values) F(v); }
    public void Name(string value) { var b = Encoding.UTF8.GetBytes(value); if (b.Length > 63 || value.Contains('\0')) throw new InvalidDataException("Asset name exceeds 63 UTF-8 bytes: " + value); stream.Write(b); stream.Write(new byte[64-b.Length]); }
    public void Bytes(byte[] value) => stream.Write(value);
    public byte[] Data => stream.ToArray();
    public void Dispose() => stream.Dispose();
}
internal static class Package
{
    public static uint Hash(ReadOnlySpan<byte> data) { uint h=2166136261; foreach(byte b in data) h=unchecked((h^b)*16777619); return h; }
    public static byte[] Write(byte[][] sections)
    {
        int offset=160; var offsets=new int[sections.Length];
        for(int i=0;i<sections.Length;i++) { offsets[i]=offset; offset=checked((offset+sections[i].Length+31)&~31); }
        if(offset>16*1024*1024) throw new InvalidDataException("Package exceeds 16 MiB limit.");
        using var w=new BigWriter(); w.U(0x444e444d);w.U(1);w.I(offset);w.I(sections.Length);
        for(int i=0;i<sections.Length;i++){w.I(i+1);w.I(offsets[i]);w.I(sections[i].Length);w.U(Hash(sections[i]));}
        var result=new byte[offset]; w.Data.CopyTo(result,0);
        for(int i=0;i<sections.Length;i++)sections[i].CopyTo(result,offsets[i]);
        return result;
    }
}
