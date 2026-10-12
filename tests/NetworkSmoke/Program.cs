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
        Complete(NetworkRuntime.StartAsync(new NetworkOptions{DnsServers=new[]{Dolphin.Network.IPAddress.Loopback},DnsPort=15353},default));
        if(IPAddress.Parse("127.0.0.1").GetAddressBytes()[0]!=127)return 1;
        using var server=new UdpClient(0);using var client=new UdpClient(0);
        var receive=server.ReceiveAsync();var send=client.SendAsync(new byte[]{4,5,6},3,(IPEndPoint)server.Client.LocalEndPoint!);
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
        var oversized=server.ReceiveAsync();var large=client.SendAsync(new byte[1201],1201,(IPEndPoint)server.Client.LocalEndPoint!);Wait(large,large);try{Wait(oversized,oversized);return 17;}catch(System.Net.Sockets.SocketException error){if(error.ErrorCode!=0)return 18;}
        NetworkRuntime.Stop();return 0;
        } catch {return 100+phase;}
    }
    private static void Wait(System.Threading.Tasks.Task a,System.Threading.Tasks.Task b){for(int i=0;i<1000000&&(!a.IsCompleted||!b.IsCompleted);i++){NetworkRuntime.Pump();Scheduler.Pump(16);}a.GetAwaiter().GetResult();b.GetAwaiter().GetResult();}
}
