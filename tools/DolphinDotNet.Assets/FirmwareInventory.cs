using Dolphin.System;
using System.Text.Json;

namespace DolphinDotNet.Assets;

/// <summary>Metadata-only inspection. Never exports firmware, model, audio or
/// texture bytes; useful for reporting unsupported regional layouts safely.</summary>
internal static class FirmwareInventory
{
    internal static void Run(string[] args)
    {
        if(args.Length!=3||args[1]!="inspect")throw new ArgumentException("firmware inspect INPUT");
        using var image=FirmwareImage.Load(args[2]);
        using var catalog=image.CreateCatalog();
        var resources=new List<object>();
        for(int i=0;i<catalog.Count;i++)
        {
            var entry=catalog[i];object? model=null;
            if(entry.Kind==FirmwareResourceKind.UnsupportedModel)
            {
                try
                {
                    var info=catalog.InspectModel(i);
                    model=new {declaredBytes=info.DeclaredBytes,geometrySupported=false,
                        sections=Enumerable.Range(0,info.SectionCount).Select(n=>new {tag=info[n].Tag,offset=info[n].Offset,length=info[n].Length}).ToArray()};
                }
                catch(Exception error) when(error is InvalidDataException or NotSupportedException)
                {model=new {geometrySupported=false,error=error.Message};}
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
        object audio;
        try {var source=catalog.GetStartupAudio();audio=new {channels=2,rate=source.SampleRate,samples=source.SampleCount,duration=source.DurationSeconds,compressedBytes=source.CompressedBytes};}
        catch(NotSupportedException error){audio=new {unsupported=error.Message};}
        var files=catalog.Files;
        Console.WriteLine(JsonSerializer.Serialize(new {
            schemaVersion=1,revision=image.Revision,source=image.Source.ToString(),encoding=image.Encoding.ToString(),
            resourceCount=catalog.Count,resources,textures,startupAudio=audio,
            files=files==null?Array.Empty<object>():Enumerable.Range(0,files.Count).Select(i=>(object)new {path=files[i].Path,offset=files[i].Offset,length=files[i].Length}).ToArray(),
            waveSystems=Enumerable.Range(0,catalog.WaveSystemCount).Select(i=>new {offset=catalog.GetWaveSystem(i).Offset,length=catalog.GetWaveSystem(i).Length,waves=catalog.GetWaveSystem(i).Count}).ToArray(),
            geometrySupported=false
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
