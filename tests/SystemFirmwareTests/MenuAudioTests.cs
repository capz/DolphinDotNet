using Dolphin.System;
using System.Text;

internal static class MenuAudioTests
{
    private static void Check(bool value){if(!value)throw new Exception("Menu audio assertion failed");}
    private static void Throws<T>(Action action) where T:Exception
    {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static void Big(byte[] d,int at,int n){d[at]=(byte)(n>>24);d[at+1]=(byte)(n>>16);d[at+2]=(byte)(n>>8);d[at+3]=(byte)n;}
    internal static void FileTable(byte[] input)
    {
        using(var image=FirmwareImage.Read(input))
        using(var menu=image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default))
        {
            Check(menu.MenuAudio.Archives.Length==1&&menu.MenuAudio.Archives[0].Layout==FirmwareAudioLayout.FileTable);
            image.Dispose();Check(!menu.TryGetMenuSoundInfo(SystemMenuSound.Select,out _));
        }
        var overlap=(byte[])input.Clone();Big(overlap,0x20000+232+8,0);
        using(var image=FirmwareImage.Read(overlap))
            Throws<InvalidDataException>(()=>image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default));
        var outOfRange=(byte[])input.Clone();Big(outOfRange,0x20000+232+8,int.MaxValue);
        using(var image=FirmwareImage.Read(outOfRange))
            Throws<InvalidDataException>(()=>image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default));
        var duplicate=(byte[])input.Clone();Big(duplicate,0x4000+20,4);Big(duplicate,0x4000+48,14);
        using(var image=FirmwareImage.Read(duplicate))
            Throws<InvalidDataException>(()=>image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default));
    }
    internal static void Run(Func<bool,byte[]> adjacent,Func<int,byte[]> descriptor,string[] args)
    {
        Throws<ArgumentNullException>(()=>SystemResources.CreateMenuSound(null!,SystemMenuSound.Select));
        foreach(bool compact in new[]{false,true})
        {
            byte[] input=adjacent(compact);
            using var image=FirmwareImage.Read(input);
            // This legacy call must keep compiling after adding the overload.
            using var normal=image.CreateCatalog(default);
            Check(normal.Options==FirmwareCatalogOptions.None&&normal.MenuAudioRetainedBytes==0);
            Throws<ArgumentOutOfRangeException>(()=>image.CreateCatalog((FirmwareCatalogOptions)2,default));
            Throws<NotSupportedException>(()=>SystemResources.CreateMenuSound(normal,SystemMenuSound.Select));
            Throws<ArgumentOutOfRangeException>(()=>SystemResources.CreateMenuSound(normal,(SystemMenuSound)99));
            Throws<ArgumentOutOfRangeException>(()=>SystemResources.CreateMenuSound(normal,SystemMenuSound.Select,nativeBudgetBytes:1023));
            using var enabled=image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default);
            Check(enabled.MenuAudioRetainedBytes==0&&enabled.MenuAudio.Archives.Length==1);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100;i++)Check(!enabled.TryGetMenuSoundInfo(SystemMenuSound.TurnSide,out _));
            Check(GC.GetAllocatedBytesForCurrentThread()==before);
            var association=enabled.MenuAudio.Archives[0];
            Check(association.Offset==0x5400&&association.System[2].BankLocalIndex==2&&association.System[2].BankIndex==0);
            Check(association.System[2].DescriptorBytes==(compact?36:44));
            var broken=adjacent(compact);Big(broken,0x5000-16,0);
            using(var brokenImage=FirmwareImage.Read(broken))
                Throws<InvalidDataException>(()=>brokenImage.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default));
            broken=adjacent(compact);Big(broken,0x5000+4,int.MaxValue);
            using(var brokenImage=FirmwareImage.Read(broken))
                Throws<InvalidDataException>(()=>brokenImage.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default));
            broken=adjacent(compact);Big(broken,0x5000+(compact?136:180),int.MaxValue);
            using(var brokenImage=FirmwareImage.Read(broken))
                Throws<InvalidDataException>(()=>brokenImage.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default));
            broken=adjacent(compact);Big(broken,0x5000+(compact?292:300)+8,0);
            using(var brokenImage=FirmwareImage.Read(broken))
                Throws<InvalidDataException>(()=>brokenImage.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default));
            foreach(var sound in Enum.GetValues<SystemMenuSound>())
            {
                Check(!normal.TryGetMenuSoundInfo(sound,out var missing)&&missing==null);
                Check(!enabled.TryGetMenuSoundInfo(sound,out var unverified)&&unverified==null);
                Throws<NotSupportedException>(()=>SystemResources.CreateMenuSound(enabled,sound));
            }
            // Expected wave count, valid startup and valid samples do not prove
            // any menu mapping. Changed sample bytes/order remain unsupported.
            var changed=adjacent(compact);changed[0x5401]^=0x77;
            using(var changedImage=FirmwareImage.Read(changed))
            using(var unknown=changedImage.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default))
                Check(!unknown.TryGetMenuSoundInfo(SystemMenuSound.TurnSide,out _));
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            Throws<OperationCanceledException>(()=>image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,canceled.Token));
            Throws<OperationCanceledException>(()=>SystemResources.CreateMenuSound(enabled,SystemMenuSound.Select,canceled.Token));
            Throws<OperationCanceledException>(()=>FirmwareMenuWaveSource.Capture(image.Data,association,2,canceled.Token));
            using var snapshot=FirmwareMenuWaveSource.Capture(image.Data,association,2,default);
            Check(snapshot.Body.Length==9&&snapshot.Header.Length==256);
            var wave=FirmwareWaveSystem.Inspect(snapshot.Header)[0];
            Check(wave.Offset==0&&wave.SampleCount==16&&wave.SampleRate==32000&&wave.Codec==0);
            image.Dispose();enabled.Dispose();GC.Collect();
            Check(snapshot.Body.Length==9&&FirmwareWaveSystem.Inspect(snapshot.Header).Length==1);
            Throws<ObjectDisposedException>(()=>enabled.TryGetMenuSoundInfo(SystemMenuSound.Select,out _));
            Throws<ObjectDisposedException>(()=>SystemResources.CreateMenuSound(enabled,SystemMenuSound.Select));
            Throws<ObjectDisposedException>(()=>{_ = enabled.MenuAudioRetainedBytes;});
            // Relocation uses the bounded WSYS end, including stale wrappers.
            var relocated=adjacent(compact);int count=0x5400+196160-0x4fe0;
            Array.Copy(relocated,0x4fe0,relocated,0x80000,count);Array.Clear(relocated,0x4fe0,count);
            using var movedImage=FirmwareImage.Read(relocated);
            using var moved=movedImage.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default);
            Check(moved.MenuAudio.Archives[0].Offset==0x80420);
            Console.WriteLine($"Menu unknown { (compact?"compact":"extended") }: retained encoded bytes=0; selected-wave snapshot=265 bytes; no ROM retained");
        }
        // Produce original decoder vectors through the real canonicalizer,
        // independently for each source layout/codec/early-loop combination.
        string? output=args.Length==1?args[0]:null;if(output!=null)Directory.CreateDirectory(output);
        foreach(bool compact in new[]{false,true})
        for(int codec=0;codec<4;codec++)
        for(int loop=0;loop<2;loop++)
        {
            var desc=descriptor(19);
            if(compact){Big(desc,132,2);Big(desc,136,192);Big(desc,140,232);Big(desc,176,0);}
            desc[193]=(byte)codec;Big(desc,196,0x472c4400); // exactly 44100 Hz
            int bytes=codec==0?18:codec==1?10:codec==2?19:38;
            Big(desc,200,7);Big(desc,204,bytes+5);Big(desc,208,loop==1?-1:0);
            Big(desc,212,loop==1?3:-1);Big(desc,216,loop==1?17:-1);
            var data=new byte[1024];desc.CopyTo(data,0);
            for(int i=0;i<bytes;i++)data[519+i]=codec==0?(byte)(i%9==0?0:0x1e):codec==1?(byte)(i%5==0?0:0x6c):codec==2?(byte)(i%2==0?1:254):(byte)(i%2==0?0x81:0x23);
            var system=new FirmwareWaveSystemInfo(0,desc.Length,FirmwareWaveSystem.Inspect(desc));
            var archive=new FirmwareAudioArchive(system,512,256,"ipl_0.aw",FirmwareAudioLayout.FileTable);
            using var selected=FirmwareMenuWaveSource.Capture(data,archive,0,default);
            Array.Clear(data);GC.Collect();
            var info=FirmwareWaveSystem.Inspect(selected.Header)[0];
            Check(info.Codec==codec&&info.SampleCount==19&&info.SampleRate==44100&&info.HasLoop==(loop==1));
            Check(selected.Body.Length==bytes&&info.EncodedBytes==bytes&&info.Offset==0);
            if(loop==1)Check(info.LoopStart==3&&info.LoopEnd==17);
            if(output!=null)
            {
                string stem=Path.Combine(output,$"menu-{(compact?0:1)}-{codec}-{loop}");
                File.WriteAllBytes(stem+".wsys",selected.Header);File.WriteAllBytes(stem+".aw",selected.Body);
            }
        }
        Console.WriteLine("Menu API, unknown-profile rejection, cancellation, relocation and canonical wave vectors passed");
    }
}
