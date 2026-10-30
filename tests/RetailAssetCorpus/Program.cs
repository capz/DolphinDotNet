using System;
using System.IO;
using Dolphin.Diagnostics;
using Dolphin.Formats;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;

internal static class Program
{
    private const int MaxImages=4, MaxDirectoryEntries=2048, MaxDepth=5;
    private const int MaxCandidatesPerDisc=10, MaxSourceBytes=2*1024*1024;
    private const int MaxArchiveBytes=4*1024*1024;
    private const int MaxArchiveMembers=64;
    private const int Ok=1, Unsupported=2, Malformed=3, RenderError=4;
    private static readonly string[] images=new string[MaxImages];
    private static readonly Candidate[] candidates=new Candidate[MaxCandidatesPerDisc];
    private static int totalScanned,totalCandidates,totalComplete,totalDiscs;
    private static readonly GraphicsDevice device=GraphicsDevice.Default;

    private sealed class Candidate
    {
        public readonly string Path;
        public readonly int Kind;
        public Candidate(string path,int kind){Path=path;Kind=kind;}
    }

    // Discover only a bounded number of ISO images; never read the ISO file
    // into RAM, and never write to retail media or game save folders.
    private static int FindImages(string gamesRoot)
    {
        if(!Directory.Exists(gamesRoot))return 0;
        int count=0,visits=0;
        var folders=new string[32];
        int head=0,tail=1;
        folders[0]=gamesRoot;
        while(head<tail && visits<512)
        {
            string folder=folders[head++];
            foreach(string path in Directory.EnumerateFiles(folder))
            {
                if(++visits>512)break;
                if(!path.EndsWith(".iso",StringComparison.OrdinalIgnoreCase) &&
                   !path.EndsWith(".gcm",StringComparison.OrdinalIgnoreCase))continue;
                // Deterministic, lexicographic first four images; no unbounded sort.
                int insert=count;
                while(insert>0 && string.CompareOrdinal(path,images[insert-1])<0)
                {
                    if(insert<MaxImages)images[insert]=images[insert-1];
                    insert--;
                }
                if(insert<MaxImages)images[insert]=path;
                if(count<MaxImages)count++;
            }
            if(visits>=512)break;
            if(head < 16)
            {
                foreach(string directory in Directory.EnumerateDirectories(folder))
                {
                    if(++visits>512)break;
                    if(tail<folders.Length)folders[tail++]=directory;
                }
            }
        }
        return count;
    }

    private static int Classify(string path)
    {
        if(path.EndsWith(".bti",StringComparison.OrdinalIgnoreCase))return 1;
        if(path.EndsWith(".bmd",StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".bdl",StringComparison.OrdinalIgnoreCase))return 2;
        if(path.EndsWith(".jpc",StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".jpa",StringComparison.OrdinalIgnoreCase))return 3;
        if(path.EndsWith(".bmg",StringComparison.OrdinalIgnoreCase))return 4;
        if(path.EndsWith(".bfn",StringComparison.OrdinalIgnoreCase))return 5;
        if(path.EndsWith(".arc",StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".szs",StringComparison.OrdinalIgnoreCase))return 6;
        return 0;
    }
    private static int ScanDisc()
    {
        var dirs=new string[128];
        var depths=new int[128];
        int head=0,tail=1,count=0,visits=0;
        dirs[0]="dvd:/";depths[0]=0;
        while(head<tail && visits<MaxDirectoryEntries)
        {
            string dir=dirs[head];int depth=depths[head++];
            foreach(string path in Directory.EnumerateFiles(dir))
            {
                if(++visits>MaxDirectoryEntries)break;
                int kind=Classify(path);
                if(kind==0)continue;
                // Keep at most two samples of each kind and ten per disc.
                int matches=0;
                for(int i=0;i<count;i++)if(candidates[i].Kind==kind)matches++;
                if(matches>=2)continue;
                int at=count;
                while(at>0 && string.CompareOrdinal(path,candidates[at-1].Path)<0)
                {
                    if(at<MaxCandidatesPerDisc)candidates[at]=candidates[at-1];
                    at--;
                }
                if(at<MaxCandidatesPerDisc)candidates[at]=new Candidate(path,kind);
                if(count<MaxCandidatesPerDisc)count++;
            }
            if(visits>=MaxDirectoryEntries)break;
            if(depth<MaxDepth)
            {
                foreach(string next in Directory.EnumerateDirectories(dir))
                {
                    if(++visits>MaxDirectoryEntries)break;
                    if(tail<dirs.Length) {dirs[tail]=next;depths[tail]=depth+1;tail++;}
                }
            }
        }
        totalScanned+=visits;
        return count;
    }

