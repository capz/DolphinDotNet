using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DolphinDotNet.Assets;
using DolphinDotNet.Imaging;

static class StudioBakeTests
{
    static int I(byte[] b,int at)=>BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at,4));
    internal static void Run(JsonObject source,string output)
    {
        var root=source.DeepClone().AsObject();root.Remove("skins");root.Remove("animations");
        root["nodes"]![2]!.AsObject().Remove("skin");
        var attrs=root["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject();attrs.Remove("JOINTS_0");attrs.Remove("WEIGHTS_0");
        var mat=root["materials"]![0]!.AsObject();mat.Remove("extensions");mat["alphaMode"]="BLEND";
        mat["pbrMetallicRoughness"]=new JsonObject{["baseColorFactor"]=new JsonArray(.5,.25,.125,.35),["metallicFactor"]=0,["roughnessFactor"]=1};
        string input=Path.Combine(output,"studio.gltf"),file=Path.Combine(output,"sd","studio.dndmodel");
        byte[] Bake(string suffix="")
        {File.WriteAllText(input,root.ToJsonString());ModelConverter.Convert(input,file+suffix,new(){BakeNeutralStudio=true,TextureLimit=4});return File.ReadAllBytes(file+suffix);}
        void Pixel(byte[] b,int r,int g,int blue,int alpha=89)
        {
            int material=I(b,52)+4+32,at=I(b,68)+4+28;
            if(I(b,4)!=1||I(b,material+20)!=6||I(b,material+16)!=0)throw new Exception("Studio output must be unlit BLEND v1.");
            if(Math.Abs(b[at+1]-r)>1||Math.Abs(b[at+32]-g)>1||Math.Abs(b[at+33]-blue)>1||Math.Abs(b[at]-alpha)>1)
                throw new Exception($"Studio numeric pixel: got {b[at+1]},{b[at+32]},{b[at+33]},{b[at]}; expected {r},{g},{blue},{alpha}.");
        }
        // Double-precision reference values calculated independently from the
        // documented light directions, BRDF equations and display curve.
        byte[] first=Bake();Pixel(first,189,144,98);
        if(!first.SequenceEqual(Bake(".repeat")))throw new Exception("Nondeterministic studio package.");
        if(JsonNode.Parse(File.ReadAllText(file+".studio.json"))!["preset"]!.GetValue<string>()!="neutral-studio-v1")throw new Exception("Missing studio reproducibility report.");
        var pbr=mat["pbrMetallicRoughness"]!.AsObject();pbr["metallicFactor"]=1;pbr["roughnessFactor"]=.2;
        Pixel(Bake(".metal"),10,4,2);pbr["metallicFactor"]=0;pbr["roughnessFactor"]=1;
        using var image=new RasterImage(4,4);
        void Map(byte r,byte g,byte b)
        {
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)image[x,y]=new Rgba32(r,g,b,255);
            using var png=new MemoryStream();image.SaveAsPng(png);
            root["images"]![0]!["uri"]="data:image/png;base64,"+Convert.ToBase64String(png.ToArray());
        }
        Map(0,51,255);pbr["metallicFactor"]=1;pbr["roughnessFactor"]=1;pbr["metallicRoughnessTexture"]=new JsonObject{["index"]=0};
        Pixel(Bake(".metal-map"),10,4,2);pbr.Remove("metallicRoughnessTexture");pbr["metallicFactor"]=0;
        Map(128,255,128);mat["normalTexture"]=new JsonObject{["index"]=0};
        Pixel(Bake(".normal-map"),207,172,134);mat.Remove("normalTexture");
        Map(0,255,0);mat["occlusionTexture"]=new JsonObject{["index"]=0};mat["emissiveFactor"]=new JsonArray(.1,.2,.3);
        Pixel(Bake(".emission"),198,189,192);mat.Remove("occlusionTexture");mat.Remove("emissiveFactor");
        // A rotation of the instance changes its world normal and lighting.
        root["nodes"]![2]!["rotation"]=new JsonArray(-Math.Sqrt(.5),0,0,Math.Sqrt(.5));
        Pixel(Bake(".rotated"),207,172,134);root["nodes"]![2]!.AsObject().Remove("rotation");
        // Unlit colors bypass the lighting and tone mapper; sRGB texture colors
        // are decoded before applying linear factors, alpha remains linear.
        Map(128,64,32);mat["extensions"]=new JsonObject{["KHR_materials_unlit"]=new JsonObject()};
        pbr["baseColorFactor"]=new JsonArray(1,1,1,.35);pbr["baseColorTexture"]=new JsonObject{["index"]=0};
        Pixel(Bake(".unlit"),128,64,32);
        root["images"]![0]!["uri"]=source["images"]![0]!["uri"]!.DeepClone();
        attrs["TEXCOORD_1"]=attrs["TEXCOORD_0"]!.DeepClone();
        root["samplers"]=new JsonArray(new JsonObject{["magFilter"]=9728,["wrapS"]=33071,["wrapT"]=33071});root["textures"]![0]!["sampler"]=0;
        pbr["baseColorTexture"]!["texCoord"]=1;
        pbr["baseColorTexture"]!["extensions"]=new JsonObject{["KHR_texture_transform"]=new JsonObject{["offset"]=new JsonArray(.25,0)}};
        Pixel(Bake(".uv1-transform"),30,0,50);
        attrs.Remove("TEXCOORD_1");pbr["baseColorTexture"]!.AsObject().Remove("texCoord");pbr["baseColorTexture"]!.AsObject().Remove("extensions");
        void Reject(Action change)
        {
            var saved=root.DeepClone().AsObject();change();
            try{Bake(".invalid");throw new Exception("Invalid studio scene accepted.");}catch(InvalidDataException){}
            root=saved;mat=root["materials"]![0]!.AsObject();pbr=mat["pbrMetallicRoughness"]!.AsObject();attrs=root["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject();
        }
        Reject(()=>root["animations"]=source["animations"]!.DeepClone());
        Reject(()=>root["skins"]=source["skins"]!.DeepClone());
        Reject(()=>attrs.Remove("TEXCOORD_0"));
        Reject(()=>root["meshes"]![0]!["primitives"]![0]!["targets"]=new JsonArray());
        Reject(()=>mat["extensions"]!["KHR_materials_clearcoat"]=new JsonObject());
        Reject(()=>pbr["baseColorTexture"]!["texCoord"]=1);
        Reject(()=>root["nodes"]![2]!["scale"]=new JsonArray(0,1,1));
        // The same chart cannot encode two differently lit sides of a face.
        Reject(()=>{
            attrs.Remove("NORMAL");mat.Remove("extensions");pbr.Remove("baseColorTexture");
            var buffers=root["buffers"]!.AsArray();int buffer=buffers.Count;
            buffers.Add(new JsonObject{["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(new byte[]{0,0,1,0,2,0,2,0,1,0,0,0}),["byteLength"]=12});
            var views=root["bufferViews"]!.AsArray();int view=views.Count;views.Add(new JsonObject{["buffer"]=buffer,["byteLength"]=12});
            var accessors=root["accessors"]!.AsArray();int accessor=accessors.Count;accessors.Add(new JsonObject{["bufferView"]=view,["componentType"]=5123,["count"]=6,["type"]="SCALAR"});
            root["meshes"]![0]!["primitives"]![0]!["indices"]=accessor;
        });
        try{ModelConverter.Convert(input,file+".invalid",new(){BakeNeutralStudio=true,CompileGxMaterials=true});throw new Exception("Conflicting material options accepted.");}catch(ArgumentException){}
        Console.WriteLine("Neutral studio BRDF pixels, alpha, world transforms, unlit transfer, determinism and strict input checks passed.");
    }
}
