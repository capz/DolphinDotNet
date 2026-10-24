using DolphinDotNet.Imaging;
using System.Text.Json;
namespace DolphinDotNet.Assets;
internal sealed partial class GltfImport
{
    private int[] textureRemap=[];
    private int[] materialRemap=[];
    private void WriteTextures(BigWriter output)
    {
        var textures=Items("textures");textureRemap=new int[textures.Length];var payloads=new List<byte[]>();int sourceIndex=0;var images=Items("images");var samplers=Items("samplers");var unique=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var tx in textures){using var writer=new BigWriter();int source=Int(tx,"source",-1);if((uint)source>=images.Length)throw new InvalidDataException("Missing texture image.");var image=images[source];byte[] data=image.TryGetProperty("uri",out var uri)?ReadUri(uri.GetString()!):View(Int(image,"bufferView",-1));using var decoded=RasterImage.Load(data);int w=4,h=4;while(w<decoded.Width&&w<options.TextureLimit)w*=2;while(h<decoded.Height&&h<options.TextureLimit)h*=2;using var resized=decoded.Resized(w,h);int si=Int(tx,"sampler",-1);if(si< -1||si>=samplers.Length)throw new InvalidDataException("Texture sampler.");var sampler=si<0?default:samplers[si];int wrapS=sampler.ValueKind==JsonValueKind.Undefined?10497:Int(sampler,"wrapS",10497),wrapT=sampler.ValueKind==JsonValueKind.Undefined?10497:Int(sampler,"wrapT",10497);int levels=1;for(int n=Math.Min(w,h);n>4;n/=2)levels++;writer.I(w);writer.I(h);writer.I(levels);writer.I(Wrap(wrapS));writer.I(Wrap(wrapT));writer.I(sampler.ValueKind==JsonValueKind.Undefined||Int(sampler,"magFilter",9729)!=9728?1:0);var payload=new List<byte>();for(int level=0;level<levels;level++){using var mip=resized.Resized(w>>level,h>>level);for(int y=0;y<mip.Height;y+=4)for(int x=0;x<mip.Width;x+=4){for(int row=0;row<4;row++)for(int col=0;col<4;col++){var px=mip[x+col,y+row];payload.Add(px.A);payload.Add(px.R);}for(int row=0;row<4;row++)for(int col=0;col<4;col++){var px=mip[x+col,y+row];payload.Add(px.G);payload.Add(px.B);}}}writer.I(payload.Count);writer.Bytes(payload.ToArray());byte[] record=writer.Data;string hash=System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record));if(!unique.TryGetValue(hash,out int id)){id=payloads.Count;unique.Add(hash,id);payloads.Add(record);}textureRemap[sourceIndex++]=id;}
        if(options.CompileGxMaterials)CompileGxMaps(payloads,unique);else BakeSecondaryMaps(payloads,unique);output.I(payloads.Count);foreach(var record in payloads)output.Bytes(record);
    }
    private static int Wrap(int value)=>value switch{10497=>1,33071=>0,33648=>2,_=>throw new InvalidDataException("Wrap mode.")};
    // The target stores one UV set. Resolve the source texture coordinate set
    // and bake KHR_texture_transform into that UV set per primitive.
    private static (int set,float[] offset,float[] scale,float rotation) BaseColorUv(JsonElement material)
    {
        if(material.ValueKind==JsonValueKind.Undefined)return(0,[0,0],[1,1],0);
        JsonElement texture;
        if(material.TryGetProperty("pbrMetallicRoughness",out var pbr)&&pbr.TryGetProperty("baseColorTexture",out texture)){}
        else if(material.TryGetProperty("emissiveTexture",out texture)){}
        else if(material.TryGetProperty("occlusionTexture",out texture)){}
        else return(0,[0,0],[1,1],0);
        int set=Int(texture,"texCoord");
        float[] offset=[0,0],scale=[1,1];float rotation=0;
        if(texture.TryGetProperty("extensions",out var extensions))
        {
            foreach(var extension in extensions.EnumerateObject())
                if(extension.Name!="KHR_texture_transform")throw new InvalidDataException("Unsupported base-color texture extension: "+extension.Name);
            if(extensions.TryGetProperty("KHR_texture_transform",out var transform))
            {
                offset=Values(transform,"offset",offset);
                scale=Values(transform,"scale",scale);
                rotation=Float(transform,"rotation");
                set=Int(transform,"texCoord",set);
            }
        }
        if(set is not (0 or 1)||offset.Length!=2||scale.Length!=2||
           !float.IsFinite(rotation)||offset.Any(x=>!float.IsFinite(x))||scale.Any(x=>!float.IsFinite(x)))
            throw new InvalidDataException("Unsupported base-color UV set/transform.");
        return(set,offset,scale,rotation);
    }
    private void WriteMaterial(BigWriter w,JsonElement m,int sourceMaterialIndex)
    {
        float[] color=[1,1,1,1];int texture=-1,mode=0;float cutoff=0.5f;bool two=false;
        if(m.ValueKind!=JsonValueKind.Undefined){mode=Text(m,"alphaMode","OPAQUE") switch{"OPAQUE"=>0,"MASK"=>1,"BLEND"=>4,_=>throw new InvalidDataException("Unsupported alpha mode.")} ;cutoff=Float(m,"alphaCutoff",0.5f);two=m.TryGetProperty("doubleSided",out var ds)&&ds.GetBoolean();bool unlit=m.TryGetProperty("extensions",out var ex)&&ex.TryGetProperty("KHR_materials_unlit",out _);if(unlit)mode|=2;
            foreach(string key in new[]{"normalTexture","occlusionTexture","emissiveTexture"})if(m.TryGetProperty(key,out _)&&!options.ApproximateMaterials&&(!options.CompileGxMaterials||key=="normalTexture"))throw new InvalidDataException("Unsupported material feature "+key+"; opt in to approximation or bake it.");if(m.TryGetProperty("pbrMetallicRoughness",out var p)){color=Values(p,"baseColorFactor",color);if(p.TryGetProperty("baseColorTexture",out var tx)){texture=Int(tx,"index",-1);if((uint)texture>=Items("textures").Length)throw new InvalidDataException("Invalid base-color texture reference.");_ = BaseColorUv(m);}if(!unlit&&!options.ApproximateMaterials&&(Float(p,"metallicFactor",1)>0||p.TryGetProperty("metallicRoughnessTexture",out _)))throw new InvalidDataException("PBR material requires --allow-material-approximation or baking.");}}
        if(m.ValueKind!=JsonValueKind.Undefined&&!options.ApproximateMaterials&&!options.CompileGxMaterials&&
           m.TryGetProperty("emissiveFactor",out var sourceEmission))
        {
            var values=sourceEmission.EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            if(values.Length!=3||values.Any(x=>x>0))
                throw new InvalidDataException("Emissive color requires explicit material approximation or baking.");
        }
        if(m.ValueKind!=JsonValueKind.Undefined&&options.ApproximateMaterials)
        {
            if(m.TryGetProperty("normalTexture",out _))
                Console.Error.WriteLine($"Material {sourceMaterialIndex}: normal map omitted; GX diffuse shading cannot reproduce tangent-space normals.");
            if(m.TryGetProperty("pbrMetallicRoughness",out var approxPbr)&&
               (approxPbr.TryGetProperty("metallicRoughnessTexture",out _)||Float(approxPbr,"metallicFactor",1)>0))
                Console.Error.WriteLine($"Material {sourceMaterialIndex}: metallic/roughness PBR contributions approximated by simple diffuse shading.");
        }
        if(color.Length!=4||color.Any(x=>x<0||x>1)||cutoff<0||cutoff>1)throw new InvalidDataException("Material range.");int packed=texture<0?-1:textureRemap[texture];if(bakedMaterials.TryGetValue(sourceMaterialIndex,out int baked)){packed=baked;color=[1,1,1,1];}if(gxMaterials.TryGetValue(sourceMaterialIndex,out var gx))packed=gx.primary;
        w.Floats(color);w.I(packed);w.I(mode);w.F(cutoff);w.I(two?1:0);
        if(options.CompileGxMaterials){w.I(gxMaterials.TryGetValue(sourceMaterialIndex,out gx)?gx.secondary:-1);w.I(gxMaterials.TryGetValue(sourceMaterialIndex,out gx)?gx.combiner:0);}
    }
}