    private static byte[] ReadBounded(string path,int budget)
    {
        using(var file=File.OpenRead(path))
        {
            if(file.Length<=0||file.Length>budget)
                throw new NotSupportedException("Retail asset exceeds the source fixture budget.");
            var bytes=new byte[(int)file.Length];
            int done=0;
            while(done<bytes.Length)
            {
                int n=file.Read(bytes,done,bytes.Length-done);
                if(n==0)throw new EndOfStreamException();
                done+=n;
            }
            return bytes;
        }
    }
    private static int LengthBounded(string path,int maximum)
    {
        using(var file=File.OpenRead(path))
        {
            if(file.Length<=0 || file.Length>maximum)
                throw new NotSupportedException("Source file outside bounded sample profile.");
            return (int)file.Length;
        }
    }

    private static void DrawEmpty()
    {
        device.BeginFrame(new Color32(16,20,28,255));
        device.EndFrame();
    }
    private static void DrawTexture(Texture2D texture)
    {
        using(var batch=new Draw2D(device,new Draw2DOptions{VertexCapacity=12}))
        {
            device.BeginFrame(Color32.Black);
            batch.Begin();
            // A raw texture fidelity fixture, not a production UI.
            batch.DrawTexture(texture,new RectF(0,0,batch.LogicalSize.X,batch.LogicalSize.Y),Color32.White);
            batch.End();
            device.EndFrame();
        }
    }
    private static void DrawModel(Model model)
    {
        using(var instance=model.CreateInstance())
        {
            var camera=new Camera();
            camera.LookAt(new Vector3(0,2,7),Vector3.Zero,new Vector3(0,1,0));
            var renderer=new Renderer();
            device.BeginFrame(new Color32(16,20,28,255));
            renderer.Begin3D(camera);
            renderer.DrawModel(instance,new float[]{
                1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1});
            renderer.End3D();
            device.EndFrame();
        }
    }
    private static void DrawParticles(JpcArchive archive)
    {
        if(archive.Particles.Count==0)throw new NotSupportedException("No particle IDs.");
        using(var emitter=new JpcParticleEmitter(archive,archive.Particles[0].Id,maxParticles:64,seed:0x13579bdu))
        {
            for(int i=0;i<30;i++)emitter.Update(1f/60f);
            var camera=new Camera();
            camera.LookAt(new Vector3(0,0,8),Vector3.Zero,new Vector3(0,1,0));
            var renderer=new Renderer();
            device.BeginFrame(new Color32(16,20,28,255));
            renderer.Begin3D(camera);
            emitter.Draw(renderer,camera);
            renderer.End3D();
            device.EndFrame();
        }
    }

    // Nintendo commonly packs model/texture members inside RARC. Exercise two
    // bounded resident assets from a sampled archive rather than considering
    // archive indexing alone proof that every member can render.
    private static void RenderArchiveMembers(RarcArchive archive,string outer,string discKey)
    {
        int checkedMembers=0,tested=0;
        foreach(var entry in archive.Entries)
        {
            if(++checkedMembers>MaxArchiveMembers||tested>=2)break;
            int kind=Classify(entry.Path);
            if((kind!=1&&kind!=2)||entry.Length<=0||entry.Length>MaxSourceBytes)continue;
            totalCandidates++;
            tested++;
            int result=Ok;
            string innerPath=outer+"#"+entry.Path;
            string key=discKey+"#"+innerPath;
            bool rendered=false;
            try
            {
                if(kind==1)
                {
                    var data=archive.ReadFile(entry,true);
                    if(data.Length>MaxSourceBytes||GameCubeFormatDetector.Detect(data,entry.Path)!=GameCubeFileFormat.Bti)
                        throw new NotSupportedException("Unsupported embedded BTI.");
                    using(var tex=Texture2D.FromBti(data))
                    {
                        NativeAssetCorpus.Publish(kind,Ok,entry.Length,device.NativeBytes,key);
                        DrawTexture(tex);
                        rendered=true;
                    }
                }
                else
                {
                    // The native source loader resolves path#member transparently.
                    using(var model=Model.LoadJ3dAsync(innerPath,allowMaterialApproximation:false).GetAwaiter().GetResult())
                    {
                        NativeAssetCorpus.Publish(kind,Ok,entry.Length,device.NativeBytes,key);
                        DrawModel(model);
                        rendered=true;
                    }
                }
            }
            catch(NotSupportedException){result=Unsupported;}
            catch(InvalidDataException){result=Malformed;}
            catch(Exception){result=RenderError;}
            if(!rendered)
            {
                NativeAssetCorpus.Publish(kind,result,entry.Length,device.NativeBytes,key);
                DrawEmpty();
            }
            totalComplete++;
        }
    }

