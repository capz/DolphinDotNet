using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Dolphin.Graphics;
using Dolphin.IO;
using Dolphin.Input;

internal static class Program
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static Task<Texture2D> Load(string url,bool aram,CancellationToken token=default)
        =>(Task<Texture2D>)typeof(SampleDownload).GetMethod("Load",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{url,aram,token})!;
    private static async Task Fails<T>(Task task) where T:Exception
    {try{await task;}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static async Task<int> Main()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        string root="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop=new CancellationTokenSource();
        var server=Serve(listener,stop.Token);
        try
        {
            using(var texture=await Load(root+"/png",false))Check(texture.Width==2,"MEM1 download decoded.");
            using(var texture=await Load(root+"/png",true))Check(texture.Width==2,"ARAM download read back and decoded.");
            Check(AramStream.Live==0,"ARAM staging released after decode.");
            await Fails<IOException>(Load(root+"/empty",true));
            await Fails<IOException>(Load(root+"/large",true));
            await Fails<System.Net.Http.HttpRequestException>(Load(root+"/error",false));
            await Fails<InvalidDataException>(Load(root+"/invalid",true));
            using(var cancel=new CancellationTokenSource(80))await Fails<OperationCanceledException>(Load(root+"/slow",true,cancel.Token));
            Check(AramStream.Live==0,"ARAM staging released on truncation, overflow, invalid image and cancellation.");
            using(var cancelled=new CancellationTokenSource())
            {cancelled.Cancel();await Fails<OperationCanceledException>(Load(root+"/png",false,cancelled.Token));}
            int initial=Texture2D.Created;
            // Request a replacement after the first displayed texture; exit after it loads.
            Controller.Read=()=>new ControllerState(Texture2D.Created==initial+1,Texture2D.Created>=initial+2);
            Check(await SampleDownload.Run(root+"/png",true)==0,"UI can retry then exit.");
            Check(Texture2D.Created==initial+2&&Texture2D.Live==0&&AramStream.Live==0,"Retry disposes replaced and final textures and staging.");
            Controller.Read=()=>new ControllerState(false,true);
            Check(await SampleDownload.Run(root+"/slow",true)==0,"Exit before first request is safe.");
            int frames=0;Controller.Read=()=>new ControllerState(false,++frames>=4);
            Check(await SampleDownload.Run(root+"/slow",true)==0&&AramStream.Live==0,"Exit cancels and settles an active response.");
            Controller.Read=()=>default;GraphicsDevice.FailAfter=4;
            await Fails<IOException>(SampleDownload.Run(root+"/slow",true));
            Check(AramStream.Live==0&&Texture2D.Live==0,"Render failure settles pending download before disposing resources.");
            Console.WriteLine("Sample download tests passed: streaming, bounds, cancellation, retry and ownership.");return 0;
        }
        finally{stop.Cancel();listener.Stop();await server;}
    }
    private static async Task Serve(TcpListener listener,CancellationToken token)
    {
        var clients=new List<Task>();
        try{while(true){var client=await listener.AcceptTcpClientAsync(token);clients.Add(Respond(client,token));}}
        catch(OperationCanceledException){}catch(SocketException){}
        await Task.WhenAll(clients);
    }
    private static async Task Respond(TcpClient client,CancellationToken token)
    {
        using(client)
        try
        {
            using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
            string request=await reader.ReadLineAsync(token)??"";while(!string.IsNullOrEmpty(await reader.ReadLineAsync(token))){}
            string path=request.Split(' ')[1];byte[] data=path=="/large"?new byte[256*1024+1]:path=="/empty"?Array.Empty<byte>():new byte[]{137,80,78,71,13,10,26,10,0};
            if(path=="/invalid")data[0]=0;
            string header="HTTP/1.1 "+(path=="/error"?"500 Error":"200 OK")+"\r\nContent-Length: "+data.Length+"\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header),token);
            if(path=="/slow")await Task.Delay(1000,token);
            await stream.WriteAsync(data,token);
        }
        catch(OperationCanceledException){}catch(IOException){}catch(SocketException){}
    }
}
internal sealed class SampleText:IDisposable
{internal void Draw(Draw2D d,string s,float x,float y,float z,Color32 c){}public void Dispose(){}}
namespace Dolphin.Graphics
{
 public readonly struct Color32 {public Color32(byte r,byte g,byte b){}public static Color32 White=>default;}
 public readonly struct RectF {public RectF(float x,float y,float w,float h){}}
 public sealed class Texture2D:IDisposable
 {
  public static int Created,Live;public int Width=>2;public int Height=>2;private bool disposed;
  public static Texture2D FromPng(byte[] bytes,bool retain){if(bytes[0]!=137)throw new InvalidDataException();Created++;Live++;return new Texture2D();}
  public void Dispose(){if(!disposed){disposed=true;Live--;}}
 }
 public sealed class GraphicsDevice {public static GraphicsDevice Default{get;}=new();public static int FailAfter;public void BeginFrame(Color32 c){if(FailAfter>0&&--FailAfter==0)throw new IOException("Injected render failure");}public void EndFrame(){}}
 public sealed class Draw2DOptions {public int Width{get;set;}public int Height{get;set;}}
 public sealed class Draw2D:IDisposable {public Draw2D(GraphicsDevice d,Draw2DOptions o){}public void Begin(){}public void End(){}public void DrawTexture(Texture2D t,RectF r,Color32 c){}public void Dispose(){}}
}
namespace Dolphin.Input
{
 public readonly struct Button {public bool WasPressed{get;}public Button(bool value){WasPressed=value;}}
 public readonly struct ControllerState {public Button A{get;}public Button B{get;}public ControllerState(bool a,bool b){A=new(a);B=new(b);}}
 public static class Controller {public static Func<ControllerState> Read=()=>default;public static void Update(){}public static ControllerState GetState()=>Read();}
}
namespace Dolphin.Memory {public static class Aram {public static bool Initialize()=>true;}}
namespace Dolphin.IO
{
 public sealed class AramStream:MemoryStream
 {public static int Live;private bool disposed;public AramStream(int capacity):base(capacity){Live++;}protected override void Dispose(bool disposing){if(!disposed){disposed=true;Live--;}base.Dispose(disposing);}}
}
namespace Dolphin.Network
{
 public sealed class NetworkOptions {public int OperationTimeoutMilliseconds{get;set;}}
 public static class NetworkRuntime
 {public static bool IsReady{get;private set;}public static Task StartAsync(NetworkOptions options,CancellationToken token){token.ThrowIfCancellationRequested();IsReady=true;return Task.CompletedTask;}public static void Stop(){IsReady=false;}}
}
