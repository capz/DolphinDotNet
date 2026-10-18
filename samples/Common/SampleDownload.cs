using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Memory;
using Dolphin.Network;
using AramStream=Dolphin.IO.AramStream;

internal static class SampleDownload
{
    private static int received;
    private const int Limit=256*1024;
    private static async Task<Texture2D> Load(string url,bool useAram,CancellationToken token)
    {
        received=0;
        if(useAram&&!Aram.Initialize())throw new InvalidOperationException("ARAM unavailable");
        if(!NetworkRuntime.IsReady)await NetworkRuntime.StartAsync(new NetworkOptions {OperationTimeoutMilliseconds=5000},token);
        using var http=new HttpClient();
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        using var source=await response.Content.ReadAsStreamAsync();
        using Stream target=useAram?(Stream)new AramStream(Limit):new MemoryStream();
        var chunk=new byte[4096];
        while(true)
        {
            int count=await source.ReadAsync(chunk,0,chunk.Length,token);if(count==0)break;
            if(received>Limit-count)throw new IOException("Image exceeds 256 KiB limit");
            await target.WriteAsync(chunk,0,count,token);received+=count;
        }
        if(received<8)throw new IOException("Image is empty or truncated");
        // ARAM is staging storage; decoding still uses a bounded MEM1 buffer.
        target.Position=0;var png=new byte[received];int at=0;
        while(at<png.Length)
        {int count=await target.ReadAsync(png,at,png.Length-at,token);if(count==0)throw new IOException("Incomplete image");at+=count;}
        token.ThrowIfCancellationRequested();return Texture2D.FromPng(png,true);
    }
    internal static async Task<int> Run(string url,bool useAram)
    {
        using var draw=new Draw2D(GraphicsDevice.Default,new Draw2DOptions {Width=640,Height=480});
        using var text=new SampleText();
        CancellationTokenSource? cancel=null;Task<Texture2D>? loading=null;Texture2D? texture=null;
        string status="A TO DOWNLOAD",progress="";int shown=-1;bool first=true,quit=false;
        Exception? failure=null;
        try
        {
            while(true)
            {
                Controller.Update();var pad=Controller.GetState();
                if(pad.B.WasPressed){quit=true;cancel?.Cancel();}
                if((first||pad.A.WasPressed)&&loading==null&&!quit)
                {first=false;cancel?.Dispose();cancel=new CancellationTokenSource();status="CONNECTING / DOWNLOADING";loading=Load(url,useAram,cancel.Token);}
                if(loading!=null&&loading.IsCompleted)
                {
                    try{var ready=loading.GetAwaiter().GetResult();texture?.Dispose();texture=ready;status="PNG READY";}
                    catch(OperationCanceledException){status="CANCELLED";}
                    catch(Exception error){status=error.Message;if(status.Length>64)status=status.Substring(0,64);}
                    loading=null;
                }
                if(quit&&loading==null)break;
                if(shown!=received){shown=received;progress=$"{received:D6} BYTES";}
                var device=GraphicsDevice.Default;device.BeginFrame(new Color32(18,30,45));draw.Begin();
                text.Draw(draw,useAram?"HTTP TO ARAM TO TEXTURE":"HTTP PNG TEXTURE",24,58,4,Color32.White);
                text.Draw(draw,status,24,92,2,new Color32(255,210,104));
                text.Draw(draw,progress,24,112,2,Color32.White);
                if(useAram)text.Draw(draw,$"ARAM STAGING CAPACITY {Limit:D6}",260,112,2,Color32.White);
                if(texture!=null)
                {
                    float scale=Math.Min(576f/texture.Width,280f/texture.Height);
                    float w=texture.Width*scale,h=texture.Height*scale;
                    draw.DrawTexture(texture,new RectF((640-w)/2,140+(280-h)/2,w,h),Color32.White);
                }
                text.Draw(draw,loading==null?"A RETRY   B EXIT   Z STATS":"B CANCEL AND EXIT   Z STATS",24,450,2,Color32.White);
                draw.End();device.EndFrame();await Task.Delay(16);
            }
        }
        catch(Exception error){failure=error;}
        // Await outside finally: ExceptionDispatchInfo is outside the AOT surface.
        cancel?.Cancel();
        if(loading!=null){try{(await loading).Dispose();}catch(Exception){}}
        cancel?.Dispose();texture?.Dispose();
        if(NetworkRuntime.IsReady)NetworkRuntime.Stop();
        if(failure!=null)throw failure;
        return 0;
    }
}
