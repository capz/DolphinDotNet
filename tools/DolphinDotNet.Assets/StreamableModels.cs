using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using DolphinDotNet.Imaging;

namespace DolphinDotNet.Assets;

internal sealed partial class GltfImport
{
    internal string StreamableOutput="";
    private readonly List<object> streamedTextures=new();
    private readonly List<object> streamedMaterials=new();
    private void ExportStreamableTextures(List<byte[]> payloads)
    {
        string directory=Path.GetDirectoryName(StreamableOutput)!;
        string texturesDirectory=StreamableOutput+".textures";Directory.CreateDirectory(texturesDirectory);
        for(int index=0;index<payloads.Count;index++)
        {
            byte[] record=payloads[index];
            int Read(int at)=>BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(at,4));
            int w=Read(0),h=Read(4);
            if(w<4||h<4||w>1024||h>1024||record.Length<28+w*h*4)throw new InvalidDataException("Invalid intermediate model image.");
            using var image=new RasterImage(w,h);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int at=28+((y/4)*(w/4)+x/4)*64+((y%4)*4+x%4)*2;
                image[x,y]=new Rgba32(record[at+1],record[at+32],record[at+33],record[at]);
            }
            int fullLevels=1;for(int n=Math.Max(w,h);n>1;n/=2)fullLevels++;
            int[]? levels=options.MaximumMipLevels==0?null:Enumerable.Range(0,Math.Min(fullLevels,options.MaximumMipLevels)).ToArray();
            var asset=DolphinDotNet.Assets.StreamableTextures.Encode(image,new StreamableTextureOptions{Mipmaps=true,LogicalLevels=levels,Format=options.StreamableFormat});
            string hash=System.Convert.ToHexString(SHA256.HashData(asset.Bytes)).ToLowerInvariant();
            string file=Path.Combine(texturesDirectory,hash+".dnlod");
            File.WriteAllBytes(file,asset.Bytes);File.WriteAllText(file+".manifest.json",asset.Manifest);
            streamedTextures.Add(new { textureIndex=index,family="textures/"+hash,file=Path.GetRelativePath(directory,file).Replace('\\','/'),sha256=hash,width=w,height=h,wrapS=Read(12),wrapT=Read(16),linear=Read(20)!=0 });
        }
    }
    internal void WriteStreamableSidecar()
    {
        var sidecar=new { version=1,model=Path.GetFileName(StreamableOutput),modelSha256=System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(StreamableOutput))).ToLowerInvariant(),embeddedTextureCount=0,textures=streamedTextures,materials=streamedMaterials };
        File.WriteAllText(StreamableOutput+".textures.json",JsonSerializer.Serialize(sidecar,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
}
