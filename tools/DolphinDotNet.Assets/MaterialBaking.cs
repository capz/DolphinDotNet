using DolphinDotNet.Imaging;
using System.Text.Json;

namespace DolphinDotNet.Assets;

internal sealed partial class GltfImport
{
    // DNMD uses one texture and one UV stream. When approximation is explicitly
    // enabled, resolve compatible glTF base/emission/occlusion textures offline.
    // This is not physically based rendering: emissive is baked into diffuse,
    // and metallic/roughness/normal inputs remain approximation-only.
    private readonly Dictionary<int,int> bakedMaterials = new();

    private static int Dimension(int input,int maximum)
    {
        int result=4;
        while(result<input&&result<maximum)result*=2;
        return result;
    }

    private RasterImage DecodeSourceTexture(int index)
    {
        var textures=Items("textures");var images=Items("images");
        if((uint)index>=textures.Length)throw new InvalidDataException("Invalid material texture index.");
        var texture=textures[index];int source=Int(texture,"source",-1);
        if((uint)source>=images.Length)throw new InvalidDataException("Invalid material image.");
        var img=images[source];
        byte[] bytes=img.TryGetProperty("uri",out var uri)?ReadUri(uri.GetString()!):View(Int(img,"bufferView",-1));
        return RasterImage.Load(bytes);
    }

    private (int wrapS,int wrapT,int linear) SamplerOf(int textureIndex)
    {
        var textures=Items("textures");var samplers=Items("samplers");
        var tx=textures[textureIndex];int index=Int(tx,"sampler",-1);
        if(index< -1||index>=samplers.Length)throw new InvalidDataException("Invalid texture sampler.");
        var sampler=index<0?default:samplers[index];
        return(
            Wrap(sampler.ValueKind==JsonValueKind.Undefined?10497:Int(sampler,"wrapS",10497)),
            Wrap(sampler.ValueKind==JsonValueKind.Undefined?10497:Int(sampler,"wrapT",10497)),
            sampler.ValueKind==JsonValueKind.Undefined||Int(sampler,"magFilter",9729)!=9728?1:0);
    }

    private static (int set,float[] offset,float[] scale,float rotation) TextureUv(JsonElement tx)
    {
        int set=Int(tx,"texCoord");
        float[] offset=[0,0],scale=[1,1];float rotation=0;
        if(tx.TryGetProperty("extensions",out var extensions))
        {
            foreach(var extension in extensions.EnumerateObject())
                if(extension.Name!="KHR_texture_transform")
                    throw new InvalidDataException("Unsupported secondary material texture extension.");
            if(extensions.TryGetProperty("KHR_texture_transform",out var tr))
            {
                set=Int(tr,"texCoord",set);
                offset=Values(tr,"offset",offset);
                scale=Values(tr,"scale",scale);
                rotation=Float(tr,"rotation");
            }
        }
        if(set is not (0 or 1)||offset.Length!=2||scale.Length!=2||
           !float.IsFinite(rotation)||offset.Any(x=>!float.IsFinite(x))||
           scale.Any(x=>!float.IsFinite(x)))throw new InvalidDataException("Invalid material texture UV transform.");
        return(set,offset,scale,rotation);
    }

    private static bool SameUv(
        (int set,float[] offset,float[] scale,float rotation) left,
        (int set,float[] offset,float[] scale,float rotation) right)
        => left.set==right.set&&left.offset.SequenceEqual(right.offset)&&
           left.scale.SequenceEqual(right.scale)&&left.rotation==right.rotation;

    private static byte Clamp255(float v)=>(byte)Math.Clamp((int)MathF.Round(v*255),0,255);

    private static byte[] PackedTexture(RasterImage bitmap,(int wrapS,int wrapT,int linear) sampler)
    {
        using var writer=new BigWriter();
        int w=bitmap.Width,h=bitmap.Height;
        int levels=1;for(int n=Math.Min(w,h);n>4;n/=2)levels++;
        writer.I(w);writer.I(h);writer.I(levels);
        writer.I(sampler.wrapS);writer.I(sampler.wrapT);writer.I(sampler.linear);
        var pixels=new List<byte>();
        for(int mip=0;mip<levels;mip++)
        {
            using var image=bitmap.Resized(w>>mip,h>>mip);
            for(int y=0;y<image.Height;y+=4)for(int x=0;x<image.Width;x+=4)
            {
                for(int row=0;row<4;row++)for(int col=0;col<4;col++)
                {
                    var p=image[x+col,y+row];pixels.Add(p.A);pixels.Add(p.R);
                }
                for(int row=0;row<4;row++)for(int col=0;col<4;col++)
                {
                    var p=image[x+col,y+row];pixels.Add(p.G);pixels.Add(p.B);
                }
            }
        }
        writer.I(pixels.Count);writer.Bytes(pixels.ToArray());
        return writer.Data;
    }

