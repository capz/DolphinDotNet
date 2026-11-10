using System;
using System.Net;
using System.Net.Sockets;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;
using NetworkOptions = Dolphin.Network.NetworkOptions;
using NativeTask = System.Threading.Tasks.Task;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
using IPAddress=System.Net.IPAddress;
using IPEndPoint=System.Net.IPEndPoint;
using UdpClient=System.Net.Sockets.UdpClient;
using TcpClient=System.Net.Sockets.TcpClient;
using TcpListener=System.Net.Sockets.TcpListener;
using Dolphin.Serialization.Json;
internal static class Program
{
    private static void Complete(NativeTask task){for(int i=0;i<1000000&&!task.IsCompleted;i++){NetworkRuntime.Pump();Scheduler.Pump(16);}task.GetAwaiter().GetResult();}
    private static int Main()
    {
        int phase=0;
        try {
        if(Dolphin.IO.DiscLauncher.GetSwissImagePath("gcloader:/AnimalTown.nkit.iso")!="gcldr:/AnimalTown.nkit.iso")return 26;
        var defaults=new NetworkOptions();var defaultDns=defaults.DnsServers;
        if(defaultDns.Length!=1)return 19;
        var defaultBytes=defaultDns[0].GetAddressBytes();
        if(defaultBytes[0]!=8||defaultBytes[1]!=8||defaultBytes[2]!=8||defaultBytes[3]!=8)return 20;
        defaults.DnsServers=new Dolphin.Network.IPAddress[0];
        if(defaults.DnsServers.Length!=0||new NetworkOptions().DnsServers.Length!=1)return 21;
        Complete(NetworkRuntime.StartAsync(new NetworkOptions{DnsServers=new[]{Dolphin.Network.IPAddress.Loopback},DnsPort=15353},default));
        if(IPAddress.Parse("127.0.0.1").GetAddressBytes()[0]!=127)return 1;
        using var server=new UdpClient(0);using var client=new UdpClient(0);
        // Wildcard bind addresses are not portable UDP destinations (macOS).
        var serverAddress=new IPEndPoint(IPAddress.Loopback,((IPEndPoint)server.Client.LocalEndPoint!).Port);
        var receive=server.ReceiveAsync();var send=client.SendAsync(new byte[]{4,5,6},3,serverAddress);
        Wait(send,receive);var packet=receive.GetAwaiter().GetResult();if(packet.Buffer.Length!=3||packet.Buffer[1]!=5)return 2;
        var reply=client.ReceiveAsync();var sent=server.SendAsync(packet.Buffer,3,packet.RemoteEndPoint);Wait(sent,reply);if(reply.GetAwaiter().GetResult().Buffer[2]!=6)return 3;
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var accept=listener.AcceptTcpClientAsync();using var outgoing=new TcpClient();var connect=outgoing.ConnectAsync(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port);Wait(connect,accept);using var incoming=accept.GetAwaiter().GetResult();listener.Stop();
        var bytes=new byte[3];var read=incoming.GetStream().ReadAsync(bytes,0,3);var write=outgoing.GetStream().WriteAsync(new byte[]{7,8,9},0,3);Wait(write,read);if(read.GetAwaiter().GetResult()!=3||bytes[2]!=9)return 4;
        outgoing.Dispose();var eof=incoming.GetStream().ReadAsync(bytes,0,3);Wait(eof,eof);if(eof.GetAwaiter().GetResult()!=0)return 5;
        phase=1;var dns=System.Net.Dns.GetHostAddressesAsync("config.test");Wait(dns,dns);if(dns.GetAwaiter().GetResult()[0].GetAddressBytes()[3]!=1)return 6;
        phase=2;var dnsTcp=System.Net.Dns.GetHostAddressesAsync("tcp.test");Wait(dnsTcp,dnsTcp);if(dnsTcp.GetAwaiter().GetResult()[0].GetAddressBytes()[0]!=127)return 7;
        phase=3;using var http=new System.Net.Http.HttpClient();var download=http.GetStringAsync("http://config.test:18081/json");Wait(download,download);string json=download.GetAwaiter().GetResult();var reader=new JsonReader(json);bool value=false;while(reader.Read())if(reader.TokenType==JsonTokenType.PropertyName&&reader.Value=="value"){if(!reader.Read()||reader.GetInt32()!=42)return 8;value=true;}if(!value)return 9;
        phase=4;var chunked=http.GetStringAsync("http://tcp.test:18081/chunked");Wait(chunked,chunked);if(chunked.GetAwaiter().GetResult()!=json)return 10;
        phase=5;var responseTask=http.GetAsync("http://config.test:18081/json",System.Net.Http.HttpCompletionOption.ResponseHeadersRead);Wait(responseTask,responseTask);using var response=responseTask.GetAwaiter().GetResult();var body=response.Content.ReadAsStreamAsync();Wait(body,body);using var memory=new System.IO.MemoryStream();var copy=body.GetAwaiter().GetResult().CopyToAsync(memory);Wait(copy,copy);if(memory.ToArray().Length!=json.Length)return 11;
        var truncated=http.GetStringAsync("http://config.test:18081/truncated");try{Wait(truncated,truncated);return 12;}catch(System.IO.IOException){}
        var ambiguous=http.GetStringAsync("http://config.test:18081/ambiguous");try{Wait(ambiguous,ambiguous);return 13;}catch(FormatException){}
        var cancel=new System.Threading.CancellationTokenSource();using var canceledSocket=new Dolphin.Network.UdpClient(0);var canceled=canceledSocket.ReceiveAsync(cancel.Token);cancel.Cancel();try{Complete(canceled);return 14;}catch(OperationCanceledException){}if(!canceled.IsCanceled)return 15;
        try{var bad=new JsonReader("{\"bad\":[1,]}");while(bad.Read()){}return 16;}catch(FormatException){}
        var oversized=server.ReceiveAsync();var large=client.SendAsync(new byte[1201],1201,serverAddress);Wait(large,large);try{Wait(oversized,oversized);return 17;}catch(System.Net.Sockets.SocketException error){if(error.ErrorCode!=0)return 18;}
        phase=6;
        using(var largeHttp=new Dolphin.Network.Http.HttpClient()){
            try{var rejected=largeHttp.GetAsync("http://config.test:18081/stream-iso",Dolphin.Network.Http.HttpCompletionOption.ResponseHeadersRead);Complete(rejected);return 22;}catch(System.IO.IOException){}
            largeHttp.MaximumStreamBytes=16777217;
            var headers=largeHttp.GetAsync("http://config.test:18081/stream-iso",Dolphin.Network.Http.HttpCompletionOption.ResponseHeadersRead);Complete(headers);
            // Changing the client after Send must not change this response's cap.
            largeHttp.MaximumStreamBytes=32;
            using(var largeResponse=headers.GetAwaiter().GetResult()){
                var stream=largeResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult();var buffer=new byte[4096];int total=0;
                while(true){var next=stream.ReadAsync(buffer,0,buffer.Length);Complete(next);int n=next.GetAwaiter().GetResult();if(n==0)break;for(int i=0;i<n;i++)if(buffer[i]!=88)return 23;total+=n;}
                if(total!=16777217)return 24;
            }
            foreach(string path in new[]{"/stream-chunked","/stream-close"}){
                bool rejected=false;
                try{var headersTask=largeHttp.GetAsync("http://config.test:18081"+path,Dolphin.Network.Http.HttpCompletionOption.ResponseHeadersRead);Complete(headersTask);using var limited=headersTask.GetAwaiter().GetResult();var stream=limited.Content.ReadAsStreamAsync().GetAwaiter().GetResult();var buffer=new byte[16];while(true){var next=stream.ReadAsync(buffer,0,buffer.Length);Complete(next);if(next.GetAwaiter().GetResult()==0)break;}}
                catch(System.IO.IOException){rejected=true;}catch(FormatException){rejected=true;}
                if(!rejected)return 25;
            }
        }
        NetworkRuntime.Stop();return 0;
        } catch {return 100+phase;}
    }
    private static void Wait(System.Threading.Tasks.Task a,System.Threading.Tasks.Task b){for(int i=0;i<1000000&&(!a.IsCompleted||!b.IsCompleted);i++){NetworkRuntime.Pump();Scheduler.Pump(16);}a.GetAwaiter().GetResult();b.GetAwaiter().GetResult();}
}
