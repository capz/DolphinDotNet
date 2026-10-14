using System;
namespace Dolphin.Graphics.Raw;

/// <summary>A borrowed native address. Keep its allocation alive until GPU completion.</summary>
public readonly struct NativeAddress
{
    private readonly NativeBuffer? owner;
    private readonly ulong address;
    private readonly int offset;
    internal NativeAddress(NativeBuffer buffer,int offset) { owner=buffer;this.offset=offset;address=0; }
    private NativeAddress(ulong address) { owner=null;this.address=address;offset=0; }
    internal ulong Value => owner==null ? address : owner.GetAddress(offset);
    public static NativeAddress Null => default;
    public bool IsNull => Value==0;
    public NativeAddress Add(int bytes)
    {
        if(bytes<0)throw new ArgumentOutOfRangeException("bytes");
        if(owner!=null) {
            if(bytes>owner.Length-offset)throw new ArgumentOutOfRangeException("bytes");
            return new NativeAddress(owner,offset+bytes);
        }
        if(ulong.MaxValue-address<(ulong)bytes)throw new ArgumentOutOfRangeException("bytes");
        return new NativeAddress(address+(ulong)bytes);
    }
    internal static NativeAddress Borrow(ulong address) => new NativeAddress(address);
    /// <summary>Expert escape hatch for platform addresses or unmanaged callbacks; no memory safety guarantee.</summary>
    public static NativeAddress UnsafeFromUInt64(ulong address) => Borrow(address);
}
public enum NativeObjectKind
{
    Fifo,Texture,Palette,Light,TextureRegion,PaletteRegion,FogAdjustment,VideoMode,
    VertexDescriptor,VertexFormat,Matrix3x4,Matrix4x4,Matrix3x3,Vector3,Quaternion
}
public struct GXColor
{
    public byte R,G,B,A;
    public GXColor(byte r,byte g,byte b,byte a=255){R=r;G=g;B=b;A=a;}
    internal uint Packed => (uint)R<<24|(uint)G<<16|(uint)B<<8|A;
}
public struct GXColorS10
{
    public short R,G,B,A;
    public GXColorS10(short r,short g,short b,short a){R=r;G=g;B=b;A=a;}
    internal ulong Packed => (ulong)(ushort)R<<48|(ulong)(ushort)G<<32|(ulong)(ushort)B<<16|(ushort)A;
}
