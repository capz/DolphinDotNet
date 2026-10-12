using System;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
namespace Dolphin.Network;
public enum AddressFamily { InterNetwork=2 }
public enum SocketType { Stream=1,Dgram=2 }
public enum ProtocolType { Tcp=6,Udp=17 }
public enum SocketFlags { None=0 }
public sealed class Socket : IDisposable
{
    internal int Handle;
    private readonly bool stream;
    private bool reading,writing,connecting,accepting;
    internal Socket(int handle,bool stream){Handle=handle;this.stream=stream;}
    public Socket(AddressFamily family,SocketType type,ProtocolType protocol)
    {
        NetworkRuntime.RequireReady();if(family!=AddressFamily.InterNetwork||!((type==SocketType.Stream&&protocol==ProtocolType.Tcp)||(type==SocketType.Dgram&&protocol==ProtocolType.Udp)))throw new NotSupportedException("Only IPv4 TCP and UDP are supported.");
        stream=type==SocketType.Stream;Handle=NativeNetwork.NetOpen(stream?1:0);NetworkRuntime.Check(Handle);
    }
    internal void Check(){if(Handle<=0)throw new ObjectDisposedException("Socket");}
    internal static void Validate(byte[] buffer,int offset,int count){if(buffer==null)throw new ArgumentNullException("buffer");if(offset<0||count<0||offset>buffer.Length-count)throw new ArgumentOutOfRangeException();}
    public EndPoint LocalEndPoint=>Endpoint(false);
    public EndPoint RemoteEndPoint=>Endpoint(true);
    private IPEndPoint Endpoint(bool remote){Check();var ep=new int[2];NetworkRuntime.Check(NativeNetwork.NetEndpoint(Handle,ep,remote?1:0));return new IPEndPoint(new IPAddress(ep[0]),ep[1]);}
    public void Bind(EndPoint endpoint){Check();var ep=endpoint as IPEndPoint??throw new ArgumentException("IPv4 endpoint required.");NetworkRuntime.Check(NativeNetwork.NetBind(Handle,ep.Address.Value,ep.Port));}
    public void Listen(int backlog){Check();if(!stream)throw new InvalidOperationException();NetworkRuntime.Check(NativeNetwork.NetListen(Handle,backlog));}
    public bool NoDelay {set{Check();NetworkRuntime.Check(NativeNetwork.NetOption(Handle,0,value?1:0));}}
    public bool EnableBroadcast {set{Check();NetworkRuntime.Check(NativeNetwork.NetOption(Handle,1,value?1:0));}}
    public Task ConnectAsync(EndPoint endpoint)=>ConnectAsync(endpoint,default);
    public Task ConnectAsync(EndPoint endpoint,CancellationToken token){Check();if(connecting)throw new InvalidOperationException("Connect pending.");var ep=endpoint as IPEndPoint??throw new ArgumentException("IPv4 endpoint required.");var work=new ConnectWork(this,ep,token);NetworkRuntime.Add(work);connecting=true;return work.Completion.Task;}
    public Task<Socket> AcceptAsync()=>AcceptAsync(default);
    public Task<Socket> AcceptAsync(CancellationToken token){Check();if(!stream||accepting)throw new InvalidOperationException("Accept unavailable.");var work=new AcceptWork(this,token);NetworkRuntime.Add(work);accepting=true;return work.Completion.Task;}
    public Task<int> ReceiveAsync(byte[] buffer,int offset,int count,SocketFlags flags)=>ReceiveAsync(buffer,offset,count,flags,default);
    public Task<int> ReceiveAsync(byte[] buffer,int offset,int count,SocketFlags flags,CancellationToken token){Check();Validate(buffer,offset,count);if(!stream||flags!=SocketFlags.None||reading)throw new InvalidOperationException("Read unavailable.");var work=new IoWork(this,buffer,offset,count,true,null,token);NetworkRuntime.Add(work);reading=true;return work.Completion.Task;}
    public Task<int> SendAsync(byte[] buffer,int offset,int count,SocketFlags flags)=>SendAsync(buffer,offset,count,flags,default);
    public Task<int> SendAsync(byte[] buffer,int offset,int count,SocketFlags flags,CancellationToken token){Check();Validate(buffer,offset,count);if(!stream||flags!=SocketFlags.None||writing)throw new InvalidOperationException("Write unavailable.");var work=new IoWork(this,buffer,offset,count,false,null,token);NetworkRuntime.Add(work);writing=true;return work.Completion.Task;}
    internal Task<int> SendDatagram(byte[] buffer,int count,IPEndPoint destination,CancellationToken token){Check();Validate(buffer,0,count);if(stream||writing||count>65507)throw new InvalidOperationException("UDP send unavailable.");var work=new IoWork(this,buffer,0,count,false,destination,token);NetworkRuntime.Add(work);writing=true;return work.Completion.Task;}
    internal Task<UdpReceiveResult> ReceiveDatagram(int capacity,CancellationToken token){Check();if(stream||reading||capacity<0||capacity>65507)throw new InvalidOperationException("UDP receive unavailable.");var work=new DatagramWork(this,capacity,token);NetworkRuntime.Add(work);reading=true;return work.Completion.Task;}
    public int TryReceiveFrom(byte[] buffer,int offset,int count,out IPEndPoint remote)
    {
        Check();Validate(buffer,offset,count);if(stream||reading)throw new InvalidOperationException();var ep=new int[2];int n=NativeNetwork.NetRead(Handle,buffer,offset,count,ep);NetworkRuntime.Check(n);remote=new IPEndPoint(new IPAddress(ep[0]),ep[1]);return n;
    }
    /// <summary>Allocation-free UDP polling; endpoint[0] is IPv4 bits, endpoint[1] the port. -1 means no packet.</summary>
    public int TryReceiveFrom(byte[] buffer,int offset,int count,int[] endpoint){Check();Validate(buffer,offset,count);if(stream||reading||endpoint==null||endpoint.Length<2)throw new InvalidOperationException();int n=NativeNetwork.NetRead(Handle,buffer,offset,count,endpoint);NetworkRuntime.Check(n);return n;}
    public int TrySendTo(byte[] buffer,int offset,int count,IPEndPoint remote){Check();Validate(buffer,offset,count);if(stream||writing)throw new InvalidOperationException();int n=NativeNetwork.NetWrite(Handle,buffer,offset,count,remote.Address.Value,remote.Port);NetworkRuntime.Check(n);return n;}
    public void Close()=>Dispose();
    public void Dispose(){if(Handle>0){NativeNetwork.NetClose(Handle);Handle=0;}}
    private sealed class ConnectWork : Operation
    {
        private readonly Socket socket;private readonly IPEndPoint endpoint;private bool begun;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal ConnectWork(Socket s,IPEndPoint ep,CancellationToken t):base(t){socket=s;endpoint=ep;}
        protected override bool Step(){socket.Check();int r=begun?NativeNetwork.NetConnected(socket.Handle):NativeNetwork.NetConnect(socket.Handle,endpoint.Address.Value,endpoint.Port);begun=true;NetworkRuntime.Check(r);if(r==-1)return false;socket.connecting=false;Completion.SetResult(0);return true;}
        protected override void Fail(Exception e){socket.connecting=false;socket.Dispose();Completion.SetException(e);}
    }
    private sealed class AcceptWork : Operation
    {
        private readonly Socket socket;private readonly int[] ep=new int[2];internal readonly TaskCompletionSource<Socket> Completion=new TaskCompletionSource<Socket>();
        internal AcceptWork(Socket s,CancellationToken t):base(t){socket=s;}
        protected override bool Step(){socket.Check();int h=NativeNetwork.NetAccept(socket.Handle,ep);NetworkRuntime.Check(h);if(h==-1)return false;socket.accepting=false;Completion.SetResult(new Socket(h,true));return true;}
        protected override void Fail(Exception e){socket.accepting=false;Completion.SetException(e);}
    }
    private sealed class IoWork : Operation
    {
        private readonly Socket socket;private readonly byte[] buffer;private readonly bool read;private readonly IPEndPoint? destination;private int offset,count;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal IoWork(Socket s,byte[] b,int o,int n,bool r,IPEndPoint? ep,CancellationToken t):base(t){socket=s;buffer=b;offset=o;count=n;read=r;destination=ep;}
        protected override bool Step(){socket.Check();int limit=count>4096&&destination==null?4096:count;int n=read?NativeNetwork.NetRead(socket.Handle,buffer,offset,limit,null!):NativeNetwork.NetWrite(socket.Handle,buffer,offset,limit,destination==null?0:destination.Address.Value,destination==null?0:destination.Port);NetworkRuntime.Check(n);if(n==-1)return false;if(destination!=null&&n!=count)throw new SocketException(0,-2);if(read)socket.reading=false;else socket.writing=false;Completion.SetResult(n);return true;}
        protected override void Fail(Exception e){if(read)socket.reading=false;else socket.writing=false;Completion.SetException(e);}
    }
    private sealed class DatagramWork : Operation
    {
        private readonly Socket socket;private readonly byte[] buffer;private readonly int[] ep=new int[2];internal readonly TaskCompletionSource<UdpReceiveResult> Completion=new TaskCompletionSource<UdpReceiveResult>();
        internal DatagramWork(Socket s,int n,CancellationToken t):base(t){socket=s;buffer=new byte[n];}
        protected override bool Step(){socket.Check();int n=NativeNetwork.NetRead(socket.Handle,buffer,0,buffer.Length,ep);NetworkRuntime.Check(n);if(n==-1)return false;var bytes=new byte[n];Array.Copy(buffer,bytes,n);socket.reading=false;Completion.SetResult(new UdpReceiveResult(bytes,new IPEndPoint(new IPAddress(ep[0]),ep[1])));return true;}
        protected override void Fail(Exception e){socket.reading=false;Completion.SetException(e);}
    }
}
public struct UdpReceiveResult
{
    public byte[] Buffer {get;}
    public IPEndPoint RemoteEndPoint {get;}
    public UdpReceiveResult(byte[] buffer,IPEndPoint remote){Buffer=buffer;RemoteEndPoint=remote;}
}
public sealed class UdpClient : IDisposable
{
    public Socket Client {get;}
    private IPEndPoint? remote;
    public int MaximumDatagramBytes {get;set;}=1200;
    public UdpClient():this(0){}
    public UdpClient(int port):this(new IPEndPoint(IPAddress.Any,port)){}
    public UdpClient(IPEndPoint local){Client=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);try{Client.Bind(local);}catch{Client.Dispose();throw;}}
    public void Connect(IPEndPoint endpoint){remote=endpoint??throw new ArgumentNullException("endpoint");}
    public Task<int> SendAsync(byte[] buffer,int count,IPEndPoint destination)=>SendAsync(buffer,count,destination,default);
    public Task<int> SendAsync(byte[] buffer,int count,IPEndPoint destination,CancellationToken token)=>Client.SendDatagram(buffer,count,destination,token);
    public Task<int> SendAsync(byte[] buffer,int count)=>Client.SendDatagram(buffer,count,remote??throw new InvalidOperationException("No default endpoint."),default);
    public Task<UdpReceiveResult> ReceiveAsync()=>ReceiveAsync(default);
    public Task<UdpReceiveResult> ReceiveAsync(CancellationToken token)=>Client.ReceiveDatagram(MaximumDatagramBytes,token);
    public void Dispose()=>Client.Dispose();
    public void Close()=>Dispose();
}
public sealed class TcpClient : IDisposable
{
    public Socket Client {get;private set;}
    private NetworkStream? stream;
    public TcpClient(){Client=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);}
    internal TcpClient(Socket socket){Client=socket;}
    public bool NoDelay {set=>Client.NoDelay=value;}
    public Task ConnectAsync(IPAddress address,int port)=>Client.ConnectAsync(new IPEndPoint(address,port));
    public Task ConnectAsync(string host,int port)=>ConnectAsync(host,port,default);
    public Task ConnectAsync(string host,int port,CancellationToken token){var work=new HostConnect(this,host,port,token);work.Start();return work.Completion.Task;}
    public NetworkStream GetStream(){if(stream==null)stream=new NetworkStream(Client,true);return stream;}
    public void Close()=>Dispose();
    public void Dispose(){Client.Dispose();}
    private sealed class HostConnect
    {
        private readonly TcpClient owner;private readonly string host;private readonly int port;private readonly CancellationToken token;private Task<IPAddress[]>? lookup;private Task? connect;private IPAddress[]? addresses;private int index;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal HostConnect(TcpClient c,string h,int p,CancellationToken t){owner=c;host=h;port=p;token=t;}
        internal void Start(){try{lookup=Dns.GetHostAddressesAsync(host,token);lookup.GetAwaiter().OnCompleted(Step);}catch(Exception e){owner.Dispose();Completion.SetException(e);}}
        private void Step(){try{token.ThrowIfCancellationRequested();if(addresses==null)addresses=lookup!.GetAwaiter().GetResult();if(connect!=null){try{connect.GetAwaiter().GetResult();Completion.SetResult(0);return;}catch{if(index>=addresses.Length)throw;owner.Client=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);}}if(index>=addresses.Length)throw new SocketException(0,-2);connect=owner.Client.ConnectAsync(new IPEndPoint(addresses[index++],port),token);connect.GetAwaiter().OnCompleted(Step);}catch(Exception e){owner.Dispose();Completion.SetException(e);}}
    }
}
public sealed class TcpListener
{
    private readonly IPEndPoint endpoint;private Socket? socket;
    public TcpListener(IPAddress address,int port){endpoint=new IPEndPoint(address,port);}
    public TcpListener(IPEndPoint endpoint){this.endpoint=endpoint;}
    public EndPoint LocalEndpoint=>socket==null?endpoint:socket.LocalEndPoint;
    public void Start()=>Start(4);
    public void Start(int backlog){if(socket!=null)throw new InvalidOperationException("Already listening.");socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);try{socket.Bind(endpoint);socket.Listen(backlog);}catch{Stop();throw;}}
    public Task<TcpClient> AcceptTcpClientAsync()=>AcceptTcpClientAsync(default);
    public Task<TcpClient> AcceptTcpClientAsync(CancellationToken token){if(socket==null)throw new InvalidOperationException("Not listening.");var work=new AcceptClient(socket.AcceptAsync(token));work.Start();return work.Completion.Task;}
    public void Stop(){if(socket!=null){socket.Dispose();socket=null;}}
    private sealed class AcceptClient
    {
        private readonly Task<Socket> accept;internal readonly TaskCompletionSource<TcpClient> Completion=new TaskCompletionSource<TcpClient>();
        internal AcceptClient(Task<Socket> task){accept=task;}
        internal void Start()=>accept.GetAwaiter().OnCompleted(Finish);
        private void Finish(){try{Completion.SetResult(new TcpClient(accept.GetAwaiter().GetResult()));}catch(Exception e){Completion.SetException(e);}}
    }
}
