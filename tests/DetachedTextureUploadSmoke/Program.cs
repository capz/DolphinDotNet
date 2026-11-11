using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Formats;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private readonly struct CrcField
    {
        internal readonly uint Value;
        internal CrcField(uint value){Value=value;}
    }
    private static void Check(bool value){if(!value)throw new Exception("Detached upload assertion failed.");}
    private static void Drain(Task work){for(int i=0;i<10000&&!work.IsCompleted;i++)Scheduler.Pump(1);Check(work.IsCompleted);work.GetAwaiter().GetResult();}
    private static int Main()
    {
        var animation=new UvTransform(new Vector2(.2f,-.3f),new Vector2(2,3),37);
        var gutter=new UvTransform(new Vector2(.1f,.2f),new Vector2(.7f,.5f));
        var point=new Vector2(.3f,.8f);var expected=gutter.Transform(animation.Transform(point));
        var actual=animation.Then(gutter).Transform(point);
        Check(Math.Abs(expected.X-actual.X)<.00001f&&Math.Abs(expected.Y-actual.Y)<.00001f);
        Check(BitConverter.Int32BitsToSingle(0x3fc00000)==1.5f&&float.IsNaN(BitConverter.Int32BitsToSingle(0x7fc00000)));
        var reference=new CrcField(0xcbf43926u);
        uint calculated=StreamableTextureDescriptor.ComputeCrc32(new byte[]{49,50,51,52,53,54,55,56,57},0,9);
        Check(reference.Value==calculated);
        if(reference.Value!=calculated)return 9;
        Check(unchecked((int)reference.Value)<0&&~reference.Value==0x340bc6d9u);
        var bytes=new byte[256];for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)(i*17+3);
        uint crc=StreamableTextureDescriptor.ComputeCrc32(bytes,0,bytes.Length);
        using(var source=NativeBuffer.Allocate(128))
        using(var upload=DetachedTextureUpload.Create(8,8,NativeTextureFormat.Rgba8,256,crc))
        {
            Check(upload.StagedBytes==256&&!upload.TryPublish(out _));
            try{upload.Seal();return 1;}catch(InvalidOperationException){}
            source.Write(bytes,0,0,128);
            try{upload.StageChunkAsync(source,0,32,128);return 2;}catch(ArgumentOutOfRangeException){}
            try{upload.StageChunkAsync(source,0,0,31);return 3;}catch(ArgumentOutOfRangeException){}
            var first=upload.StageChunkAsync(source,0,0,128);
            try{source.Write(bytes,0,0,32);return 4;}catch(InvalidOperationException){}
            Drain(first);Check(upload.CompletedBytes==128);
            try{upload.StageChunkAsync(source,0,0,128);return 5;}catch(ArgumentOutOfRangeException){}
            source.Write(bytes,128,0,128);Drain(upload.StageChunkAsync(source,0,128,128));
            upload.Seal();Check(upload.TryPublish(out var texture)&&texture!=null&&upload.StagedBytes==0);
            Check(!texture!.TryDisposeAfterFence(default));
            var device=GraphicsDevice.Default;device.BeginFrame(Color32.Black);var fence=device.InsertFence();
            Check(!texture.TryDisposeAfterFence(fence));device.EndFrame();Check(texture.TryDisposeAfterFence(fence));
        }
        using(var source=NativeBuffer.Allocate(64))
        using(var bad=DetachedTextureUpload.Create(4,4,NativeTextureFormat.Rgba8,64,0))
        {
            source.Write(bytes,0,0,64);Drain(bad.StageChunkAsync(source,0,0,64));
            try{bad.Seal();return 6;}catch(InvalidDataException){}Check(!bad.TryPublish(out _));
        }
        using(var source=NativeBuffer.Allocate(64))
        {
            var upload=DetachedTextureUpload.Create(4,4,NativeTextureFormat.Rgba8,64,0);
            var work=upload.StageChunkAsync(source,0,0,64);
            try{upload.Dispose();return 7;}catch(InvalidOperationException){}
            Drain(upload.AbortAsync());Check(work.IsCompleted&&upload.StagedBytes==0);upload.Dispose();
        }
        using(var source=NativeBuffer.Allocate(64))
        using(var upload=DetachedTextureUpload.Create(4,4,NativeTextureFormat.Rgba8,64,0))
        using(var cancel=new CancellationTokenSource())
        {
            var work=upload.StageChunkAsync(source,0,0,64,cancel.Token);cancel.Cancel();
            try{Drain(work);return 8;}catch(OperationCanceledException){}
            Check(!upload.TryPublish(out _));Drain(upload.AbortAsync());
        }
        var heldSource=NativeBuffer.Allocate(64);heldSource.Write(bytes,0,0,64);
        using(var upload=DetachedTextureUpload.Create(4,4,NativeTextureFormat.Rgba8,64,StreamableTextureDescriptor.ComputeCrc32(bytes,0,64)))
        {
            var work=upload.StageChunkAsync(heldSource,0,0,64);heldSource.Dispose();Drain(work);
            upload.Seal();Check(upload.TryPublish(out var texture));texture!.Dispose();
        }
        return 0;
    }
}
