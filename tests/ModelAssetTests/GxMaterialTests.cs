using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DolphinDotNet.Assets;
static class GxMaterialTests
{
    static int I(byte[] data,int at)=>BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at,4));
    static int Pixels(byte[] data,int index)
    {
        int at=I(data,68)+4;
        while(index-->0)at+=28+I(data,at+24);
        return at+28;
    }
    internal static void Run(JsonObject source,string output)
    {
        string input=Path.Combine(output,"gx-material.gltf"),file=Path.Combine(output,"sd","gx-material.dndmodel");
        var root=source.DeepClone().AsObject();var mat=root["materials"]![0]!.AsObject();
        mat.Remove("extensions");mat["alphaMode"]="BLEND";
        mat["emissiveFactor"]=new JsonArray(0.25,0.5,0.75);
        mat["emissiveTexture"]=new JsonObject{["index"]=0};
        File.WriteAllText(input,root.ToJsonString());
        ModelConverter.Convert(input,file,new(){CompileGxMaterials=true});
        byte[] data=File.ReadAllBytes(file);int offset=I(data,52),record=offset+4+40;
        if(I(data,4)!=2||I(data,offset)!=2||I(data,record+32)<0||I(data,record+36)!=2||(I(data,record+20)&4)==0)
            throw new Exception("Imported emission TEV record missing.");
        int pixels=Pixels(data,I(data,record+32));
        if(data[pixels]!=255||data[pixels+3]!=8||data[pixels+33]!=38)
            throw new Exception("Emission factor pixels or secondary alpha.");
        // AO remains a separate runtime map and must not alter authored alpha.
        mat.Remove("emissiveFactor");mat.Remove("emissiveTexture");
        mat["occlusionTexture"]=new JsonObject{["index"]=0,["strength"]=0.5};
        File.WriteAllText(input,root.ToJsonString());
        string ao=Path.Combine(output,"sd","gx-occlusion.dndmodel");
        ModelConverter.Convert(input,ao,new(){CompileGxMaterials=true});data=File.ReadAllBytes(ao);offset=I(data,52);record=offset+4+40;
        if(I(data,record+36)!=1||I(data,record+32)<0||BitConverter.Int32BitsToSingle(I(data,record+12))!=1)
            throw new Exception("Imported occlusion record or alpha.");
        pixels=Pixels(data,I(data,record+32));
        if(data[pixels]!=255||data[pixels+1]!=128||Math.Abs(data[pixels+3]-142)>1)
            throw new Exception("Occlusion red-channel/strength mapping.");
        void Reject(Action change)
        {var saved=root.DeepClone().AsObject();change();File.WriteAllText(input,root.ToJsonString());
         try{ModelConverter.Convert(input,file+".invalid",new(){CompileGxMaterials=true});throw new Exception("Invalid GX material accepted.");}
         catch(InvalidDataException){}root=saved;mat=root["materials"]![0]!.AsObject();}
        Reject(()=>mat["occlusionTexture"]!["texCoord"]=1);
        Reject(()=>mat["occlusionTexture"]!["index"]=99);
        Reject(()=>mat["occlusionTexture"]!["strength"]=1.1);
        Reject(()=>mat["emissiveFactor"]=new JsonArray(2,0,0));
        // Both contributions are retained: AO in base, emission in TEX1.
        mat["emissiveFactor"]=new JsonArray(0.25,0.5,0.75);mat["emissiveTexture"]=new JsonObject{["index"]=0};
        File.WriteAllText(input,root.ToJsonString());ModelConverter.Convert(input,file+".combined",new(){CompileGxMaterials=true});
        data=File.ReadAllBytes(file+".combined");offset=I(data,52);record=offset+4+40;
        if(I(data,record+36)!=2||I(data,record+16)==0)throw new Exception("Base/AO contribution was discarded.");
        mat["extensions"]=new JsonObject{["KHR_materials_unlit"]=new JsonObject()};
        File.WriteAllText(input,root.ToJsonString());ModelConverter.Convert(input,file+".unlit",new(){CompileGxMaterials=true});
        data=File.ReadAllBytes(file+".unlit");record=I(data,52)+4+40;
        if(I(data,record+32)!=-1||I(data,record+36)!=0)throw new Exception("Unlit source acquired lit surface contributions.");
        // A constant emission map needs no authored texture coordinate stream.
        root=source.DeepClone().AsObject();mat=root["materials"]![0]!.AsObject();mat.Remove("extensions");
        mat["pbrMetallicRoughness"]!.AsObject().Remove("baseColorTexture");
        mat["emissiveFactor"]=new JsonArray(0.2,0.1,0);
        root["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject().Remove("TEXCOORD_0");
        File.WriteAllText(input,root.ToJsonString());ModelConverter.Convert(input,file+".constant",new(){CompileGxMaterials=true});
        data=File.ReadAllBytes(file+".constant");record=I(data,52)+4+40;
        if(I(data,record+16)!=-1||I(data,record+32)<0||I(data,record+36)!=2)
            throw new Exception("Constant emission without UVs was lost.");
        Console.WriteLine("Versioned GX material records, source mapping and invalid input checks passed.");
    }
}
