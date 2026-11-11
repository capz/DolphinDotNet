using System;
using System.Threading.Tasks;
using Dolphin.Formats;
using Dolphin.Graphics;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static readonly byte[] chunk=new byte[32768];
    private static void Fill(int width,int format,int offset,int count)
    {
        for(int i=0;i<count;i++)
        {
            int at=offset+i,x,y;
            if(format==6)
            {
                int tile=at/64,pixel=(at%32)/2;x=tile%(width/4)*4+pixel%4;y=tile/(width/4)*4+pixel/4;
                bool right=x>=width/2,bottom=y>=width/2;
                byte r=(byte)(right?0:255),g=(byte)(right?255:0),b=(byte)(bottom?255:0);
                chunk[i]=at%64<32?(at%2==0?(byte)255:r):(at%2==0?g:b);
            }
            else
            {
                int tile=at/32;x=tile%(width/8)*8+(at%32/8%2)*4;y=tile/(width/8)*8+(at%32/16)*4;
                int color=(x>=width/2?0x07e0:0xf800)|(y>=width/2?31:0);
                int p=at%8;chunk[i]=(byte)(p==0||p==2?color>>8:p==1||p==3?color:0);
            }
        }
    }
    private sealed class Lane
    {
        internal readonly int Format;
        internal readonly NativeBuffer Staging=NativeBuffer.Allocate(chunk.Length);
        internal Texture2D? Active,Retired;
        internal GpuFence LastFence,RetiredFence;
        internal DetachedTextureUpload? Upload;
        internal Task? Copy;
        internal int Width=8,Offset,Publications,LastChange;
        internal Lane(int format){Format=format;}
        internal void Pump(int frame)
        {
            if(Retired!=null&&Retired.TryDisposeAfterFence(RetiredFence))Retired=null;
            if(Copy!=null){if(!Copy.IsCompleted)return;Copy.GetAwaiter().GetResult();Copy=null;}
            if(Upload==null)
            {
                if(Active!=null&&frame-LastChange<90)return;
                Width=Active==null||Width!=8?8:256;Offset=0;
                int bytes=StreamableTextureDescriptor.NativeByteCount(Width,Width,Format);uint crc=0;
                for(int at=0;at<bytes;at+=chunk.Length){int n=Math.Min(chunk.Length,bytes-at);Fill(Width,Format,at,n);crc=StreamableTextureDescriptor.ComputeCrc32(chunk,0,n,crc);}
                Upload=DetachedTextureUpload.Create(Width,Width,(NativeTextureFormat)Format,bytes,crc);
            }
            if(Offset<Upload.ExpectedBytes)
            {
                int n=Math.Min(chunk.Length,Upload.ExpectedBytes-Offset);Fill(Width,Format,Offset,n);Staging.Write(chunk,0,0,n);
                Copy=Upload.StageChunkAsync(Staging,0,Offset,n);Offset+=n;return;
            }
            Upload.Seal();if(!Upload.TryPublish(out var next))throw new Exception("Publication failed.");
            Retired=Active;RetiredFence=LastFence;Active=next;Upload.Dispose();Upload=null;Publications++;LastChange=frame;
        }
    }
    private static int Main()
    {
        var device=GraphicsDevice.Default;using var draw=new Draw2D(device);
        var rgba=new Lane(6);var cmpr=new Lane(14);int frame=0;
        while(true)
        {
            Scheduler.Pump(8);rgba.Pump(frame);cmpr.Pump(frame);
            device.BeginFrame(new Color32(24,28,36));draw.Begin();
            if(rgba.Active!=null)draw.DrawTexture(rgba.Active,new RectF(32,80,256,256),Color32.White);
            if(cmpr.Active!=null)draw.DrawTexture(cmpr.Active,new RectF(352,80,256,256),Color32.White);
            int[] fields={0x444c,frame,rgba.Publications,cmpr.Publications,rgba.Width,cmpr.Width};
            for(int k=0;k<fields.Length;k++)for(int bit=0;bit<16;bit++)draw.FillRectangle(new RectF((k*16+bit)*6+32,400,5,8),(fields[k]&(1<<bit))!=0?Color32.White:Color32.Black);
            draw.End();var fence=device.InsertFence();rgba.LastFence=cmpr.LastFence=fence;device.EndFrame();frame++;
            Task.Delay(1).GetAwaiter().GetResult();
        }
    }
}
