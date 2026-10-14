using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
using Dolphin.Text;
namespace Dolphin.Network.Http;
public sealed class HttpMethod
{
    public string Method {get;}
    public HttpMethod(string method){if(method!="GET"&&method!="HEAD"&&method!="POST"&&method!="PUT"&&method!="DELETE")throw new NotSupportedException("Unsupported HTTP method.");Method=method;}
    public static readonly HttpMethod Get=new HttpMethod("GET"),Head=new HttpMethod("HEAD"),Post=new HttpMethod("POST"),Put=new HttpMethod("PUT"),Delete=new HttpMethod("DELETE");
}
public enum HttpCompletionOption { ResponseContentRead=0,ResponseHeadersRead=1 }
public sealed class HttpHeaders
{
    private readonly List<string> names=new List<string>(),values=new List<string>();
    public void Add(string name,string value){if(names.Count>=64)throw new InvalidOperationException("Header count limit.");if(name==null||name.Length==0||value==null)throw new ArgumentException("Invalid HTTP header.");for(int i=0;i<name.Length;i++)if(!HttpText.Token(name[i]))throw new ArgumentException("Invalid header name.");for(int i=0;i<value.Length;i++)if(value[i]=='\r'||value[i]=='\n'||value[i]==0)throw new ArgumentException("Invalid header value.");names.Add(HttpText.Lower(name));values.Add(value);}
    public string? Get(string name){name=HttpText.Lower(name);for(int i=0;i<names.Count;i++)if(names[i]==name)return values[i];return null;}
    internal int Count=>names.Count;
    internal string Name(int i)=>names[i];
    internal string Value(int i)=>values[i];
}
public sealed class HttpRequestMessage : IDisposable
{
    public HttpMethod Method {get;set;}
    public string RequestUri {get;set;}
    public HttpHeaders Headers {get;}=new HttpHeaders();
    public HttpContent? Content {get;set;}
    public HttpRequestMessage(HttpMethod method,string uri){Method=method;RequestUri=uri;}
    public void Dispose(){if(Content!=null)Content.Dispose();}
}
public sealed class HttpResponseMessage : IDisposable
{
    public System.Net.HttpStatusCode StatusCode {get;internal set;}
    public string ReasonPhrase {get;internal set;}="";
    public HttpHeaders Headers {get;}=new HttpHeaders();
    public HttpContent Content {get;internal set;}=null!;
    public bool IsSuccessStatusCode=>(int)StatusCode>=200&&(int)StatusCode<=299;
    public HttpResponseMessage EnsureSuccessStatusCode(){if(!IsSuccessStatusCode)throw new IOException("HTTP request returned a non-success status.");return this;}
    public void Dispose(){if(Content!=null)Content.Dispose();}
}
public class HttpContent : IDisposable
{
    internal Dolphin.IO.Stream? Body;
    internal byte[]? Bytes;
    internal int Limit=65536;
    public HttpHeaders Headers {get;}=new HttpHeaders();
    public Task<Dolphin.IO.Stream> ReadAsStreamAsync(){if(Body==null)Body=new Dolphin.IO.MemoryStream(Bytes??new byte[0],false);return Task.FromResult(Body);}
    public Task<byte[]> ReadAsByteArrayAsync(){if(Bytes!=null)return Task.FromResult(Bytes);var work=new BufferWork(this);work.Step();return work.Completion.Task;}
    public Task<string> ReadAsStringAsync(){var work=new StringWork(ReadAsByteArrayAsync());work.Start();return work.Completion.Task;}
    public void Dispose(){if(Body!=null)Body.Dispose();}
    private sealed class StringWork
    {
        private readonly Task<byte[]> bytes;internal readonly TaskCompletionSource<string> Completion=new TaskCompletionSource<string>();
        internal StringWork(Task<byte[]> b){bytes=b;}
        internal void Start()=>bytes.GetAwaiter().OnCompleted(Finish);
        private void Finish(){try{Completion.SetResult(new UTF8Encoding(false,true).GetString(bytes.GetAwaiter().GetResult()));}catch(Exception e){Completion.SetException(e);}}
    }
    private sealed class BufferWork
    {
        private readonly HttpContent content;private readonly byte[] chunk=new byte[4096];private readonly Dolphin.IO.MemoryStream memory=new Dolphin.IO.MemoryStream();private Task<int>? read;
        internal readonly TaskCompletionSource<byte[]> Completion=new TaskCompletionSource<byte[]>();
        internal BufferWork(HttpContent c){content=c;}
        internal void Step(){try{if(read!=null){int n=read.GetAwaiter().GetResult();if(n==0){content.Bytes=memory.ToArray();content.Body!.Dispose();content.Body=new Dolphin.IO.MemoryStream(content.Bytes,false);Completion.SetResult(content.Bytes);return;}if(memory.Length>content.Limit-n)throw new IOException("HTTP body exceeds buffer limit; use streaming.");memory.Write(chunk,0,n);}read=content.Body!.ReadAsync(chunk,0,chunk.Length);read.GetAwaiter().OnCompleted(Step);}catch(Exception e){content.Dispose();Completion.SetException(e);}}
    }
}
public sealed class ByteArrayContent : HttpContent {public ByteArrayContent(byte[] bytes){Bytes=bytes??throw new ArgumentNullException("bytes");}}
public sealed class StringContent : HttpContent {public StringContent(string text){Bytes=new UTF8Encoding(false,true).GetBytes(text);Headers.Add("content-type","text/plain; charset=utf-8");}}
public sealed class HttpClient : IDisposable
{
    private readonly List<RequestWork> active=new List<RequestWork>();private bool disposed;
    public int MaximumResponseBytes {get;set;}=65536;
    public int MaximumRedirects {get;set;}=5;
    public HttpHeaders DefaultRequestHeaders {get;}=new HttpHeaders();
    public Task<HttpResponseMessage> GetAsync(string uri)=>GetAsync(uri,HttpCompletionOption.ResponseContentRead,default);
    public Task<HttpResponseMessage> GetAsync(string uri,CancellationToken token)=>GetAsync(uri,HttpCompletionOption.ResponseContentRead,token);
    public Task<HttpResponseMessage> GetAsync(string uri,HttpCompletionOption option)=>GetAsync(uri,option,default);
    public Task<HttpResponseMessage> GetAsync(string uri,HttpCompletionOption option,CancellationToken token)=>SendAsync(new HttpRequestMessage(HttpMethod.Get,uri),option,token);
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)=>SendAsync(request,HttpCompletionOption.ResponseContentRead,default);
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>SendAsync(request,HttpCompletionOption.ResponseContentRead,token);
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,HttpCompletionOption option,CancellationToken token)
    {
        if(disposed)throw new ObjectDisposedException("HttpClient");if(request==null)throw new ArgumentNullException("request");if(MaximumResponseBytes<1||MaximumResponseBytes>1048576||active.Count>=2)throw new InvalidOperationException("HTTP resource limit.");
        var work=new RequestWork(this,request,option,token);NetworkRuntime.Add(work);active.Add(work);return work.Completion.Task;
    }
    public Task<string> GetStringAsync(string uri){var work=new DownloadString(GetAsync(uri));work.Start();return work.Completion.Task;}
    public Task<byte[]> GetByteArrayAsync(string uri){var work=new DownloadBytes(GetAsync(uri));work.Start();return work.Completion.Task;}
    public Task<Dolphin.IO.Stream> GetStreamAsync(string uri){var work=new DownloadStream(GetAsync(uri,HttpCompletionOption.ResponseHeadersRead));work.Start();return work.Completion.Task;}
    public void Dispose(){disposed=true;while(active.Count>0)active[0].Abort();}
    private sealed class DownloadString
    {
        private readonly Task<HttpResponseMessage> task;internal readonly TaskCompletionSource<string> Completion=new TaskCompletionSource<string>();internal DownloadString(Task<HttpResponseMessage> t){task=t;}internal void Start()=>task.GetAwaiter().OnCompleted(Finish);
        private void Finish(){try{var r=task.GetAwaiter().GetResult();r.EnsureSuccessStatusCode();Completion.SetResult(new UTF8Encoding(false,true).GetString(r.Content.Bytes!));r.Dispose();}catch(Exception e){if((task.IsCompleted && !task.IsFaulted && !task.IsCanceled))task.GetAwaiter().GetResult().Dispose();Completion.SetException(e);}}
    }
    private sealed class DownloadBytes
    {
        private readonly Task<HttpResponseMessage> task;internal readonly TaskCompletionSource<byte[]> Completion=new TaskCompletionSource<byte[]>();internal DownloadBytes(Task<HttpResponseMessage> t){task=t;}internal void Start()=>task.GetAwaiter().OnCompleted(Finish);
        private void Finish(){try{var r=task.GetAwaiter().GetResult();r.EnsureSuccessStatusCode();Completion.SetResult(r.Content.Bytes!);r.Dispose();}catch(Exception e){if((task.IsCompleted && !task.IsFaulted && !task.IsCanceled))task.GetAwaiter().GetResult().Dispose();Completion.SetException(e);}}
    }
    private sealed class DownloadStream
    {
        private readonly Task<HttpResponseMessage> task;internal readonly TaskCompletionSource<Dolphin.IO.Stream> Completion=new TaskCompletionSource<Dolphin.IO.Stream>();internal DownloadStream(Task<HttpResponseMessage> t){task=t;}internal void Start()=>task.GetAwaiter().OnCompleted(Finish);
        private void Finish(){try{var r=task.GetAwaiter().GetResult();r.EnsureSuccessStatusCode();Completion.SetResult(r.Content.Body!);}catch(Exception e){if((task.IsCompleted && !task.IsFaulted && !task.IsCanceled))task.GetAwaiter().GetResult().Dispose();Completion.SetException(e);}}
    }
    private sealed class RequestWork : Operation
    {
        private readonly HttpClient owner;private readonly HttpRequestMessage request;private readonly HttpCompletionOption option;private readonly CancellationToken token;private HttpUrl url;private int redirects;private bool crossOrigin;
        private TcpClient? client;private Task? connect;private Task<int>? io;private readonly byte[] headers=new byte[8192];private byte[]? wire;private int stage,offset,used,requestOffset;private HttpResponseMessage? response;private Task<byte[]>? body;
        internal readonly TaskCompletionSource<HttpResponseMessage> Completion=new TaskCompletionSource<HttpResponseMessage>();
        internal RequestWork(HttpClient c,HttpRequestMessage r,HttpCompletionOption o,CancellationToken t):base(t){owner=c;request=r;option=o;token=t;url=new HttpUrl(r.RequestUri);}
        protected override bool Step()
        {
            if(stage==0){client=new TcpClient();connect=client.ConnectAsync(url.Host,url.Port,token);stage=1;return false;}
            if(stage==1){if(!connect!.IsCompleted)return false;connect.GetAwaiter().GetResult();wire=BuildRequest();stage=2;return false;}
            if(stage==2){if(io!=null){if(!io.IsCompleted)return false;int n=io.GetAwaiter().GetResult();if(n==0)throw new IOException("HTTP write closed.");requestOffset+=n;io=null;}if(requestOffset==wire!.Length){stage=3;return false;}io=client!.Client.SendAsync(wire,requestOffset,wire.Length-requestOffset,SocketFlags.None,token);return false;}
            if(stage==3){if(io!=null){if(!io.IsCompleted)return false;int n=io.GetAwaiter().GetResult();io=null;if(n==0)throw new IOException("HTTP headers truncated.");used+=n;}for(int i=offset;i<=used-4;i++)if(headers[i]==13&&headers[i+1]==10&&headers[i+2]==13&&headers[i+3]==10){int end=i+4;response=ParseHeaders(end);if((int)response.StatusCode>=100&&(int)response.StatusCode<200){if((int)response.StatusCode==101)throw new NotSupportedException("HTTP upgrades unavailable.");Array.Copy(headers,end,headers,0,used-end);used-=end;offset=0;return false;}if(Redirect(response)){return false;}response.Content=new HttpContent{Body=new HttpBodyStream(client!,response,headers,end,used-end,request.Method.Method=="HEAD",token),Limit=owner.MaximumResponseBytes};if(option==HttpCompletionOption.ResponseHeadersRead){Finish();return true;}body=response.Content.ReadAsByteArrayAsync();stage=4;return false;}offset=used>3?used-3:0;if(used==headers.Length)throw new IOException("HTTP headers exceed limit.");io=client!.Client.ReceiveAsync(headers,used,headers.Length-used,SocketFlags.None,token);return false;}
            if(!body!.IsCompleted)return false;body.GetAwaiter().GetResult();Finish();return true;
        }
        private void Finish(){owner.active.Remove(this);Completion.SetResult(response!);}
        private bool Redirect(HttpResponseMessage response)
        {
            int code=(int)response.StatusCode;string? location=response.Headers.Get("location");if(location==null||!(code==301||code==302||code==303||code==307||code==308))return false;
            if(request.Method.Method!="GET"&&request.Method.Method!="HEAD")return false;if(++redirects>owner.MaximumRedirects)throw new IOException("HTTP redirect limit reached.");
            string target;if(location.StartsWith("http://")||location.StartsWith("https://"))target=location;else if(location.StartsWith("//"))target=string.Concat("http:",location);else if(location.StartsWith("/"))target=string.Concat(string.Concat("http://",url.Authority),location);else{int last=0;for(int i=0;i<url.Path.Length&&url.Path[i]!='?';i++)if(url.Path[i]=='/')last=i;target=string.Concat(string.Concat(string.Concat("http://",url.Authority),url.Path.Substring(0,last+1)),location);}
            var next=new HttpUrl(target);if(next.Authority!=url.Authority)crossOrigin=true;client!.Dispose();url=next;client=null;connect=null;io=null;wire=null;response.Dispose();this.response=null;stage=offset=used=requestOffset=0;return true;
        }
        private byte[] BuildRequest()
        {
            string text=string.Concat(string.Concat(request.Method.Method," "),string.Concat(url.Path," HTTP/1.1\r\nHost: "));text=string.Concat(text,string.Concat(url.Authority,"\r\nConnection: close\r\nAccept-Encoding: identity\r\n"));
            Append(owner.DefaultRequestHeaders,ref text,crossOrigin);Append(request.Headers,ref text,crossOrigin);if(request.Content!=null){if(request.Content.Bytes==null)throw new NotSupportedException("Only bounded byte/string request content is supported.");Append(request.Content.Headers,ref text);text=string.Concat(text,string.Concat("Content-Length: ",string.Concat(request.Content.Bytes.Length.ToString(),"\r\n")));}
            text=string.Concat(text,"\r\n");var head=Encoding.ASCII.GetBytes(text);var payload=request.Content?.Bytes;if(head.Length>8192||(payload!=null&&payload.Length>65536))throw new IOException("HTTP request exceeds limits.");var all=new byte[head.Length+(payload==null?0:payload.Length)];Array.Copy(head,all,head.Length);if(payload!=null)Array.Copy(payload,0,all,head.Length,payload.Length);return all;
        }
        private static void Append(HttpHeaders source,ref string text,bool removeCredentials=false){for(int i=0;i<source.Count;i++){string name=source.Name(i);if(removeCredentials&&(name=="authorization"||name=="cookie"||name=="proxy-authorization"))continue;if(name=="host"||name=="connection"||name=="transfer-encoding"||name=="content-length"||name=="accept-encoding")throw new ArgumentException("Reserved HTTP framing header.");text=string.Concat(text,string.Concat(string.Concat(name,": "),string.Concat(source.Value(i),"\r\n")));if(text.Length>8192)throw new IOException("HTTP header limit.");}}
        private HttpResponseMessage ParseHeaders(int end)
        {
            int p=0;string status=Line(ref p,end);if(status.Length<13||!status.StartsWith("HTTP/1.1 ")&&!status.StartsWith("HTTP/1.0 ")||status[9]<'1'||status[9]>'5'||status[10]<'0'||status[10]>'9'||status[11]<'0'||status[11]>'9'||status[12]!=' ')throw new FormatException("Invalid HTTP status.");var r=new HttpResponseMessage{StatusCode=(System.Net.HttpStatusCode)((status[9]-'0')*100+(status[10]-'0')*10+status[11]-'0'),ReasonPhrase=status.Substring(13)};
            while(p<end-2){string line=Line(ref p,end);int colon=HttpText.Index(line,':');if(colon<=0)throw new FormatException("Invalid HTTP header.");string name=HttpText.Lower(line.Substring(0,colon)),value=HttpText.Trim(line.Substring(colon+1));if((name=="content-length"||name=="transfer-encoding"||name=="content-encoding")&&r.Headers.Get(name)!=null)throw new FormatException("Duplicate HTTP framing header.");r.Headers.Add(name,value);}return r;
        }
        private string Line(ref int p,int end){int start=p;while(p<end-1){if(headers[p]==13&&headers[p+1]==10){int n=p-start;p+=2;return Encoding.ASCII.GetString(headers,start,n);}if(headers[p]<32&&headers[p]!=9||headers[p]>126)throw new FormatException("Invalid HTTP header byte.");p++;}throw new FormatException("HTTP line truncated.");}
        protected override void Fail(Exception e){if(client!=null)client.Dispose();owner.active.Remove(this);Completion.SetException(e);}
    }
}
internal sealed class HttpUrl
{
    internal readonly string Host,Authority,Path;internal readonly int Port;
    internal HttpUrl(string url)
    {
        if(url==null)throw new ArgumentNullException("url");if(url.StartsWith("https://"))throw new PlatformNotSupportedException("HTTPS requires a verified TLS provider; not available in this build.");if(!url.StartsWith("http://"))throw new NotSupportedException("Only http:// URLs are supported.");for(int i=0;i<url.Length;i++)if(url[i]<=32||url[i]>126||url[i]=='#'||url[i]=='\\')throw new ArgumentException("Invalid URL; supply an ASCII escaped URL.");
        int end=7;while(end<url.Length&&url[end]!='/'&&url[end]!='?')end++;Authority=url.Substring(7,end-7);if(Authority.Length==0||HttpText.Index(Authority,'@')>=0||HttpText.Index(Authority,'[')>=0)throw new ArgumentException("Invalid IPv4 authority.");int colon=HttpText.Index(Authority,':');Host=colon<0?Authority:Authority.Substring(0,colon);Port=colon<0?80:HttpText.Number(Authority.Substring(colon+1),65535);if(Host.Length==0||Port==0)throw new ArgumentException("Invalid host or port.");Path=end==url.Length?"/":url[end]=='?'?string.Concat("/",url.Substring(end)):url.Substring(end);
    }
}
internal static class HttpText
{
    internal static int Index(string text,char value){for(int i=0;i<text.Length;i++)if(text[i]==value)return i;return -1;}
    internal static string Lower(string s){var chars=new char[s.Length];for(int i=0;i<s.Length;i++)chars[i]=s[i]>='A'&&s[i]<='Z'?(char)(s[i]+32):s[i];return new string(chars);}
    internal static string Trim(string s){int first=0,last=s.Length;while(first<last&&(s[first]==' '||s[first]=='\t'))first++;while(last>first&&(s[last-1]==' '||s[last-1]=='\t'))last--;return s.Substring(first,last-first);}
    internal static int Number(string s,int limit){if(s.Length==0)throw new FormatException();int n=0;for(int i=0;i<s.Length;i++){int d=s[i]-'0';if(d<0||d>9||n>(limit-d)/10)throw new FormatException("Number exceeds limit.");n=n*10+d;}return n;}
    internal static bool Token(char c)=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='-'||c=='_'||c=='!'||c=='#'||c=='$'||c=='%'||c=='&'||c=='\''||c=='*'||c=='+'||c=='.'||c=='^'||c=='`'||c=='|'||c=='~';
}
