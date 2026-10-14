using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
namespace Dolphin.Network.Http;
internal sealed class HttpBodyStream : Dolphin.IO.Stream
{
    private readonly TcpClient client;private readonly CancellationToken requestToken;private readonly byte[] input;private int position,available,remaining,chunkState,lineLength,trailerBytes,total;private readonly char[] line=new char[256];private readonly bool chunked,untilClose;private bool lineCr,finished,disposed,busy;
    internal HttpBodyStream(TcpClient client,HttpResponseMessage response,byte[] initial,int offset,int count,bool head,CancellationToken token)
    {
        this.client=client;requestToken=token;input=new byte[8192];Array.Copy(initial,offset,input,0,count);available=count;
        string? encoding=response.Headers.Get("content-encoding"),transfer=response.Headers.Get("transfer-encoding"),length=response.Headers.Get("content-length");
        if(encoding!=null&&HttpText.Lower(encoding)!="identity")throw new NotSupportedException("Compressed HTTP content is unavailable.");if(transfer!=null&&length!=null)throw new FormatException("Ambiguous HTTP body framing.");
        chunked=transfer!=null;if(chunked&&HttpText.Lower(transfer!)!="chunked")throw new NotSupportedException("Unsupported transfer coding.");
        untilClose=!chunked&&length==null;remaining=length==null?0:HttpText.Number(length,16777216);int status=(int)response.StatusCode;finished=head||status==204||status==304||!chunked&&!untilClose&&remaining==0;if(finished)client.Dispose();
    }
    public override bool CanRead=>!disposed;
    public override bool CanWrite=>false;
    public override bool CanSeek=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException("Use asynchronous HTTP body reads.");
    public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    public override long Seek(long n,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long n)=>throw new NotSupportedException();
    public override void Flush(){}
    public override Task<int> ReadAsync(byte[] b,int o,int n,CancellationToken token)
    {
        if(disposed)throw new ObjectDisposedException("HttpBodyStream");Validate(b,o,n);if(busy)throw new InvalidOperationException("HTTP body read pending.");if(n==0||finished)return Task.FromResult(0);
        var work=new ReadWork(this,b,o,n,token);NetworkRuntime.Add(work);busy=true;return work.Completion.Task;
    }
    public override void Dispose(){disposed=true;client.Dispose();}
    private void End(){finished=true;client.Dispose();}
    private bool LineByte(byte b)
    {
        if(lineCr){if(b!=10)throw new FormatException("HTTP chunk CRLF invalid.");lineCr=false;return true;}if(b==13){lineCr=true;return false;}if(b<32&&b!=9||b>126||lineLength==line.Length)throw new FormatException("HTTP chunk line invalid.");line[lineLength++]=(char)b;return false;
    }
    private void ChunkSize()
    {
        int n=0,digits=0;for(int i=0;i<lineLength;i++){char c=line[i];if(c==';')break;int d=c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:-1;if(d<0||n>(16777216-d)/16)throw new FormatException("Invalid chunk size.");n=n*16+d;digits++;}if(digits==0)throw new FormatException("Missing chunk size.");remaining=n;lineLength=0;chunkState=n==0?4:1;
    }
    private sealed class ReadWork : Operation
    {
        private readonly HttpBodyStream owner;private readonly byte[] output;private readonly int offset,count;internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal ReadWork(HttpBodyStream s,byte[] b,int o,int n,CancellationToken token):base(token){owner=s;output=b;offset=o;count=n>4096?4096:n;}
        protected override bool Step()
        {
            owner.requestToken.ThrowIfCancellationRequested();if(owner.disposed)throw new ObjectDisposedException("HttpBodyStream");int written=0;
            while(written<count&&!owner.finished)
            {
                if(owner.position==owner.available){if(written>0)break;int n=NativeNetwork.NetRead(owner.client.Client.Handle,owner.input,0,4096,null!);NetworkRuntime.Check(n);if(n==-1)return false;if(n==0){if(!owner.untilClose)throw new IOException("HTTP response body truncated.");owner.End();break;}owner.position=0;owner.available=n;}
                if(!owner.chunked||owner.chunkState==1){int n=owner.available-owner.position;if(n>count-written)n=count-written;if(!owner.untilClose&&n>owner.remaining)n=owner.remaining;Array.Copy(owner.input,owner.position,output,offset+written,n);owner.position+=n;written+=n;owner.total+=n;if(owner.total>16777216)throw new IOException("HTTP streamed body exceeds limit.");if(!owner.untilClose){owner.remaining-=n;if(owner.remaining==0){if(owner.chunked)owner.chunkState=2;else owner.End();}}continue;}
                byte next=owner.input[owner.position++];if(owner.chunkState==2){if(next!=13)throw new FormatException("Chunk terminator missing.");owner.chunkState=3;}else if(owner.chunkState==3){if(next!=10)throw new FormatException("Chunk terminator invalid.");owner.chunkState=0;}else if(owner.chunkState==0){if(owner.LineByte(next))owner.ChunkSize();}else{if(++owner.trailerBytes>8192)throw new FormatException("HTTP trailers exceed limit.");if(owner.LineByte(next)){if(owner.lineLength==0)owner.End();else{string trailer=new string(owner.line,0,owner.lineLength);int colon=HttpText.Index(trailer,':');if(colon<=0)throw new FormatException("Invalid trailer.");string name=HttpText.Lower(trailer.Substring(0,colon));if(name=="content-length"||name=="transfer-encoding")throw new FormatException("Framing trailer rejected.");owner.lineLength=0;}}}
            }
            owner.busy=false;Completion.SetResult(written);return true;
        }
        protected override void Fail(Exception e){owner.busy=false;owner.Dispose();Completion.SetException(e);}
    }
}
