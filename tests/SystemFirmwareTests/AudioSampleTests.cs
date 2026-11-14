using Dolphin.System;

internal static class AudioSampleTests
{
    private static void Check(bool value){if(!value)throw new Exception("Original audio sample assertion failed");}
    private static void Throws<T>(Action action) where T:Exception
    {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    internal static void Run(Func<bool,byte[]> fixture,string? output)
    {
        Throws<ArgumentNullException>(()=>SystemResources.CreateAudioSample(null!,null!));
        foreach(bool compact in new[]{false,true})
        {
            byte[] data=fixture(compact);int descriptor=0x5000+256+2*(compact?36:44);
            FirmwareMenuRecipe.Put(data,descriptor+16,-1);
            FirmwareMenuRecipe.Put(data,descriptor+20,3);
            FirmwareMenuRecipe.Put(data,descriptor+24,14);
            using var image=FirmwareImage.Read(data);
            using var legacy=image.CreateCatalog();
            byte[] startup=legacy.GetStartupAudio().ExtractAfc();
            using var catalog=image.CreateCatalog(FirmwareCatalogOptions.IncludeSystemAudio,default);
            Check(catalog.AudioSampleCount==12&&catalog.AudioSampleRetainedBytes>0);
            Check(catalog.GetStartupAudio().ExtractAfc().SequenceEqual(startup));
            using(var combined=image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio|FirmwareCatalogOptions.IncludeSystemAudio,default))
                Check(combined.AudioSampleCount==12&&combined.GetStartupAudio().ExtractAfc().SequenceEqual(startup));
            var info=catalog.GetAudioSampleInfo(2);
            Check(info.WaveSystemIndex==0&&info.BankIndex==0&&info.WaveId==-1);
            Check(info.HasLoop&&info.LoopStartSample==3&&info.LoopEndSample==14&&info.SampleCount==16);
            Check(info.Channels==1&&info.SampleRate==32000&&info.ArchiveName=="ipl_0.aw");
            var plain=catalog.GetAudioSampleInfo(3);Check(!plain.HasLoop&&plain.LoopStartSample==0&&plain.LoopEndSample==16);
            long start=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100;i++)Check(ReferenceEquals(catalog.GetAudioSampleInfo(2),info));
            Check(GC.GetAllocatedBytesForCurrentThread()==start);
            var copy=catalog.GetAudioSamples();copy[2]=plain;Check(catalog.GetAudioSampleInfo(2)==info);
            Throws<ArgumentOutOfRangeException>(()=>catalog.GetAudioSampleInfo(-1));
            Throws<ArgumentOutOfRangeException>(()=>catalog.GetAudioSampleInfo(12));
            Throws<ArgumentOutOfRangeException>(()=>SystemResources.CreateAudioSample(catalog,info,nativeBudgetBytes:1023));
            Throws<ArgumentNullException>(()=>SystemResources.CreateAudioSample(catalog,null!));
            using var other=image.CreateCatalog(FirmwareCatalogOptions.IncludeSystemAudio,default);
            Throws<ArgumentException>(()=>SystemResources.CreateAudioSample(other,info));
            Throws<ArgumentException>(()=>SystemResources.CreateAudioSampleAsync(other,info));
            using var disabled=image.CreateCatalog();Check(disabled.AudioSampleCount==0&&disabled.GetAudioSamples().Length==0);
            Throws<NotSupportedException>(()=>SystemResources.CreateAudioSample(disabled,info));
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            Throws<OperationCanceledException>(()=>catalog.SnapshotAudioSample(info,canceled.Token));
            Throws<OperationCanceledException>(()=>SystemResources.CreateAudioSampleAsync(catalog,info,canceled.Token));
            using var source=catalog.SnapshotAudioSample(info,default);
            image.Dispose();
            Check(catalog.GetStartupAudio().ExtractAfc().SequenceEqual(startup));
            var shared=catalog.GetStartupAudio();catalog.Dispose();
            Throws<ObjectDisposedException>(()=>shared.ExtractAfc());
            Check(info.HasLoop&&info.SampleCount==16);
            var retained=FirmwareWaveSystem.Inspect(source.Header)[0];
            Check(retained.HasLoop&&retained.LoopStart==3&&retained.LoopEnd==14&&source.Body.Length==9);
            if(output!=null)
            {
                string prefix=Path.Combine(output,"auto-sample-"+(compact?0:1));
                File.WriteAllBytes(prefix+".wsys",source.Header);File.WriteAllBytes(prefix+".aw",source.Body);
            }
            Throws<ObjectDisposedException>(()=>catalog.GetAudioSampleInfo(2));
            Throws<ObjectDisposedException>(()=>catalog.GetAudioSamples());
            Throws<ObjectDisposedException>(()=>SystemResources.CreateAudioSample(catalog,info));
        }
        Console.WriteLine("Original sample metadata, copied ownership, foreign descriptors, cancellation and embedded loop snapshots passed");
    }
}
