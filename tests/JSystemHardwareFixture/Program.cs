using System;
using System.IO;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Formats;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;

/// <summary>Headless visual fixture DOL. Finds a bounded corpus in the user's
/// own GC Loader /games directory; never writes to the mounted filesystem.
/// The host selects each rendered case with injected GameCube A presses.</summary>
internal static class Program
{
    private const int MaxGames=4, MaxCandidates=24, MaxPerGame=8;
    private const int MaxFilesPerDisc=192, MaxDirectories=48;
    private const int MaxAssetBytes=4*1024*1024;
    private const int MaxArchiveBytes=6*1024*1024;
    private static readonly Case[] cases=new Case[MaxCandidates];
    private static readonly string[] images=new string[MaxGames];
    private static int imageCount,count,discIndex=-1,filesSeen,dirsSeen,passed,skipped,failed;
    private static string problem="";
    private static Texture2D? texture;
    private static Model? model;
    private static ModelInstance? instance;
    private static Draw2D? batch;
    private static readonly GraphicsDevice device=GraphicsDevice.Default;
    private static readonly Renderer renderer=new Renderer();
    private static readonly Camera camera=new Camera();
    private static readonly float[] world=new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};

    private sealed class Case
    {
        internal string Game="",File="",Member="";
        internal int GameIndex;
        internal bool IsArchive=>Member.Length>0;
        internal string Label=>Game+"/"+File+(IsArchive?"#"+Member:"");
    }
    private static void Report(int phase,int index,int kind,int detail,string name)
        => JSystemFixtureTelemetry.Publish(phase,discIndex,imageCount,index,count,
            passed,skipped,failed,kind,detail,name);

    private static bool CandidateName(string path)
    {
        string extension=Path.GetExtension(path).ToLowerInvariant();
        return extension==".bti"||extension==".bmd"||extension==".bdl"||
            extension==".btk"||extension==".brk"||extension==".btp"||
            extension==".bva"||extension==".jpc"||extension==".bmg"||
            extension==".bfn";
    }
    private static bool ArchiveName(string path)
    {
        string extension=Path.GetExtension(path).ToLowerInvariant();
        return extension==".arc"||extension==".rarc"||extension==".szs";
    }
    private static bool ImageName(string path)
    {
        string extension=Path.GetExtension(path).ToLowerInvariant();
        return extension==".gcm"||extension==".iso";
    }
    private static void Sort(string[] array,int count)
    {
        for(int i=1;i<count;i++)
        {
            string value=array[i];int j=i;
            while(j>0&&string.CompareOrdinal(array[j-1],value)>0)
            {array[j]=array[j-1];j--;}
            array[j]=value;
        }
    }
    private static bool UnderLimit(string path,int limit)
    {
        try
        {
            using var file=File.OpenRead(path);
            return file.Length>=0x20&&file.Length<=limit;
        }
        catch(IOException){return false;}
        catch(NotSupportedException){return false;}
    }
    private static void AddCase(int game,string path,string member="")
    {
        if(count>=MaxCandidates)return;
        cases[count++]=new Case{
            GameIndex=game,Game=Path.GetFileName(images[game]),
            File=path,Member=member
        };
    }
    private static void Gather(string directory,string[] found,ref int foundCount,int depth)
    {
        if(depth>8||dirsSeen>=MaxDirectories||filesSeen>=MaxFilesPerDisc)return;
        dirsSeen++;
        try
        {
            foreach(string path in Directory.EnumerateFiles(directory))
            {
                if(++filesSeen>MaxFilesPerDisc)break;
                if((CandidateName(path)||ArchiveName(path))&&foundCount<found.Length)
                    found[foundCount++]=path;
            }
            if(filesSeen>=MaxFilesPerDisc)return;
            // At most 48 directory visits: never recurse unboundedly through
            // arbitrary retail filesystem structures.
            foreach(string sub in Directory.EnumerateDirectories(directory))
            {
                if(dirsSeen>=MaxDirectories||filesSeen>=MaxFilesPerDisc)break;
                Gather(sub,found,ref foundCount,depth+1);
            }
        }
        catch(IOException){ /* tolerate individual damaged/unreadable directories */ }
        catch(UnauthorizedAccessException){ }
    }
    private static void Discover()
    {
        bool mounted=false;
        StorageDevice deviceKind=StorageDevice.GcLoader;
        try
        {
            mounted=Storage.Mount(StorageDevice.GcLoader);
            if(!mounted||!Directory.Exists("gcloader:/games"))
            {
                if(mounted)Storage.Unmount(StorageDevice.GcLoader);
                mounted=Storage.Mount(StorageDevice.SdSerialPort2);
                deviceKind=StorageDevice.SdSerialPort2;
            }
            string root=deviceKind==StorageDevice.GcLoader?"gcloader:/games":"sd:/games";
            if(!mounted||!Directory.Exists(root)){problem="No accessible /games volume";return;}
            // Bounded list of ISO/GCM paths; never enumerate into a huge array.
            string[] discovered=new string[256];int available=0;
            foreach(string path in Directory.EnumerateFiles(root))
            {
                if(ImageName(path)&&available<discovered.Length)discovered[available++]=path;
                if(available==discovered.Length)break;
            }
            Sort(discovered,available);
            imageCount=Math.Min(MaxGames,available);
            for(int i=0;i<imageCount;i++)images[i]=discovered[i];
            for(int i=0;i<imageCount&&count<MaxCandidates;i++)
            {
                Report(JSystemFixtureTelemetry.Scanning,i,0,0,images[i]);
                if(!Storage.MountDisc(images[i])){skipped++;continue;}
                discIndex=i;
                try
                {
                    filesSeen=0;dirsSeen=0;
                    string[] entries=new string[MaxFilesPerDisc];int entryCount=0;
                    Gather("dvd:/",entries,ref entryCount,0);
                    Sort(entries,entryCount);
                    int before=count;
                    for(int j=0;j<entryCount&&count-before<MaxPerGame;j++)
                    {
                        string file=entries[j];
                        if(ArchiveName(file))
                        {
                            if(!UnderLimit(file,MaxArchiveBytes))continue;
                            try
                            {
                                var archive=RarcArchive.Read(File.ReadAllBytes(file));
                                // The archive is indexed but individual members are
                                // decoded only when selected. Never extract to disk.
                                foreach(var member in archive.Entries)
                                {
                                    if(count-before>=MaxPerGame)break;
                                    if(member.Length>MaxAssetBytes||!CandidateName(member.Path))continue;
                                    AddCase(i,file,member.Path);
                                }
                            }
                            catch(InvalidDataException){skipped++;}
                            catch(NotSupportedException){skipped++;}
                            catch(OutOfMemoryException){skipped++;}
                            catch(OverflowException){skipped++;}
                        }
                        else if(UnderLimit(file,MaxAssetBytes))AddCase(i,file);
                    }
                }
                finally{Storage.Unmount(StorageDevice.DvdDrive);}
            }
        }
        catch(IOException){problem="Failed to enumerate /games";}
        catch(UnauthorizedAccessException){problem="Cannot read /games";}
        finally
        {
            if(Storage.IsMounted(StorageDevice.DvdDrive))Storage.Unmount(StorageDevice.DvdDrive);
            if(mounted && Storage.IsMounted(deviceKind))Storage.Unmount(deviceKind);
        }
    }
    private static void Release()
    {
        instance?.Dispose();instance=null;
        model?.Dispose();model=null;
        texture?.Dispose();texture=null;
    }
    private static async Task ShowAsync(int selected)
    {
        Release();
        Case c=cases[selected];
        discIndex=c.GameIndex;
        int kind=0,detail=0;
        try
        {
            // Remount source read-only for this case. Keep the source mounted
            // until the following case so native model background loading works.
            var source=c.Game.StartsWith("sd:/",StringComparison.Ordinal) ||
                images[c.GameIndex].StartsWith("sd:/",StringComparison.Ordinal)
                ? StorageDevice.SdSerialPort2 : StorageDevice.GcLoader;
            if(!Storage.IsMounted(source) && !Storage.Mount(source))
                throw new IOException("Disc image source is not mounted.");
            if(!Storage.MountDisc(images[c.GameIndex]))
                throw new IOException("Selected disc cannot be mounted.");
            byte[] data;
            if(c.IsArchive)
            {
                if(!UnderLimit(c.File,MaxArchiveBytes))
                    throw new NotSupportedException("Archive exceeds fixture budget.");
                var arc=RarcArchive.Read(File.ReadAllBytes(c.File));
                data=arc.ReadFile(c.Member,false);
                if(data.Length>MaxAssetBytes)throw new NotSupportedException("Oversized archive member.");
                if(data.Length>=8 && GameCubeBinary.Ascii(data,0,4)=="Yaz0")
                {
                    if(GameCubeBinary.U32(data,4)>MaxAssetBytes)
                        throw new NotSupportedException("Oversized Yaz0 decoded member.");
                    data=GameCubeBinary.UnwrapYaz0(data);
                }
            }
            else
            {
                if(!UnderLimit(c.File,MaxAssetBytes))
                    throw new NotSupportedException("Asset exceeds fixture budget.");
                data=File.ReadAllBytes(c.File);
            }
            var result=JSystemFixtureProbe.Inspect(c.IsArchive?c.Member:c.File,data);
            kind=(int)result.Format;detail=result.Elements;
            if(result.Format==GameCubeFileFormat.Bti&&result.HasNativeVisual)
                texture=Texture2D.FromBti(data);
            else if(result.Format==GameCubeFileFormat.J3d&&result.HasNativeVisual)
            {
                string path=c.File+(c.IsArchive?"#"+c.Member:"");
                model=await Model.LoadJ3dAsync(path,allowMaterialApproximation:true);
                instance=model.CreateInstance();
            }

            passed++;
        }
        catch(NotSupportedException){skipped++;}
        catch(InvalidDataException){failed++;}
        catch(IOException){failed++;}
        catch(ArgumentException){failed++;}
        catch(InvalidOperationException){failed++;}
        // A known asset may exceed the console heap; keep the DOL alive and
        // expose the failure rather than advancing automatically.
        catch(OutOfMemoryException){failed++;}
        catch(OverflowException){failed++;}
        finally
        {
            Report(JSystemFixtureTelemetry.Showing,selected,kind,detail,c.Label);
        }
    }
    private static void Frame()
    {
        Color32 clear=new Color32(24,29,39);
        device.BeginFrame(clear);
        if(texture!=null)
        {
            if(batch==null)throw new InvalidOperationException("2D batch not ready.");
            batch.Begin();
            float width=texture.Width,height=texture.Height;
            float scale=Math.Min(540f/width,400f/height);
            if(scale>4)scale=4;
            width*=scale;height*=scale;
            batch.DrawTexture(texture,new RectF((640-width)/2,(480-height)/2,width,height),Color32.White);
            batch.End();
        }
        if(instance!=null)
        {
            renderer.Begin3D(camera);
            renderer.DrawModel(instance,world);
            renderer.End3D();
        }
        device.EndFrame();
    }
    private static async Task<int> Main()
    {
        Report(JSystemFixtureTelemetry.Scanning,0,0,0,"/games");
        Discover();
        camera.LookAt(new Vector3(0,1,6),Vector3.Zero,new Vector3(0,1,0));
        batch=new Draw2D(device,new Draw2DOptions{Width=640,Height=480,VertexCapacity=12});
        if(count==0)
        {
            Report(JSystemFixtureTelemetry.Unavailable,0,0,0,problem);
            while(true){Controller.Update();Frame();}
        }
        Report(JSystemFixtureTelemetry.Ready,0,0,0,"discovery-complete");
        int index=0;
        await ShowAsync(index);
        while(true)
        {
            Controller.Update();
            var pad=Controller.GetState(0);
            if(pad.A.WasPressed)
            {
                if(index+1<count)
                {
                    if(Storage.IsMounted(StorageDevice.DvdDrive))
                        Storage.Unmount(StorageDevice.DvdDrive);
                    index++;
                    await ShowAsync(index);
                }
                else Report(JSystemFixtureTelemetry.Done,index,0,0,"complete");
            }
            Frame();
        }
    }
}
