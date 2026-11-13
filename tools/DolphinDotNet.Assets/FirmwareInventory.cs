using Dolphin.System;
using System.Text.Json;

namespace DolphinDotNet.Assets;

/// <summary>Metadata-only inspection. Never exports firmware, model, audio or
/// texture bytes; useful for reporting unsupported regional layouts safely.</summary>
internal static class FirmwareInventory
{
    internal static void Run(string[] args)
    {
        if(args.Length>1&&args[1]=="audition"){Audition(args);return;}
        bool menu=args.Length==4&&args[3]=="--menu-audio";
        if((args.Length!=3&&!menu)||args[1]!="inspect")throw new ArgumentException("firmware inspect INPUT [--menu-audio]");
        using var image=FirmwareImage.Load(args[2]);
        using var catalog=image.CreateCatalog(menu?FirmwareCatalogOptions.IncludeMenuAudio:FirmwareCatalogOptions.None,default);
        object? menuAudio=null;
        if(menu)
        {
            var candidates=new List<object>();
            for(int s=0;s<catalog.WaveSystemCount;s++)
            {
                var system=catalog.GetWaveSystem(s);
                for(int w=0;w<system.Count;w++)
                {
                    var wave=system[w];
                    var association=catalog.MenuAudio.Archives.SingleOrDefault(a=>a.System.Offset==system.Offset&&a.Name==wave.ArchiveName);
                    candidates.Add(new {waveSystemIndex=s,bankIndex=wave.BankIndex,bankLocalIndex=wave.BankLocalIndex,
                        flattenedIndex=w,waveId=wave.WaveId,baseKey=wave.BaseKey,archive=wave.ArchiveName,descriptorBytes=wave.DescriptorBytes,
                        codec=wave.Codec,rate=wave.SampleRate,sampleFrames=wave.SampleCount,
                        hasLoop=wave.HasLoop,loopStart=wave.LoopStart,loopEnd=wave.LoopEnd,
                        archiveRelativeOffset=wave.Offset,encodedBytes=wave.EncodedBytes,
                        association=association==null?null:(object)new {layout=association.Layout.ToString(),offset=association.Offset,length=association.Length},
                        associationStatus=association==null?"Unsupported archive association":"Structurally resolved; event identity unverified"});
                }
            }
            var effects=new Dictionary<string,object>();
            foreach(var sound in Enum.GetValues<SystemMenuSound>())
            {
                if(catalog.TryGetMenuSoundInfo(sound,out var info))effects[sound.ToString()]=info!;
                else effects[sound.ToString()]=new {unsupported=catalog.MenuAudio.UnsupportedReason};
            }
            using var stream=File.OpenRead(args[2]);
            menuAudio=new {inputSha256=Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant(),
                fingerprintStage="Explicit input file, before descrambling; evidence only, not a semantic profile",
                retainedEncodedBytes=catalog.MenuAudioRetainedBytes,candidates,effects};
        }
        var resources=new List<object>();
        for(int i=0;i<catalog.Count;i++)
        {
            var entry=catalog[i];object? model=null;
            if(entry.Kind==FirmwareResourceKind.Model)
            {
                try
                {
                    var info=catalog.InspectModel(i);
                    model=new {declaredBytes=info.DeclaredBytes,geometryValidation="NativeImportRequired",
                        sections=Enumerable.Range(0,info.SectionCount).Select(n=>new {tag=info[n].Tag,offset=info[n].Offset,length=info[n].Length}).ToArray()};
                }
                catch(Exception error) when(error is InvalidDataException or NotSupportedException)
                {model=new {geometryValidation="NativeImportRequired",error=error.Message};}
            }
            resources.Add(new {index=i,offset=entry.Offset,compressedBytes=entry.CompressedBytes,decodedBytes=entry.DecodedBytes,kind=entry.Kind.ToString(),model});
        }
        var textures=new Dictionary<string,object>();
        foreach(var name in Enum.GetValues<SystemTexture>())
        {
            try {var info=catalog.GetTexture(name);textures[name.ToString()]=new {resource=info.ResourceIndex,headerOffset=info.HeaderOffset,width=info.Width,height=info.Height,format=info.ImageFormat,mips=info.MipCount};}
            catch(Exception error) when(error is InvalidDataException or NotSupportedException)
            {textures[name.ToString()]=new {unsupported=error.Message};}
        }
        var models=new Dictionary<string,object>();
        foreach(var name in Enum.GetValues<SystemModel>())
        {
            try {models[name.ToString()]=new {resource=catalog.GetModelIndex(name)};}
            catch(Exception error) when(error is InvalidDataException or NotSupportedException)
            {models[name.ToString()]=new {unsupported=error.Message};}
        }
        object audio;
        try {var source=catalog.GetStartupAudio();audio=new {channels=2,rate=source.SampleRate,samples=source.SampleCount,duration=source.DurationSeconds,compressedBytes=source.CompressedBytes,layout=source.Layout.ToString(),archiveOffset=source.FirmwareOffset,archiveBytes=source.ArchiveBytes};}
        catch(NotSupportedException error){audio=new {unsupported=error.Message};}
        var files=catalog.Files;
        var report=new Dictionary<string,object?> {
            ["schemaVersion"]=2,["revision"]=image.Revision,["source"]=image.Source.ToString(),["encoding"]=image.Encoding.ToString(),
            ["resourceCount"]=catalog.Count,["resources"]=resources,["textures"]=textures,["models"]=models,["startupAudio"]=audio,
            ["files"]=files==null?Array.Empty<object>():Enumerable.Range(0,files.Count).Select(i=>(object)new {path=files[i].Path,offset=files[i].Offset,length=files[i].Length}).ToArray(),
            ["waveSystems"]=Enumerable.Range(0,catalog.WaveSystemCount).Select(i=>new {offset=catalog.GetWaveSystem(i).Offset,length=catalog.GetWaveSystem(i).Length,waves=catalog.GetWaveSystem(i).Count}).ToArray(),
            ["geometryValidation"]="NativeImportRequired"
        };
        if(menu)report["menuAudio"]=menuAudio;
        Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }

