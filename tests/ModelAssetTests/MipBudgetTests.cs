using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DolphinDotNet.Assets;
using DolphinDotNet.Imaging;

internal static class MipBudgetTests
{
    internal static void Run(JsonObject template,string output)
    {
        var scene=template.DeepClone().AsObject();
        string input=Path.Combine(output,"mip-budget.gltf");
        void Image(int w,int h)
        {
            using var image=new RasterImage(w,h,new Rgba32(80,40,20,255));
            using var png=new MemoryStream();image.SaveAsPng(png);
            scene["images"]![0]!["uri"]="data:image/png;base64,"+Convert.ToBase64String(png.ToArray());
            File.WriteAllText(input,scene.ToJsonString());
        }
        int Read(byte[] b,int at)=>checked((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at,4)));
        byte[] Build(string name,int maximum,int expected,bool gx=false,bool approximate=false)
        {
            string file=Path.Combine(output,"mip-budget-"+name+".dndmodel");
            ModelConverter.Convert(input,file,new ImportOptions{MaximumMipLevels=maximum,TextureLimit=128,CompileGxMaterials=gx,ApproximateMaterials=approximate});
            var b=File.ReadAllBytes(file);int start=Read(b,68),end=start+Read(b,72),count=Read(b,start),at=start+4;
            if(count<1)throw new Exception("Mip fixture has no textures.");
            for(int i=0;i<count;i++){
                int w=Read(b,at),h=Read(b,at+4),levels=Read(b,at+8),size=Read(b,at+24),expectedSize=0;
                for(int l=0;l<expected;l++)expectedSize+=(w>>l)*(h>>l)*4;
                if(levels!=expected||size!=expectedSize)throw new Exception("Model mip count/payload budget was not applied to every generated texture.");
                at+=28+size;
            }
            if(at!=end)throw new Exception("Mipmap records overrun their section.");
            if((gx||approximate)&&count<2)throw new Exception("Secondary-map fixture did not exercise generated maps.");
            return b;
        }
        Image(16,16);var full=Build("full",0,3);var two=Build("two",2,2);Build("base",1,1);
        int fullPixels=Read(full,68)+4+28,twoPixels=Read(two,68)+4+28;
        if(!full.AsSpan(fullPixels,1280).SequenceEqual(two.AsSpan(twoPixels,1280)))throw new Exception("Mip limiting changed retained source-derived levels.");
        int fullMesh=Read(full,36),twoMesh=Read(two,36);
        if(!full.AsSpan(fullMesh,Read(full,40)).SequenceEqual(two.AsSpan(twoMesh,Read(two,40))))throw new Exception("Mip limiting changed model geometry.");
        var material=scene["materials"]![0]!.AsObject();material.Remove("extensions");
        material["emissiveFactor"]=new JsonArray(.5,.5,.5);material["emissiveTexture"]=new JsonObject{["index"]=0};
        material["occlusionTexture"]=new JsonObject{["index"]=0};Image(16,16);
        Build("gx",2,2,gx:true);Build("baked",2,2,approximate:true);
        material.Remove("emissiveFactor");material.Remove("emissiveTexture");material.Remove("occlusionTexture");
        Image(16,4);Build("short",2,1);
        foreach(int invalid in new[]{-1,10}){
            try{ModelConverter.Convert(input,Path.Combine(output,"bad-mips.dndmodel"),new ImportOptions{MaximumMipLevels=invalid});}
            catch(ArgumentException){continue;}
            throw new Exception("Invalid mip-level limit accepted.");
        }
        Console.WriteLine("Bounded source/generated model mip chains, geometry preservation and minimum dimensions passed.");
    }
}
