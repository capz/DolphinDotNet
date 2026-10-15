using System.Text;
using System.Text.Json.Nodes;

internal static class GlbImportTests
{
    internal static void Run(string output)
    {
        var root=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"animated.gltf")))!.AsObject();
        byte[] geometry=File.ReadAllBytes(Path.Combine(output,"geometry.bin")),image=File.ReadAllBytes(Path.Combine(output,"texture.png"));
        int imageOffset=(geometry.Length+3)&~3;byte[] binary=new byte[(imageOffset+image.Length+3)&~3];geometry.CopyTo(binary,0);image.CopyTo(binary,imageOffset);
        root["buffers"]![0]!.AsObject().Remove("uri");root["buffers"]![0]!["byteLength"]=binary.Length;
        var views=root["bufferViews"]!.AsArray();int view=views.Count;
        views.Add(new JsonObject{["buffer"]=0,["byteOffset"]=imageOffset,["byteLength"]=image.Length});
        root["images"]=new JsonArray(new JsonObject{["bufferView"]=view,["mimeType"]="image/png"});
        string path=Path.Combine(output,"sd","embedded.glb");Write(path,root,binary);
        // JSON-first is required by GLB, not merely a convention.
        Write(Path.Combine(output,"sd","invalid-order.glb"),root,binary,true);
        File.Copy(Path.Combine(output,"animated.glb"),Path.Combine(output,"sd","external.glb"),true);
        var sparse=root.DeepClone().AsObject();sparse["accessors"]![0]!["sparse"]=new JsonObject{["count"]=1};
        Write(Path.Combine(output,"sd","sparse.glb"),sparse,binary);
        var escape=root.DeepClone().AsObject();escape["images"]=new JsonArray(new JsonObject{["uri"]="../texture.png"});
        Write(Path.Combine(output,"sd","escape.glb"),escape,binary);
        Console.WriteLine("Generated embedded and malformed GLB fixtures for native loading tests.");
    }
    private static void Write(string path,JsonObject root,byte[] binary,bool reversed=false)
    {
        byte[] json=Encoding.UTF8.GetBytes(root.ToJsonString());int jsonSize=(json.Length+3)&~3;
        using var file=File.Create(path);using var writer=new BinaryWriter(file);
        writer.Write(0x46546c67);writer.Write(2);writer.Write(28+jsonSize+binary.Length);
        void Json(){writer.Write(jsonSize);writer.Write(0x4e4f534a);writer.Write(json);writer.Write(Enumerable.Repeat((byte)32,jsonSize-json.Length).ToArray());}
        void Bin(){writer.Write(binary.Length);writer.Write(0x004e4942);writer.Write(binary);}
        if(reversed){Bin();Json();}else{Json();Bin();}
    }
}