    // Explicit research-only sample isolation. Inspect never exports samples.
    private static void Audition(string[] args)
    {
        if(args.Length!=6)throw new ArgumentException("firmware audition INPUT WSYS_INDEX FLAT_WAVE_INDEX OUTPUT_DIRECTORY");
        int systemIndex=int.Parse(args[3]),waveIndex=int.Parse(args[4]);
        using var image=FirmwareImage.Load(args[2]);
        using var catalog=image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default);
        if(systemIndex<0||systemIndex>=catalog.WaveSystemCount)throw new ArgumentOutOfRangeException(nameof(systemIndex));
        var system=catalog.GetWaveSystem(systemIndex);
        if(waveIndex<0||waveIndex>=system.Count)throw new ArgumentOutOfRangeException(nameof(waveIndex));
        var archive=catalog.MenuAudio.Archives.SingleOrDefault(a=>a.System.Offset==system.Offset&&a.Name==system[waveIndex].ArchiveName);
        if(archive==null)throw new NotSupportedException("No validated archive association for this wave.");
        using var source=FirmwareMenuWaveSource.Capture(image.Data,archive,waveIndex,default);
        image.Dispose();catalog.Dispose();
        string output=Path.GetFullPath(args[5]);Directory.CreateDirectory(output);
        string header=Path.Combine(output,"selected.wsys"),body=Path.Combine(output,"selected.aw");
        bool headerCreated=false,bodyCreated=false;
        try
        {
            using(var file=new FileStream(header,FileMode.CreateNew)){headerCreated=true;file.Write(source.Header);}
            using(var file=new FileStream(body,FileMode.CreateNew)){bodyCreated=true;file.Write(source.Body);}
        }
        catch
        {
            if(headerCreated)File.Delete(header);if(bodyCreated)File.Delete(body);throw;
        }
        Console.WriteLine("Isolated raw wave, NOT a named menu effect. Use JAudioWaveBank entry 0 with selected.wsys and selected.aw. Keep these files private and outside source control.");
    }
}
