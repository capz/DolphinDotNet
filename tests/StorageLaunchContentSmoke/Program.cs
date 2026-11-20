using System;
using System.IO;
using Dolphin.IO;
using Dolphin.Storage;
using StorageApi=Dolphin.Storage.Storage;

// Executed as generated C against the production storage/launch runtime.
// The Python/C hosts supply volumes and launcher arguments, not resolver results.
internal static class Program
{
    private static void Check(bool condition,string message)
    {
        if(condition)return;
        Console.WriteLine("Launch content assertion: "+message);
        throw new InvalidOperationException(message);
    }
    private static LaunchContent Success(LaunchContentResult result)
    {
        Check(result.Succeeded&&result.Failure==LaunchContentFailure.None&&result.Content!=null,"expected a content session");
        return result.Content!;
    }
    private static void Failure(LaunchContentResult result,LaunchContentFailure expected)
        =>Check(!result.Succeeded&&result.Content==null&&result.Failure==expected,"unexpected resolution failure");
    private static LaunchContentLocation Location(StorageDevice device,string directory="apps/example")
        =>new LaunchContentLocation(MountConfiguration.BlockVolume(device),directory);
    private static LaunchContentOptions Fallback(StorageDevice device)
        =>new LaunchContentOptions{Fallbacks=new[]{Location(device)}};
    private static LaunchContentOptions ImageOptions()
        =>new LaunchContentOptions{ImageMode=LaunchContentImageMode.MountImage,Validate=ValidatePayload};
    private static bool ValidatePayload(LaunchContent content)
    {
        using(var file=content.OpenRead("payload.bin")){
            var bytes=new byte[4];int at=0;
            while(at<bytes.Length){int count=file.Read(bytes,at,bytes.Length-at);if(count==0)return false;at+=count;}
            return file.Length==4&&bytes[0]==41&&bytes[1]==42&&bytes[2]==43&&bytes[3]==44;
        }
    }
    private static bool RejectContent(LaunchContent content)
    {Check(ValidatePayload(content),"rejected candidate remains readable during validation");return false;}
    private static bool ThrowValidation(LaunchContent content)
    {Check(ValidatePayload(content),"throwing validator reads its candidate");throw new InvalidOperationException("validator sentinel");}
    private static void ExpectArgument(Action action)
    {
        bool thrown=false;try{action();}catch(ArgumentException){thrown=true;}
        Check(thrown,"invalid options or relative path must raise ArgumentException");
    }
    private static string Components(int length,int count)
    {
        string part=new string('a',length),path="";
        for(int i=0;i<count;i++)path+=(i==0?"":"/")+part;
        return path;
    }
    private static void Unmounted()
    {
        for(int i=0;i<7;i++)if(i!=4&&i!=5)Check(!StorageApi.IsMounted((StorageDevice)i),"resolver left an unexpected mount");
    }
    private static void NoMountAttempts()
    {
        var mounts=StorageApi.GetMounts();
        for(int i=0;i<mounts.Length;i++)Check(mounts[i].State==MountState.NeverMounted&&mounts[i].LastNativeError==0,"invalid input must not probe or transiently mount a volume");
    }
    private static void NoArguments()
    {
        Check(LaunchContext.ArgumentCount==0,"host supplied no launch arguments");
        var result=LaunchContent.Mount();Failure(result,LaunchContentFailure.MissingLaunchPath);
        Check(result.Attempts.Length==0,"missing metadata does not probe unconfigured devices");Unmounted();
        using(var content=Success(LaunchContent.Mount(Fallback(StorageDevice.SdSerialPort2)))){
            Check(content.IsFallback&&content.RootPath=="sd:/apps/example/","explicit no-argument fallback");
            Check(ValidatePayload(content),"fallback reads actual content");
        }
        Unmounted();
    }
    private static void Captured()
    {
        string before=Dolphin.IO.Directory.GetCurrentDirectory();
        var result=LaunchContent.Mount();var content=Success(result);
        Check(content.Device==StorageDevice.GcLoader&&content.RootPath=="gcloader:/relocated/My Town \u00fc/","captured alias preserves relocated UTF-8 directory");
        Check(content.SourcePath=="gcloader:/relocated/My Town \u00fc/default.dol"&&content.LaunchPath==LaunchContext.ExecutablePath,"canonical source and original launch paths are distinct metadata");
        Check(!content.IsFallback&&content.IsValid&&content.FileSystem.IsValid,"captured source is a live direct session");
        Check(ValidatePayload(content),"captured path reads actual bytes");
        Check(content.GetPath("nested/data.bin")==content.RootPath+"nested/data.bin","relative nested asset path");
        content.Dispose();content.Dispose();
        Check(!content.IsValid&&content.RootPath=="gcloader:/relocated/My Town \u00fc/","disposed session retains metadata only");
        bool disposed=false;try{content.GetPath("payload.bin");}catch(ObjectDisposedException){disposed=true;}
        Check(disposed,"disposed content rejects path access");
        disposed=false;try{using(var file=content.OpenRead("payload.bin")){} }catch(ObjectDisposedException){disposed=true;}
        Check(disposed,"disposed content rejects file access");
        Check(Dolphin.IO.Directory.GetCurrentDirectory()==before,"resolver never changes current directory");Unmounted();
    }
    private static void Aliases()
    {
        string[] aliases={"sd","sdc","sd2","sd2sp2","SD2SP2","carda","sda","CARDA","cardb","sdb","SDB","gcldr","gcloader","GCLDR"};
        StorageDevice[] devices={StorageDevice.SdSerialPort2,StorageDevice.SdSerialPort2,StorageDevice.SdSerialPort2,StorageDevice.SdSerialPort2,StorageDevice.SdSerialPort2,
            StorageDevice.SdSlotA,StorageDevice.SdSlotA,StorageDevice.SdSlotA,StorageDevice.SdSlotB,StorageDevice.SdSlotB,StorageDevice.SdSlotB,
            StorageDevice.GcLoader,StorageDevice.GcLoader,StorageDevice.GcLoader};
        for(int i=0;i<aliases.Length;i++){
            string path=aliases[i]+":/apps/example/default.dol";
            using(var content=Success(LaunchContent.Mount(path))){
                Check(content.Device==devices[i]&&!content.IsFallback,"physical alias selects its adapter");
                Check(content.SourcePath==content.RootPath+"default.dol"&&content.LaunchPath==path,"alias normalization does not lose original path");
                Check(ValidatePayload(content),"alias reads its selected volume");
            }
            Unmounted();
        }
    }
    private sealed class IdentityValidator
    {
        internal int Calls;
        private readonly byte expected;
        internal IdentityValidator(byte expected){this.expected=expected;}
        internal bool Validate(LaunchContent content)
        {
            Calls++;
            using(var file=content.OpenRead("identity.bin")){
                var bytes=new byte[1];return file.Read(bytes,0,1)==1&&bytes[0]==expected&&ValidatePayload(content);
            }
        }
    }
    private static void OrderedFallbacks()
    {
        var validator=new IdentityValidator(7);
        var options=new LaunchContentOptions{Fallbacks=new[]{Location(StorageDevice.SdSlotA),Location(StorageDevice.GcLoader),Location(StorageDevice.SdSlotB),Location(StorageDevice.SdSerialPort2)},Validate=validator.Validate};
        var result=LaunchContent.Mount("",options);
        using(var content=Success(result)){
            Check(content.IsFallback&&content.Device==StorageDevice.SdSlotB,"first validated explicit fallback wins");
            Check(result.Attempts.Length==3&&validator.Calls==2,"ordered fallback stops without probing the fourth volume");
            Check(result.Attempts[0].Failure==LaunchContentFailure.MountFailed&&result.Attempts[1].Failure==LaunchContentFailure.ValidationRejected,"fallback report distinguishes unavailable media and rejected contents");
            Check(!StorageApi.IsMounted(StorageDevice.GcLoader)&&!StorageApi.IsMounted(StorageDevice.SdSerialPort2),"rejected owned mount is released and later candidate is untouched");
        }
        Unmounted();
        using(var content=Success(LaunchContent.Mount("relative/default.dol",Fallback(StorageDevice.SdSlotB))))Check(content.IsFallback,"relative hint uses only configured fallback");
        using(var content=Success(LaunchContent.Mount("usb:/apps/example/default.dol",Fallback(StorageDevice.SdSlotB))))Check(content.IsFallback,"unknown transport uses configured fallback");
        Failure(LaunchContent.Mount("usb:/apps/example/default.dol"),LaunchContentFailure.UnsupportedDevice);Unmounted();
    }
    private static void AuthoritativeFailure()
    {
        var options=Fallback(StorageDevice.SdSlotB);options.Validate=RejectContent;
        var result=LaunchContent.Mount("gcldr:/apps/example/default.dol",options);
        Failure(result,LaunchContentFailure.ValidationRejected);
        Check(result.Attempts.Length==1&&!StorageApi.IsMounted(StorageDevice.SdSlotB),"explicit rejection never borrows another installation");Unmounted();
        options.Validate=null;
        result=LaunchContent.Mount("gcldr:/apps/missing/default.dol",options);
        Failure(result,LaunchContentFailure.ContentNotFound);Check(result.Attempts.Length==1,"missing direct file is an authoritative failure");Unmounted();
        result=LaunchContent.Mount("sda:/apps/example/default.dol",options);
        Failure(result,LaunchContentFailure.MountFailed);Check(result.Attempts.Length==1,"unavailable explicit adapter never falls back");Unmounted();
    }
    private static void InvalidPaths()
    {
        string[] bad={"gcldr:/../default.dol","gcldr:/apps//default.dol","gcldr:/apps/./default.dol","gcldr:/apps/../default.dol",
            "gcldr:/apps\\default.dol","gcldr:/apps/a:default.dol","gcldr:/apps/\0/default.dol","gcldr:/apps/\n/default.dol",
            "gcldr:/"+new string('a',256),"gcldr:/"+new string('\u00fc',128),"gcldr:/"+Components(210,5),"gcldr:/"+Components(1,65),"gcldr:/bad\ud800path"};
        var fallback=Fallback(StorageDevice.SdSlotB);
        for(int i=0;i<bad.Length;i++){
            var result=LaunchContent.Mount(bad[i],fallback);Failure(result,LaunchContentFailure.InvalidPath);
            Check(result.Attempts.Length==0,"malformed absolute hint is rejected before mounting");NoMountAttempts();
        }
        using(var content=Success(LaunchContent.Mount("gcldr:/apps/example/default.dol"))){
            string[] children={"","/payload.bin","../payload.bin","nested/../payload.bin","nested//data.bin","./payload.bin","sd:/payload.bin","nested\\data.bin","bad\0file","bad\u007ffile",
                new string('a',256),new string('\u00fc',128),Components(210,5),Components(1,63),"bad\udc00file"};
            for(int i=0;i<children.Length;i++){
                string child=children[i];ExpectArgument(()=>content.GetPath(child));ExpectArgument(()=>content.OpenRead(child));
            }
            string boundary=Components(255,3)+"/"+new string('a',1023-content.RootPath.Length-768);
            Check(content.GetPath(boundary).Length==1023,"full path accepts exactly 1023 encoded bytes");
            ExpectArgument(()=>content.GetPath(boundary+"a"));
            Check(content.GetPath(Components(1,62)).Length>0,"combined path accepts exactly 64 components");
            Check(content.GetPath(new string('\u00fc',127)+"a").Length>0,"a component accepts exactly 255 UTF-8 bytes");
            ExpectArgument(()=>content.GetPath(null!));Check(ValidatePayload(content),"invalid child requests do not poison the session");
        }
        Unmounted();
    }
    private static void Ownership()
    {
        Check(StorageApi.Mount(StorageDevice.SdSlotB),"prepare borrowed volume");
        long borrowed=StorageApi.GetMountInfo(StorageDevice.SdSlotB).MountId;
        using(var content=Success(LaunchContent.Mount("sdb:/apps/example/default.dol")))Check(content.FileSystem.MountId==borrowed&&ValidatePayload(content),"session borrows existing mount generation");
        Check(StorageApi.IsMounted(StorageDevice.SdSlotB)&&StorageApi.GetMountInfo(StorageDevice.SdSlotB).MountId==borrowed,"borrowed mount survives disposal");
        Failure(LaunchContent.Mount("sdb:/apps/example/default.dol",new LaunchContentOptions{Validate=RejectContent}),LaunchContentFailure.ValidationRejected);
        Check(StorageApi.IsMounted(StorageDevice.SdSlotB),"borrowed mount survives rejected validation");StorageApi.Unmount(StorageDevice.SdSlotB);
        var stale=Success(LaunchContent.Mount("gcldr:/apps/example/default.dol"));long original=stale.FileSystem.MountId;
        StorageApi.Unmount(StorageDevice.GcLoader);Check(StorageApi.Remount(StorageDevice.GcLoader).Succeeded,"replace original volume generation");
        long replacement=StorageApi.GetMountInfo(StorageDevice.GcLoader).MountId;Check(replacement!=original&&!stale.IsValid,"old session detects replacement");
        bool rejected=false;try{stale.GetPath("payload.bin");}catch(DriveNotFoundException){rejected=true;}Check(rejected,"stale session cannot resolve a new path");
        rejected=false;try{using(var file=stale.OpenRead("payload.bin")){} }catch(DriveNotFoundException){rejected=true;}Check(rejected,"stale session cannot open replacement content");
        stale.Dispose();Check(StorageApi.IsMounted(StorageDevice.GcLoader)&&StorageApi.GetMountInfo(StorageDevice.GcLoader).MountId==replacement,"stale disposal preserves another owner's mount");
        StorageApi.Unmount(StorageDevice.GcLoader);Unmounted();
    }
    private static void CallbackExceptions()
    {
        var options=new LaunchContentOptions{Validate=ThrowValidation};bool thrown=false;
        try{LaunchContent.Mount("gcldr:/apps/example/default.dol",options);}catch(InvalidOperationException error){thrown=error.Message=="validator sentinel";}
        Check(thrown,"callback exception propagates after owned cleanup");Unmounted();
        Check(StorageApi.Mount(StorageDevice.SdSlotB),"prepare borrowed callback mount");thrown=false;
        try{LaunchContent.Mount("sdb:/apps/example/default.dol",options);}catch(InvalidOperationException error){thrown=error.Message=="validator sentinel";}
        Check(thrown&&StorageApi.IsMounted(StorageDevice.SdSlotB),"callback exception preserves borrowed mount");
        StorageApi.Unmount(StorageDevice.SdSlotB);Unmounted();
    }
    private static void InvalidOptions()
    {
        var valid=Location(StorageDevice.SdSlotB,"apps/example");Check(valid.Directory=="apps/example/","location normalizes trailing directory separator");
        ExpectArgument(()=>new LaunchContentLocation(null!));
        ExpectArgument(()=>new LaunchContentLocation(MountConfiguration.BlockVolume(StorageDevice.SdSlotB),"../apps"));
        ExpectArgument(()=>new LaunchContentLocation(MountConfiguration.BlockVolume(StorageDevice.SdSlotB),"sd:/apps"));
        ExpectArgument(()=>new LaunchContentLocation(MountConfiguration.BlockVolume(StorageDevice.SdSlotB),"apps//example"));
        var excessive=new LaunchContentLocation[17];for(int i=0;i<excessive.Length;i++)excessive[i]=valid;
        ExpectArgument(()=>LaunchContent.Mount("gcldr:/apps/example/default.dol",new LaunchContentOptions{Fallbacks=excessive}));
        ExpectArgument(()=>LaunchContent.Mount("gcldr:/apps/example/default.dol",new LaunchContentOptions{Fallbacks=null!}));
        ExpectArgument(()=>LaunchContent.Mount("gcldr:/apps/example/default.dol",new LaunchContentOptions{Fallbacks=new LaunchContentLocation[]{null!}}));
        ExpectArgument(()=>LaunchContent.Mount("gcldr:/apps/example/default.dol",new LaunchContentOptions{ImageMode=(LaunchContentImageMode)99}));
        ExpectArgument(()=>LaunchContent.Mount("gcldr:/apps/example/default.dol",new LaunchContentOptions{DvdSource=(LaunchContentDvdSource)99}));
        ExpectArgument(()=>LaunchContent.Mount("",new LaunchContentOptions{Fallbacks=new[]{valid,new LaunchContentLocation(MountConfiguration.DiscImage("gcloader:/../bad.iso"))}}));
        NoMountAttempts();
    }
    private static void ImageHints()
    {
        using(var content=Success(LaunchContent.Mount("gcldr:/apps/example/content.iso")))Check(content.RootPath=="gcloader:/apps/example/"&&content.Device==StorageDevice.GcLoader,"default image mode returns containing directory");Unmounted();
        using(var content=Success(LaunchContent.Mount("gcldr:/apps/example/default.dol",ImageOptions())))Check(content.RootPath=="gcloader:/apps/example/","MountImage preserves ordinary DOL directory behavior");Unmounted();
        string[] compressed={"content.nkit.iso","content.rvz","content.gcz","content.ciso","content.wia"};
        for(int i=0;i<compressed.Length;i++){
            Failure(LaunchContent.Mount("gcldr:/apps/example/"+compressed[i],ImageOptions()),LaunchContentFailure.UnsupportedImage);Unmounted();
        }
    }
    private static void DvdWithoutIdentity()
    {
        var result=LaunchContent.Mount("dvd:/apps/example/default.dol");Failure(result,LaunchContentFailure.AmbiguousSource);
        Check(result.Attempts.Length==0,"ambiguous dvd hint cannot mount without a validator");Unmounted();
    }
    private static void LongPath()
    {
        string directory=Components(180,3);
        using(var content=Success(LaunchContent.Mount("gcldr:/"+directory+"/default.dol"))){
            Check(content.RootPath=="gcloader:/"+directory+"/"&&content.RootPath.Length>512,"valid long multi-component directory is retained");
            Check(ValidatePayload(content),"valid path beyond 512 bytes reads actual content");
        }
        Unmounted();
    }
    private static void ImageOwned()
    {
        using(var content=Success(LaunchContent.Mount("gcldr:/apps/example/content.iso",ImageOptions()))){
            Check(content.Device==StorageDevice.DvdDrive&&content.RootPath=="dvd:/"&&content.SourcePath=="gcloader:/apps/example/content.iso","companion image returns FST root and canonical backing source");
            Check(content.FileSystem.Info.LastSuccessfulConfiguration!.Kind==MountKind.DiscImage&&StorageApi.IsMounted(StorageDevice.GcLoader),"image and backing source stay mounted together");
            Check(ValidatePayload(content),"mounted companion image has readable contents");
        }
        Unmounted();
        using(var content=Success(LaunchContent.Mount("gcldr:/apps/example/content.gcm",ImageOptions())))Check(content.RootPath=="dvd:/"&&ValidatePayload(content),"plain GCM is an image too");Unmounted();
    }
    private static void ImageBorrowedSource()
    {
        Check(StorageApi.Mount(StorageDevice.GcLoader),"prepare image's borrowed backing volume");long before=StorageApi.GetMountInfo(StorageDevice.GcLoader).MountId;
        using(var content=Success(LaunchContent.Mount("gcldr:/apps/example/content.iso",ImageOptions())))Check(ValidatePayload(content),"image on borrowed source reads");
        Check(!StorageApi.IsMounted(StorageDevice.DvdDrive)&&StorageApi.IsMounted(StorageDevice.GcLoader)&&StorageApi.GetMountInfo(StorageDevice.GcLoader).MountId==before,"owned image closes before preserving borrowed backing volume");
        StorageApi.Unmount(StorageDevice.GcLoader);Unmounted();
    }
    private static void ImageFailures()
    {
        var options=ImageOptions();options.Validate=RejectContent;
        Failure(LaunchContent.Mount("gcldr:/apps/example/content.iso",options),LaunchContentFailure.ValidationRejected);Unmounted();
        options.Validate=ThrowValidation;bool thrown=false;
        try{LaunchContent.Mount("gcldr:/apps/example/content.iso",options);}catch(InvalidOperationException error){thrown=error.Message=="validator sentinel";}
        Check(thrown,"image callback exception preserves original error");Unmounted();
        options.Validate=ValidatePayload;
        Failure(LaunchContent.Mount("gcldr:/apps/example/missing.iso",options),LaunchContentFailure.ContentNotFound);Unmounted();
        Failure(LaunchContent.Mount("gcldr:/apps/example/corrupt.iso",options),LaunchContentFailure.MountFailed);Unmounted();
    }
    private static void ProtectPhysicalDisc()
    {
        Check(StorageApi.Mount(MountConfiguration.Disc()).Succeeded,"prepare borrowed physical disc");long before=StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId;
        var result=LaunchContent.Mount("gcldr:/apps/example/default.dol");Failure(result,LaunchContentFailure.MountFailed);
        Check(result.Attempts.Length==1&&result.Attempts[0].MountFailure==MountFailure.ConfigurationConflict,"raw GC Loader access reports physical-disc conflict");
        Check(!StorageApi.IsMounted(StorageDevice.GcLoader)&&StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId==before,"raw probe does not disturb borrowed disc or mount GC Loader");
        Failure(LaunchContent.Mount("gcldr:/apps/example/content.iso",ImageOptions()),LaunchContentFailure.MountFailed);
        Check(StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId==before,"image request never replaces another disc");StorageApi.Unmount(StorageDevice.DvdDrive);Unmounted();
        Check(StorageApi.Mount(MountConfiguration.Disc(DiscFormat.Iso9660)).Succeeded,"prepare borrowed ISO9660 disc");before=StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId;
        result=LaunchContent.Mount("dvd:/games/External.iso",new LaunchContentOptions{DvdSource=LaunchContentDvdSource.Disc,ImageMode=LaunchContentImageMode.MountImage,Validate=ValidatePayload});
        Failure(result,LaunchContentFailure.MountFailed);
        Check(result.Attempts.Length==1&&result.Attempts[0].MountFailure==MountFailure.ConfigurationConflict,"MountImage cannot borrow a non-FST physical disc");
        Check(StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId==before,"image mode preserves borrowed ISO9660 generation");StorageApi.Unmount(StorageDevice.DvdDrive);Unmounted();
    }
    private static void ExplicitDvdSources()
    {
        using(var content=Success(LaunchContent.Mount("dvd:/apps/example/default.dol",new LaunchContentOptions{DvdSource=LaunchContentDvdSource.GcLoader,Validate=ValidatePayload})))
            Check(content.Device==StorageDevice.GcLoader&&content.RootPath=="gcloader:/apps/example/","explicit dvd alias maps to raw GC Loader");Unmounted();
        using(var content=Success(LaunchContent.Mount("dvd:/apps/example/default.dol",new LaunchContentOptions{DvdSource=LaunchContentDvdSource.Disc,Validate=ValidatePayload})))
            Check(content.Device==StorageDevice.DvdDrive&&content.RootPath=="dvd:/apps/example/","explicit disc source preserves a DOL's directory");Unmounted();
        using(var content=Success(LaunchContent.Mount("dvd:/games/External.iso",new LaunchContentOptions{DvdSource=LaunchContentDvdSource.Disc,ImageMode=LaunchContentImageMode.MountImage,Validate=ValidatePayload})))
            Check(content.Device==StorageDevice.DvdDrive&&content.RootPath=="dvd:/","explicit physical image uses mounted FST without looking for ISO inside itself");Unmounted();
        using(var content=Success(LaunchContent.Mount("dvd:/apps/example/default.dol",new LaunchContentOptions{Validate=ValidatePayload})))
            Check(content.Device==StorageDevice.DvdDrive,"automatic validated dvd source can use the physical disc");Unmounted();
    }
    private static void AutomaticDvdRaw()
    {
        using(var content=Success(LaunchContent.Mount("dvd:/apps/example/default.dol",new LaunchContentOptions{Validate=ValidatePayload}))){
            Check(content.Device==StorageDevice.GcLoader&&content.RootPath=="gcloader:/apps/example/","automatic dvd hint reaches GC Loader when no physical disc is mounted");
            Check(!StorageApi.IsMounted(StorageDevice.DvdDrive),"failed physical-disc probe does not retain a mount");
        }
        Unmounted();
    }
    private static void ReplacedImage()
    {
        var stale=Success(LaunchContent.Mount("gcldr:/apps/example/content.iso",ImageOptions()));long oldImage=stale.FileSystem.MountId;
        StorageApi.Unmount(StorageDevice.DvdDrive);StorageApi.Unmount(StorageDevice.GcLoader);
        Check(StorageApi.Mount(MountConfiguration.BlockVolume(StorageDevice.GcLoader)).Succeeded,"replace image backing generation");
        Check(StorageApi.Mount(MountConfiguration.DiscImage("gcloader:/apps/example/content.iso")).Succeeded,"replace image generation");
        long replacement=StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId;Check(replacement!=oldImage&&!stale.IsValid,"stale image invalidates");
        stale.Dispose();Check(StorageApi.IsMounted(StorageDevice.GcLoader)&&StorageApi.IsMounted(StorageDevice.DvdDrive)&&StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId==replacement,"stale image disposal preserves both new generations");
        StorageApi.Unmount(StorageDevice.DvdDrive);StorageApi.Unmount(StorageDevice.GcLoader);Unmounted();
    }
    private static void BorrowedImage()
    {
        Check(StorageApi.Mount(MountConfiguration.BlockVolume(StorageDevice.GcLoader)).Succeeded,"prepare externally mounted image source");
        Check(StorageApi.Mount(MountConfiguration.DiscImage("gcloader:/apps/example/content.iso")).Succeeded,"prepare externally mounted image");
        long source=StorageApi.GetMountInfo(StorageDevice.GcLoader).MountId,image=StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId;
        using(var content=Success(LaunchContent.Mount("gcldr:/apps/example/content.iso",ImageOptions())))Check(content.FileSystem.MountId==image&&ValidatePayload(content),"existing compatible image is borrowed");
        Check(StorageApi.GetMountInfo(StorageDevice.GcLoader).MountId==source&&StorageApi.GetMountInfo(StorageDevice.DvdDrive).MountId==image&&StorageApi.IsMounted(StorageDevice.DvdDrive),"borrowed image/source survive disposal");
        StorageApi.Unmount(StorageDevice.DvdDrive);StorageApi.Unmount(StorageDevice.GcLoader);Unmounted();
    }
    private static void CleanupFailures()
    {
        var options=new LaunchContentOptions{Fallbacks=new[]{Location(StorageDevice.GcLoader),Location(StorageDevice.SdSlotB)},Validate=RejectContent};
        var result=LaunchContent.Mount("",options);Failure(result,LaunchContentFailure.CleanupFailed);
        Check(result.Attempts.Length==1&&StorageApi.IsMounted(StorageDevice.GcLoader)&&!StorageApi.IsMounted(StorageDevice.SdSlotB),"failed rollback stops fallback before another installation");
        StorageApi.Unmount(StorageDevice.GcLoader);Unmounted();
        var content=Success(LaunchContent.Mount("gcldr:/apps/example/content.iso",ImageOptions()));
        bool failed=false;try{content.Dispose();}catch(IOException){failed=true;}
        Check(failed&&content.IsValid&&StorageApi.IsMounted(StorageDevice.GcLoader),"failed image disposal retains ownership and backing mount for retry");
        content.Dispose();Check(!content.IsValid,"retry completes disposal after a transient backend failure");Unmounted();
    }
    private static int Main()
    {
        int scenario=LaunchContext.ArgumentCount>1?int.Parse(LaunchContext.GetArgument(1)):0;
        switch(scenario){
            case 0:NoArguments();break;case 1:Captured();break;case 2:Aliases();break;case 3:OrderedFallbacks();break;
            case 4:AuthoritativeFailure();break;case 5:InvalidPaths();break;case 6:Ownership();break;case 7:CallbackExceptions();break;
            case 8:InvalidOptions();break;case 9:ImageHints();break;case 10:DvdWithoutIdentity();break;case 11:LongPath();break;
            case 20:ImageOwned();break;case 21:ImageBorrowedSource();break;case 22:ImageFailures();break;case 23:ProtectPhysicalDisc();break;
            case 24:ExplicitDvdSources();break;case 25:AutomaticDvdRaw();break;case 26:ReplacedImage();break;case 27:BorrowedImage();break;case 28:CleanupFailures();break;
            default:return 99;
        }
        return 0;
    }
}