    private static void RenderCandidate(Candidate item,string discKey)
    {
        int result=Ok,bytes=0;
        RarcArchive? nested=null;
        bool rendered=false;
        // Parse before opening a GPU frame so malformed input does not leave
        // the shared device in a partially submitted state.
        try
        {
            int kind=item.Kind;
            bytes=LengthBounded(item.Path,kind==6?MaxArchiveBytes:MaxSourceBytes);
            if(kind==1)
            {
                byte[] raw=ReadBounded(item.Path,MaxSourceBytes);
                if(GameCubeFormatDetector.Detect(raw,item.Path)!=GameCubeFileFormat.Bti)
                    throw new InvalidDataException("Not a supported BTI.");
                using(var texture=Texture2D.FromBti(raw))
                {
                    NativeAssetCorpus.Publish(kind,Ok,bytes,device.NativeBytes,discKey+"#"+item.Path);
                    DrawTexture(texture);rendered=true;
                }
            }
            else if(kind==2)
            {
                var header=new byte[8];
                using(var stream=File.OpenRead(item.Path))
                    if(stream.Read(header,0,8)!=8)throw new InvalidDataException("J3D header truncated.");
                if(GameCubeFormatDetector.Detect(header,item.Path)!=GameCubeFileFormat.J3d)
                    throw new InvalidDataException("Not a J3D2 BMD/BDL.");
                // Strict MAT3 compatibility: approximation is deliberately OFF.
                using(var model=Model.LoadJ3dAsync(item.Path,allowMaterialApproximation:false).GetAwaiter().GetResult())
                {
                    NativeAssetCorpus.Publish(kind,Ok,bytes,device.NativeBytes,discKey+"#"+item.Path);
                    DrawModel(model);rendered=true;
                }
            }
            else if(kind==3)
            {
                var archive=JpcArchive.Read(ReadBounded(item.Path,MaxSourceBytes));
                NativeAssetCorpus.Publish(kind,Ok,bytes,device.NativeBytes,discKey+"#"+item.Path);
                DrawParticles(archive);rendered=true;
            }
            else if(kind==4)
            {
                var catalog=BmgCatalog.Read(ReadBounded(item.Path,MaxSourceBytes));
                if(catalog.Entries.Count==0)throw new NotSupportedException("Empty BMG.");
            }
            else if(kind==5)
            {
                BfnFont.Read(ReadBounded(item.Path,MaxSourceBytes));
            }
            else if(kind==6)
            {
                nested=RarcArchive.Read(ReadBounded(item.Path,MaxArchiveBytes));
                if(nested.Entries.Count==0)throw new NotSupportedException("Empty RARC.");
                // Archive directory validation is separate from nested asset rendering.
            }
            else throw new NotSupportedException("Unknown format.");
        }
        catch(NotSupportedException){result=Unsupported;}
        catch(InvalidDataException){result=Malformed;}
        catch(Exception){result=RenderError;}
        if(!rendered)
        {
            NativeAssetCorpus.Publish(item.Kind,result,bytes,device.NativeBytes,discKey+"#"+item.Path);
            DrawEmpty();
        }
        totalComplete++;
        if(item.Kind==6 && result==Ok && nested!=null)
            RenderArchiveMembers(nested,item.Path,discKey);
    }

    private static void RunDisc(string imagePath)
    {
        // A raw image can be selected by the ODE but cannot be assumed mounted
        // as a FAT volume on every GC Loader firmware.
        if(!Storage.MountDisc(imagePath))return;
        try
        {
            int count=ScanDisc();
            var identity=Storage.GetMountedDiscIdentity();
            string discKey=identity.GameId+"-"+identity.DiscNumber+"-"+identity.Revision;
            totalCandidates+=count;
            totalDiscs++;
            for(int i=0;i<count;i++)
            {
                Controller.Update(); // synthetic PAD input is exercised every case
                RenderCandidate(candidates[i],discKey);
            }
        }
        finally {Storage.Unmount(StorageDevice.DvdDrive);}
    }

    private static int Main()
    {
        // Record an initial blank frame so the RSP runner has a rendezvous
        // even when the GC Loader mount or directory traversal fails.
        DrawEmpty();
        StorageDevice source;
        string root;
        if(Storage.Mount(StorageDevice.GcLoader) &&
           Directory.Exists("gcloader:/games"))
        {
            source=StorageDevice.GcLoader;root="gcloader:/games";
        }
        else if(Storage.Mount(StorageDevice.SdSerialPort2))
        {
            source=StorageDevice.SdSerialPort2;root="sd:/games";
        }
        else
        {
            NativeAssetCorpus.Finish(0,0,0,0);
            for(;;){Controller.Update();DrawEmpty();}
        }
        try
        {
            int count=FindImages(root);
            for(int i=0;i<count;i++)
            {
                try{RunDisc(images[i]);}
                catch(IOException){ /* unreadable retail disc, continue read-only */ }
                catch(InvalidDataException){ /* invalid/unsupported FST, continue */ }
                catch(NotSupportedException){ /* different disc/container variant */ }
            }
        }
        finally
        {
            NativeAssetCorpus.Finish(totalDiscs,totalScanned,totalCandidates,totalComplete);
            Storage.Unmount(source);
        }
        // Keep reporting state and the last capture address live for debugger
        // inspection. No exiting to Swiss, no additional filesystem writes.
        for(;;){Controller.Update();DrawEmpty();}
    }
}
