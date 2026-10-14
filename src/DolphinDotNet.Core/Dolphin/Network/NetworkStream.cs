using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
namespace Dolphin.Network;
public sealed class NetworkStream : Dolphin.IO.Stream
{
    private readonly Socket socket;private readonly bool owns;private bool disposed;
    public NetworkStream(Socket socket):this(socket,false){}
    public NetworkStream(Socket socket,bool ownsSocket){this.socket=socket??throw new ArgumentNullException("socket");owns=ownsSocket;}
    private void Check(){if(disposed)throw new ObjectDisposedException("NetworkStream");socket.Check();}
    public override bool CanRead=>!disposed;
    public override bool CanWrite=>!disposed;
    public override bool CanSeek=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException("Use ReadAsync; blocking network IO is unavailable on the game thread.");
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException("Use WriteAsync.");
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Flush(){Check();}
    public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){Check();return socket.ReceiveAsync(buffer,offset,count,SocketFlags.None,token);}
    public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken token){Check();Validate(buffer,offset,count);var work=new WriteAll(socket,buffer,offset,count,token);work.Start();return work.Completion.Task;}
    public override void Dispose(){if(!disposed){disposed=true;if(owns)socket.Dispose();}}
    private sealed class WriteAll
    {
        private readonly Socket socket;private readonly byte[] buffer;private readonly CancellationToken token;private int offset,count;private Task<int>? write;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal WriteAll(Socket s,byte[] b,int o,int n,CancellationToken t){socket=s;buffer=b;offset=o;count=n;token=t;}
        internal void Start()=>Step();
        private void Step(){try{token.ThrowIfCancellationRequested();if(write!=null){int n=write.GetAwaiter().GetResult();if(n==0&&count!=0)throw new SocketException(0,-2);offset+=n;count-=n;}if(count==0){Completion.SetResult(0);return;}write=socket.SendAsync(buffer,offset,count,SocketFlags.None,token);write.GetAwaiter().OnCompleted(Step);}catch(Exception e){socket.Dispose();Completion.SetException(e);}}
    }
}
