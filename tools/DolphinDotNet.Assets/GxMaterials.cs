using DolphinDotNet.Imaging;
using System.Text.Json;
namespace DolphinDotNet.Assets;
internal sealed partial class GltfImport
{
    private readonly Dictionary<int,(int primary,int secondary,int combiner)> gxMaterials=new();
    private readonly HashSet<int> gxUvMaterials=new();
    // Explicit fixed-function mapping: base*raster*occlusion + emission.
    // Two runtime maps share authored UVs. If all three maps are present, AO
    // is resolved into base offline while emission stays independent of light.
    private void CompileGxMaps(List<byte[]> payloads,Dictionary<string,int> unique)
    {
        var materials=Items("materials");
        for(int index=0;index<materials.Length;index++)
        {
            var m=materials[index];
            if(m.TryGetProperty("extensions",out var extensions)&&extensions.TryGetProperty("KHR_materials_unlit",out _))continue;
            var factor=Values(m,"emissiveFactor",[0,0,0]);
            if(factor.Length!=3||factor.Any(v=>!float.IsFinite(v)||v<0||v>1))
                throw new InvalidDataException("GX emissive factors must be finite and within 0..1.");
            bool emission=factor.Any(v=>v>0);
            bool hasEm=m.TryGetProperty("emissiveTexture",out var em);
            bool ao=m.TryGetProperty("occlusionTexture",out var occ);
            if(!emission&&!ao)continue;
            bool hasBase=m.TryGetProperty("pbrMetallicRoughness",out var pbr)&&pbr.TryGetProperty("baseColorTexture",out _);
            JsonElement basis=hasBase?pbr.GetProperty("baseColorTexture"):hasEm?em:ao?occ:default;
            var uv=basis.ValueKind==JsonValueKind.Undefined?(0,new float[]{0,0},new float[]{1,1},0f):TextureUv(basis);
            int primary=hasBase?Int(pbr.GetProperty("baseColorTexture"),"index",-1):-1;
            if(hasBase||hasEm||ao)gxUvMaterials.Add(index);
            int basisIndex=basis.ValueKind==JsonValueKind.Undefined?-1:Int(basis,"index",-1);
            if(basisIndex < -1)throw new InvalidDataException("Invalid GX material texture.");
            var sampler=basisIndex>=0?SamplerOf(basisIndex):(wrapS:1,wrapT:1,linear:1);
            void Validate(JsonElement texture)
            {
                int id=Int(texture,"index",-1);
                if(id<0||id>=Items("textures").Length||!SameUv(uv,TextureUv(texture)))
                    throw new InvalidDataException("GX material maps require the same valid UV set and transform.");
                // The mesh stores only one UV stream; independent sampler modes
                // are nevertheless retained by each texture record.
            }
            if(hasEm)Validate(em);if(ao)Validate(occ);
            using var original=primary>=0?DecodeSourceTexture(primary):new RasterImage(4,4,new Rgba32(255,255,255,255));
            using var baseImage=original.Resized(Dimension(original.Width,options.TextureLimit),Dimension(original.Height,options.TextureLimit));
            int secondary=-1,combiner=0;
            if(ao)
            {
                float strength=Float(occ,"strength",1);
                if(!float.IsFinite(strength)||strength<0||strength>1)throw new InvalidDataException("GX occlusion strength.");
                int aoIndex=Int(occ,"index",-1);
                using var decoded=DecodeSourceTexture(aoIndex);
                // With no emission, retain AO as the second runtime texture.
                if(!emission)
                {
                    using var image=decoded.Resized(Dimension(decoded.Width,options.TextureLimit),Dimension(decoded.Height,options.TextureLimit));
                    for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
                    {byte value=Clamp255(1-strength+strength*image[x,y].R/255f);image[x,y]=new Rgba32(value,value,value,255);}
                    secondary=AddUnique(payloads,unique,PackedTexture(image,SamplerOf(aoIndex)));combiner=1;
                }
                else
                {
                    if(SamplerOf(aoIndex)!=sampler)throw new InvalidDataException("Offline base/AO combination requires matching samplers.");
                    using var image=decoded.Resized(baseImage.Width,baseImage.Height);
                    for(int y=0;y<baseImage.Height;y++)for(int x=0;x<baseImage.Width;x++)
                    {var b=baseImage[x,y];float amount=1-strength+strength*image[x,y].R/255f;
                     baseImage[x,y]=new Rgba32(Clamp255(b.R/255f*amount),Clamp255(b.G/255f*amount),Clamp255(b.B/255f*amount),b.A);}
                    primary=-2; // material-specific resolved base map below
                }
            }
            if(emission)
            {
                int emIndex=hasEm?Int(em,"index",-1):-1;
                using var decoded=hasEm?DecodeSourceTexture(emIndex):new RasterImage(4,4,new Rgba32(255,255,255,255));
                using var image=decoded.Resized(Dimension(decoded.Width,options.TextureLimit),Dimension(decoded.Height,options.TextureLimit));
                for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
                {var p=image[x,y];image[x,y]=new Rgba32(Clamp255(p.R/255f*factor[0]),Clamp255(p.G/255f*factor[1]),Clamp255(p.B/255f*factor[2]),255);}
                secondary=AddUnique(payloads,unique,PackedTexture(image,hasEm?SamplerOf(emIndex):sampler));combiner=2;
            }
            int packed=primary>=0?textureRemap[primary]:primary==-2?AddUnique(payloads,unique,PackedTexture(baseImage,sampler)):-1;
            gxMaterials.Add(index,(packed,secondary,combiner));
        }
    }
}