    private static int AddUnique(List<byte[]> payloads,Dictionary<string,int> unique,byte[] data)
    {
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        if(unique.TryGetValue(hash,out int existing))return existing;
        int index=payloads.Count;
        unique.Add(hash,index);payloads.Add(data);
        return index;
    }

    private void BakeSecondaryMaps(List<byte[]> payloads,Dictionary<string,int> unique)
    {
        if(!options.ApproximateMaterials)return;
        var mats=Items("materials");
        for(int m=0;m<mats.Length;m++)
        {
            var material=mats[m];
            bool hasEmission=material.TryGetProperty("emissiveTexture",out var emissionTx);
            bool hasOcclusion=material.TryGetProperty("occlusionTexture",out var occlusionTx);
            var sourceEmission=Values(material,"emissiveFactor",[0,0,0]);
            bool hasConstantEmission=sourceEmission.Length==3&&sourceEmission.Any(x=>x>0);
            if(!hasEmission&&!hasOcclusion&&!hasConstantEmission)continue;

            bool hasPbr=material.TryGetProperty("pbrMetallicRoughness",out var pbr);
            JsonElement baseTx=default;
            bool hasBase=hasPbr&&pbr.TryGetProperty("baseColorTexture",out baseTx);
            int baseIndex=hasBase?Int(baseTx,"index",-1):-1;
            int emissionIndex=hasEmission?Int(emissionTx,"index",-1):-1;
            int occlusionIndex=hasOcclusion?Int(occlusionTx,"index",-1):-1;
            int primary=baseIndex>=0?baseIndex:hasEmission?emissionIndex:hasOcclusion?occlusionIndex:-1;
            using var primaryImage=primary>=0?DecodeSourceTexture(primary):
                new RasterImage(4,4,new Rgba32(255,255,255,255));
            int width=Dimension(primaryImage.Width,options.TextureLimit);
            int height=Dimension(primaryImage.Height,options.TextureLimit);

            var baseUv=hasBase?TextureUv(baseTx):(0,new float[]{0,0},new float[]{1,1},0f);
            var sampler=primary>=0?SamplerOf(primary):(wrapS:1,wrapT:1,linear:1);
            void ValidateLayer(JsonElement layer,int textureIndex)
            {
                if(!SameUv(baseUv,TextureUv(layer)))
                    throw new InvalidDataException("Multi-texture maps require matching UV sets and transforms for offline baking.");
                if(SamplerOf(textureIndex)!=sampler)
                    throw new InvalidDataException("Multi-texture maps require matching texture samplers for offline baking.");
            }
            if(hasEmission)ValidateLayer(emissionTx,emissionIndex);
            if(hasOcclusion)ValidateLayer(occlusionTx,occlusionIndex);

            using var baseDecoded=hasBase?DecodeSourceTexture(baseIndex):
                new RasterImage(4,4,new Rgba32(255,255,255,255));
            using var baseImage=baseDecoded.Resized(width,height);
            using var emissionDecoded=hasEmission?DecodeSourceTexture(emissionIndex):null;
            using var occlusionDecoded=hasOcclusion?DecodeSourceTexture(occlusionIndex):null;
            using var emissionImage=emissionDecoded?.Resized(width,height);
            using var occlusionImage=occlusionDecoded?.Resized(width,height);
            using var baked=new RasterImage(width,height);

            float[] factor=hasPbr?Values(pbr,"baseColorFactor",[1,1,1,1]):[1,1,1,1];
            float[] emissive=Values(material,"emissiveFactor",[0,0,0]);
            float strength=hasOcclusion?Float(occlusionTx,"strength",1):0;
            if(factor.Length!=4||emissive.Length!=3||factor.Any(x=>!float.IsFinite(x)||x<0||x>1)||
               emissive.Any(x=>!float.IsFinite(x)||x<0)||!float.IsFinite(strength)||strength<0||strength>1)
                throw new InvalidDataException("Invalid secondary material color factor.");
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                var b=baseImage[x,y];var e=emissionImage?[x,y];var o=occlusionImage?[x,y];
                float occlusion=o.HasValue?1-strength+strength*o.Value.R/255f:1;
                float R=(b.R/255f)*factor[0]*occlusion+(e?.R??255)/255f*emissive[0];
                float G=(b.G/255f)*factor[1]*occlusion+(e?.G??255)/255f*emissive[1];
                float B=(b.B/255f)*factor[2]*occlusion+(e?.B??255)/255f*emissive[2];
                baked[x,y]=new Rgba32(Clamp255(R),Clamp255(G),Clamp255(B),Clamp255((b.A/255f)*factor[3]));
            }
            bakedMaterials[m]=AddUnique(payloads,unique,PackedTexture(baked,sampler));
            Console.Error.WriteLine($"Material {m}: baked compatible base/emissive/occlusion maps into a GX texture (diffuse approximation).");
        }
    }
}
