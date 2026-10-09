using System;
using System.IO;
namespace Dolphin.IO;
public abstract class Stream : IDisposable
{
    public abstract bool CanRead {get;}
    public abstract bool CanWrite {get;}
    public abstract bool CanSeek {get;}
    public abstract long Length {get;}
    public abstract long Position {get;set;}
    public abstract int Read(byte[] buffer,int offset,int count);
    public abstract void Write(byte[] buffer,int offset,int count);
    public abstract long Seek(long offset,SeekOrigin origin);
    public abstract void SetLength(long length);
    public abstract void Flush();
    public virtual int ReadByte(){var buffer=new byte[1];return Read(buffer,0,1)==0?-1:buffer[0];}
    public virtual void WriteByte(byte value){var buffer=new byte[1];buffer[0]=value;Write(buffer,0,1);}
    public void CopyTo(Stream destination)=>CopyTo(destination,4096);
    public void CopyTo(Stream destination,int bufferSize){if(destination==null)throw new ArgumentNullException("destination");if(bufferSize<=0)throw new ArgumentOutOfRangeException("bufferSize");if(!CanRead||!destination.CanWrite)throw new NotSupportedException();var buffer=new byte[bufferSize];int n;while((n=Read(buffer,0,buffer.Length))>0)destination.Write(buffer,0,n);}
    protected static void Validate(byte[] buffer,int offset,int count){if(buffer==null)throw new ArgumentNullException("buffer");if(offset<0||count<0)throw new ArgumentOutOfRangeException();if(offset>buffer.Length-count)throw new ArgumentException("Invalid buffer range.");}
    public virtual Dolphin.Threading.Tasks.Task<int> ReadAsync(byte[] buffer,int offset,int count)=>ReadAsync(buffer,offset,count,default);
    public virtual Dolphin.Threading.Tasks.Task<int> ReadAsync(byte[] buffer,int offset,int count,Dolphin.Threading.CancellationToken token){Validate(buffer,offset,count);return Dolphin.Threading.Tasks.Task.Run(()=>Read(buffer,offset,count>4096?4096:count),token);}
    public virtual Dolphin.Threading.Tasks.Task WriteAsync(byte[] buffer,int offset,int count)=>WriteAsync(buffer,offset,count,default);
    public virtual Dolphin.Threading.Tasks.Task WriteAsync(byte[] buffer,int offset,int count,Dolphin.Threading.CancellationToken token){Validate(buffer,offset,count);var work=new WriteWork(this,buffer,offset,count,token);Dolphin.Threading.Tasks.Scheduler.Post(work.Step);return work.Completion.Task;}
    public virtual Dolphin.Threading.Tasks.Task FlushAsync()=>FlushAsync(default);
    public virtual Dolphin.Threading.Tasks.Task FlushAsync(Dolphin.Threading.CancellationToken token)=>Dolphin.Threading.Tasks.Task.Run(Flush,token);
    public Dolphin.Threading.Tasks.Task CopyToAsync(Stream destination)=>CopyToAsync(destination,4096,default);
    public Dolphin.Threading.Tasks.Task CopyToAsync(Stream destination,int bufferSize)=>CopyToAsync(destination,bufferSize,default);
    public Dolphin.Threading.Tasks.Task CopyToAsync(Stream destination,int bufferSize,Dolphin.Threading.CancellationToken token){if(destination==null)throw new ArgumentNullException("destination");if(bufferSize<=0)throw new ArgumentOutOfRangeException("bufferSize");var work=new CopyWork(this,destination,bufferSize,token);Dolphin.Threading.Tasks.Scheduler.Post(work.Step);return work.Completion.Task;}
    private sealed class WriteWork
    {
        private readonly Stream stream;private readonly byte[] buffer;private int offset,count;private readonly Dolphin.Threading.CancellationToken token;
        internal readonly Dolphin.Threading.Tasks.TaskCompletionSource<int> Completion=new Dolphin.Threading.Tasks.TaskCompletionSource<int>();
        internal WriteWork(Stream stream,byte[] buffer,int offset,int count,Dolphin.Threading.CancellationToken token){this.stream=stream;this.buffer=buffer;this.offset=offset;this.count=count;this.token=token;}
        internal void Step(){try{token.ThrowIfCancellationRequested();var n=count>4096?4096:count;stream.Write(buffer,offset,n);offset+=n;count-=n;if(count==0)Completion.SetResult(0);else Dolphin.Threading.Tasks.Scheduler.Post(Step);}catch(Exception error){Completion.SetException(error);}}
    }
    private sealed class CopyWork
    {
        private readonly Stream source,destination;private readonly byte[] buffer;private readonly Dolphin.Threading.CancellationToken token;
        internal readonly Dolphin.Threading.Tasks.TaskCompletionSource<int> Completion=new Dolphin.Threading.Tasks.TaskCompletionSource<int>();
        internal CopyWork(Stream source,Stream destination,int bufferSize,Dolphin.Threading.CancellationToken token){this.source=source;this.destination=destination;buffer=new byte[bufferSize>4096?4096:bufferSize];this.token=token;}
        internal void Step(){try{token.ThrowIfCancellationRequested();var n=source.Read(buffer,0,buffer.Length);if(n==0)Completion.SetResult(0);else{destination.Write(buffer,0,n);Dolphin.Threading.Tasks.Scheduler.Post(Step);}}catch(Exception error){Completion.SetException(error);}}
    }
    public virtual void Close()=>Dispose();
    public virtual void Dispose(){}
}
public sealed class MemoryStream : Stream
{
    private byte[] buffer;
    private int origin,length,position;
    private readonly bool expandable,writable,visible;
    private bool disposed;
    public MemoryStream():this(0){}
    public MemoryStream(int capacity){if(capacity<0)throw new ArgumentOutOfRangeException("capacity");buffer=new byte[capacity];expandable=true;writable=true;visible=true;}
    public MemoryStream(byte[] buffer):this(buffer,true){}
    public MemoryStream(byte[] buffer,bool writable):this(buffer,0,buffer==null?0:buffer.Length,writable,false){}
    public MemoryStream(byte[] buffer,int index,int count):this(buffer,index,count,true,false){}
    public MemoryStream(byte[] buffer,int index,int count,bool writable):this(buffer,index,count,writable,false){}
    public MemoryStream(byte[] buffer,int index,int count,bool writable,bool publiclyVisible){Validate(buffer,index,count);this.buffer=buffer;origin=index;length=count;this.writable=writable;visible=publiclyVisible;}
    private void Check(){if(disposed)throw new ObjectDisposedException("MemoryStream");}
    private void Ensure(int capacity){if(capacity<=buffer.Length-origin)return;if(!expandable)throw new NotSupportedException("Stream is not expandable.");var size=buffer.Length==0?256:buffer.Length>1073741823?int.MaxValue:buffer.Length*2;if(size<capacity)size=capacity;var next=new byte[size];Array.Copy(buffer,origin,next,0,length);buffer=next;origin=0;}
    public override bool CanRead=>!disposed;
    public override bool CanWrite=>!disposed&&writable;
    public override bool CanSeek=>!disposed;
    public override long Length {get {Check();return length;}}
    public override long Position {get {Check();return position;}set {Check();if(value<0||value>int.MaxValue)throw new ArgumentOutOfRangeException("value");position=(int)value;}}
    public int Capacity {get {Check();return buffer.Length-origin;}set {Check();if(value<length)throw new ArgumentOutOfRangeException("value");if(!expandable&&value!=Capacity)throw new NotSupportedException();if(value==Capacity)return;var next=new byte[value];Array.Copy(buffer,origin,next,0,length);buffer=next;origin=0;}}
    public override int Read(byte[] destination,int offset,int count){Check();Validate(destination,offset,count);var available=position>=length?0:length-position;if(count>available)count=available;if(count==0)return 0;Array.Copy(buffer,origin+position,destination,offset,count);position+=count;return count;}
    public override int ReadByte(){Check();return position>=length?-1:buffer[origin+position++];}
    public override void Write(byte[] source,int offset,int count){Check();Validate(source,offset,count);if(!writable)throw new NotSupportedException();if(count>int.MaxValue-position)throw new IOException("Stream is too large.");var end=position+count;Ensure(end);if(position>length)Array.Clear(buffer,origin+length,position-length);Array.Copy(source,offset,buffer,origin+position,count);position=end;if(end>length)length=end;}
    public override void WriteByte(byte value){var one=new byte[1];one[0]=value;Write(one,0,1);}
    public override long Seek(long offset,SeekOrigin seekOrigin){Check();long basis=seekOrigin==SeekOrigin.Begin?0:seekOrigin==SeekOrigin.Current?position:seekOrigin==SeekOrigin.End?length:throw new ArgumentException("Invalid origin.");if(offset< -basis||offset>int.MaxValue-basis)throw new IOException("Invalid seek.");position=(int)(basis+offset);return origin+position;}
    public override void SetLength(long value){Check();if(!writable)throw new NotSupportedException();if(value<0||value>int.MaxValue)throw new ArgumentOutOfRangeException("value");Ensure((int)value);if(value>length)Array.Clear(buffer,origin+length,(int)value-length);length=(int)value;if(position>length)position=length;}
    public override void Flush(){Check();}
    public byte[] ToArray(){var result=new byte[length];Array.Copy(buffer,origin,result,0,length);return result;}
    public byte[] GetBuffer(){if(!visible)throw new UnauthorizedAccessException();return buffer;}
    public void WriteTo(Stream destination){Check();if(destination==null)throw new ArgumentNullException("destination");destination.Write(buffer,origin,length);}
    public override void Dispose(){disposed=true;}
}
