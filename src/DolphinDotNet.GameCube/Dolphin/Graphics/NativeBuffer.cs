using System;
using Dolphin.Graphics.Raw;
namespace Dolphin.Graphics;

/// <summary>Aligned, zeroed native storage. Mutate between frames; raw cache maintenance is explicit.</summary>
public sealed class NativeBuffer : IDisposable
{
    internal readonly int Token;
    private bool disposed;
    public int Length {get;}
    private NativeBuffer(int token,int length){Token=token;Length=length;}
    public static NativeBuffer Allocate(int bytes,int alignment=32)
    {
        int token=NativeGraphics.BufferCreate(bytes,alignment);
        if(token==0)throw new ArgumentException("Invalid native allocation, alignment or memory budget.");
        return new NativeBuffer(token,bytes);
    }
    public static NativeBuffer AllocateObject(NativeObjectKind kind)
    {
        int size=NativeGraphics.NativeObjectSize((int)kind);
        if(size<0)throw new ArgumentOutOfRangeException("kind");
        return Allocate(size);
    }
    public NativeAddress Address {get{Check();return new NativeAddress(this,0);}}
    public NativeAddress UncachedAddress {get{Check();return NativeAddress.Borrow(NativeGraphics.BufferUncached(Token));}}
    internal ulong GetAddress(int offset){Check();return NativeGraphics.BufferAddress(Token,offset);}
    public void Write(byte[] source,int sourceOffset=0,int destinationOffset=0,int count=-1)
    {Check();if(source==null)throw new ArgumentNullException("source");if(count<0)count=source.Length-sourceOffset;GraphicsDevice.Check(NativeGraphics.BufferWriteBytes(Token,destinationOffset,source,sourceOffset,count),"Invalid buffer write or active frame.");}
    public void Write(float[] source,int sourceOffset=0,int destinationOffset=0,int count=-1)
    {Check();if(source==null)throw new ArgumentNullException("source");if(count<0)count=source.Length-sourceOffset;GraphicsDevice.Check(NativeGraphics.BufferWriteFloats(Token,destinationOffset,source,sourceOffset,count),"Invalid buffer write or active frame.");}
    public void Write(ushort[] source,int sourceOffset=0,int destinationOffset=0,int count=-1)
    {Check();if(source==null)throw new ArgumentNullException("source");if(count<0)count=source.Length-sourceOffset;GraphicsDevice.Check(NativeGraphics.BufferWriteIndices(Token,destinationOffset,source,sourceOffset,count),"Invalid buffer write or active frame.");}
    public void Read(byte[] destination,int sourceOffset=0,int destinationOffset=0,int count=-1)
    {Check();if(destination==null)throw new ArgumentNullException("destination");if(count<0)count=destination.Length-destinationOffset;GraphicsDevice.Check(NativeGraphics.BufferReadBytes(Token,sourceOffset,destination,destinationOffset,count),"Invalid buffer read or active frame.");}
    /// <summary>Compute an incremental IEEE CRC32 directly over native staging
    /// bytes between frames. No managed payload copy or temporary allocation.
    /// Pass the preceding result as initial to checksum consecutive chunks.</summary>
    public uint ComputeCrc32(int offset=0,int count=-1,uint initial=0)
    {
        Check();if(count<0)count=Length-offset;
        if(offset<0||count<0||offset>Length||count>Length-offset)throw new ArgumentOutOfRangeException("count");
        return NativeGraphics.BufferCrc32(Token,offset,count,initial);
    }
    public void Flush(int offset=0,int count=-1){Check();if(count<0)count=Length-offset;GraphicsDevice.Check(NativeGraphics.BufferFlush(Token,offset,count,0),"Invalid cache range.");}
    public void Invalidate(int offset=0,int count=-1){Check();if(count<0)count=Length-offset;GraphicsDevice.Check(NativeGraphics.BufferFlush(Token,offset,count,1),"Invalid cache range.");}
    private void Check(){if(disposed)throw new ObjectDisposedException("NativeBuffer");}
    public void Dispose(){if(disposed)return;GraphicsDevice.Check(NativeGraphics.DrawRelease(Token),"Cannot dispose native storage during a raw stream.");disposed=true;}
}
