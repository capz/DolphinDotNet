using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dolphin.Formats;
using DolphinDotNet.Assets;
using DolphinDotNet.Imaging;

internal static class StreamableModelTests
{
    private static int I(byte[] b,int at)=>BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at,4));
    private static void Check(bool v,string message){if(!v)throw new Exception(message);}
    internal static void Run(JsonObject template,string output)
    {
        var root=template.DeepClone().AsObject();
        root["materials"]!.AsArray().Add(root["materials"]![0]!.DeepClone());
        var primitives=root["meshes"]![0]!["primitives"]!.AsArray();var second=primitives[0]!.DeepClone();second["material"]=1;primitives.Add(second);
        using(var image=new RasterImage(16,8,new Rgba32(200,40,80,255)))
        {
            image[0,0]=new Rgba32(200,40,80,128);using var png=new MemoryStream();image.SaveAsPng(png);
            root["images"]![0]!["uri"]="data:image/png;base64,"+Convert.ToBase64String(png.ToArray());
        }
        string input=Path.Combine(output,"streamed-multi.gltf"),file=Path.Combine(output,"sd","streamed-multi.dndmodel");File.WriteAllText(input,root.ToJsonString());
        ModelConverter.Convert(input,file+".resident");var legacy=File.ReadAllBytes(file+".resident");
        ModelConverter.Convert(input,file,new ImportOptions{StreamableTextures=true});var bytes=File.ReadAllBytes(file);string text=File.ReadAllText(file+".textures.json");
        using var sidecar=JsonDocument.Parse(text);var data=sidecar.RootElement;
        Check(I(bytes,I(bytes,68))==0&&I(bytes,72)==4,"Streamable models must embed no resident texture chains.");
        Check(I(bytes,I(bytes,52))==3&&data.GetProperty("materials").GetArrayLength()==3,"Different authored material slots survive native fallback deduplication.");
        Check(data.GetProperty("textures").GetArrayLength()==1,"Shared source imagery remains one family.");
        for(int i=1;i<3;i++){var m=data.GetProperty("materials")[i];Check(m.GetProperty("materialIndex").GetInt32()==i&&m.GetProperty("primaryTexture").GetInt32()==0,"Native material sidecar mapping.");}
        var texture=data.GetProperty("textures")[0];string path=Path.Combine(Path.GetDirectoryName(file)!,texture.GetProperty("file").GetString()!);byte[] asset=File.ReadAllBytes(path);var descriptor=StreamableTextureDescriptor.Read(asset);
        Check(descriptor.Count==5&&descriptor.GetLevel(0).Width==16&&descriptor.GetLevel(0).Height==8&&descriptor.DefaultFormat==6,"Model mip generation preserves rectangular dimensions and smooth alpha.");
        for(int i=0;i<descriptor.Count;i++)Check(descriptor.ValidatePayload(i,asset,descriptor.GetLevel(i).Offset),"Model sidecar level CRC.");
        Check(asset[descriptor.GetLevel(0).Offset]==128,"Smooth alpha must survive native model extraction.");
        // Skinning, animation, geometry and normalized authored UVs remain
        // byte-identical; only the second primitive's native material ID differs.
        foreach(int section in new[]{0,4,5})Check(bytes.AsSpan(I(bytes,20+section*16),I(bytes,24+section*16)).SequenceEqual(legacy.AsSpan(I(legacy,20+section*16),I(legacy,24+section*16))),"Streaming must preserve nodes/skins/animation.");
        int at=I(bytes,36)+4,oldAt=I(legacy,36)+4;
        for(int i=0;i<2;i++)
        {
            int length=44+I(bytes,at+12)*80+I(bytes,at+16)*4;
            Check(bytes.AsSpan(at,8).SequenceEqual(legacy.AsSpan(oldAt,8))&&bytes.AsSpan(at+12,length-12).SequenceEqual(legacy.AsSpan(oldAt+12,length-12)),"Streaming must preserve mesh attributes, UVs, indices and skin weights.");at+=length;oldAt+=length;
        }
        ModelConverter.Convert(input,file,new ImportOptions{StreamableTextures=true});Check(bytes.SequenceEqual(File.ReadAllBytes(file))&&text==File.ReadAllText(file+".textures.json")&&asset.SequenceEqual(File.ReadAllBytes(path)),"Deterministic model, sidecar and DNLT output.");
        string limited=file+".limited";ModelConverter.Convert(input,limited,new ImportOptions{StreamableTextures=true,MaximumMipLevels=1});
        using(var json=JsonDocument.Parse(File.ReadAllText(limited+".textures.json")))
        {var tx=json.RootElement.GetProperty("textures")[0];var d=StreamableTextureDescriptor.Read(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(limited)!,tx.GetProperty("file").GetString()!)));Check(d.Count==1,"Streamable model quality limits.");}
        foreach(var material in root["materials"]!.AsArray())
        {material!.AsObject().Remove("extensions");material["emissiveFactor"]=new JsonArray(.2,.3,.4);material["emissiveTexture"]=new JsonObject{["index"]=0};}
        File.WriteAllText(input,root.ToJsonString());string gx=file+".gx";ModelConverter.Convert(input,gx,new ImportOptions{StreamableTextures=true,CompileGxMaterials=true});
        using(var json=JsonDocument.Parse(File.ReadAllText(gx+".textures.json")))
        {var m=json.RootElement.GetProperty("materials")[1];Check(m.GetProperty("combiner").GetInt32()==2&&m.GetProperty("secondaryTexture").GetInt32()>=0&&json.RootElement.GetProperty("textures").GetArrayLength()>=2,"Secondary generated TEV maps become independent streamed families.");}
        Check(I(File.ReadAllBytes(gx),I(File.ReadAllBytes(gx),68))==0,"Generated GX maps must not become hidden resident chains.");
        ModelConverter.Convert(input,gx,new ImportOptions{StreamableTextures=true,StreamableFormat=StreamableTextureFormat.Rgba8,CompileGxMaterials=true});
        using(var json=JsonDocument.Parse(File.ReadAllText(gx+".textures.json")))
        foreach(var tx in json.RootElement.GetProperty("textures").EnumerateArray())Check(StreamableTextureDescriptor.Read(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(gx)!,tx.GetProperty("file").GetString()!))).DefaultFormat==6,"Explicit streamable model format applies to generated maps.");
        Console.WriteLine("Streamable model sidecars: shared families, zero resident images, slots, rectangular/alpha mips, UV/skinning/animation, limits and generated secondary maps passed.");
    }
}
