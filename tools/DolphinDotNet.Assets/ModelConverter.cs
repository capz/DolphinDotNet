using DolphinDotNet.Imaging;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
namespace DolphinDotNet.Assets;

public sealed class ImportOptions
{
    public int TextureLimit { get; set; } = 256;
    public string Assimp { get; set; } = "assimp";
    public bool ApproximateMaterials { get; set; }
}
public static class ModelConverter
{
    public static void Convert(string input,string output,ImportOptions? options=null)
    {
        options??=new();
        if(options.TextureLimit<4||options.TextureLimit>1024||(options.TextureLimit&(options.TextureLimit-1))!=0)throw new ArgumentException("Texture limit must be a power of two from 4 to 1024.");
        input=Path.GetFullPath(input);output=Path.GetFullPath(output);string ext=Path.GetExtension(input).ToLowerInvariant();string? temp=null;
        try
        {
            GltfImport importer;
            if(ext is ".fbx" or ".obj")
            {
                temp=Path.Combine(Path.GetDirectoryName(input)!,".dnd-"+Guid.NewGuid().ToString("N")+".glb");
                string version=Run(options.Assimp,"version");if(!version.Contains("Version 5.3 ")&&!version.Contains("Version 5.3."))throw new InvalidDataException("FBX/OBJ require Assimp CLI 5.3.x; found: "+version.Trim());
                Console.Error.WriteLine("Import adapter: "+version.Trim()+". Verify FBX sampled curves/pivots against the authoring application.");
                Run(options.Assimp,"dump",input,temp+".assxml","-tri");
                var durations=AssimpAudit.Check(temp+".assxml");
                Run(options.Assimp,"export",input,temp,"-fglb2","-tri");importer=new(temp,options);importer.SourceDurations=durations;
                if(durations.Count>importer.AnimationCount)throw new InvalidDataException("Assimp export dropped animation clips.");importer.Dependencies.Remove(temp);importer.Dependencies.Add(input);
                if(ext==".obj")foreach(string line in File.ReadLines(input))if(line.StartsWith("mtllib ",StringComparison.Ordinal))importer.Dependencies.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(input)!,line[7..].Trim())));
            }
            else if(ext is ".gj" or ".xj")
            {
                var native=PsoGjImport.Convert(input,options.ApproximateMaterials);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                string tmp=output+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllBytes(tmp,native);File.Move(tmp,output,true);}finally{File.Delete(tmp);}
                File.WriteAllText(output+".deps.json",JsonSerializer.Serialize(
                    new[]{new {path=input,sha256=System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input)))}}));
                return;
            }
            else if(ext is ".gltf" or ".glb")importer=new(input,options);
            else throw new ArgumentException("Supported inputs: FBX, OBJ, glTF, GLB, PSO GameCube GJ and Blue Burst XJ geometry.");
            var package=importer.Build();Directory.CreateDirectory(Path.GetDirectoryName(output)!);string staged=output+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(staged,package);File.Move(staged,output,true);}finally{File.Delete(staged);}
            File.WriteAllText(output+".deps.json",JsonSerializer.Serialize(importer.Dependencies.OrderBy(x=>x,StringComparer.Ordinal).Select(x=>new{path=x,sha256=System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(x)))})));
        }
        finally{if(temp!=null){File.Delete(temp);File.Delete(temp+".assxml");}}
    }
    private static string Run(string exe,params string[] args)
    {
        var info=new ProcessStartInfo(exe){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};foreach(var a in args)info.ArgumentList.Add(a);
        using var p=Process.Start(info)??throw new IOException("Cannot start Assimp.");var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        if(!p.WaitForExit(120000)){p.Kill(true);throw new IOException("Assimp timed out.");}string text=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();if(p.ExitCode!=0)throw new InvalidDataException("Assimp failed: "+text);return text;
    }
}
internal sealed partial class GltfImport
{
    private readonly JsonElement root;
    private readonly string directory;
    private readonly ImportOptions options;
    private readonly List<byte[]> buffers=new();
    public Dictionary<string,float>? SourceDurations;
    public int AnimationCount=>Items("animations").Length;
    public readonly HashSet<string> Dependencies=new(StringComparer.Ordinal);
    private static JsonElement[] Array(JsonElement e,string k)=>e.TryGetProperty(k,out var a)?a.EnumerateArray().ToArray():[];
    private JsonElement[] Items(string k)=>Array(root,k);
    private static int Int(JsonElement e,string k,int fallback=0)=>e.TryGetProperty(k,out var v)?v.GetInt32():fallback;
    private static float Float(JsonElement e,string k,float fallback=0)=>e.TryGetProperty(k,out var v)?v.GetSingle():fallback;
    private static string Text(JsonElement e,string k,string fallback="")=>e.TryGetProperty(k,out var v)?v.GetString()!:fallback;
    private static float[] Values(JsonElement e,string k,float[] fallback)=>e.TryGetProperty(k,out var v)?v.EnumerateArray().Select(x=>x.GetSingle()).ToArray():fallback;
    public GltfImport(string input,ImportOptions options)
    {
        this.options=options;directory=Path.GetDirectoryName(input)!;Dependencies.Add(input);byte[] bytes=File.ReadAllBytes(input);byte[]? bin=null;
        if(Path.GetExtension(input).Equals(".glb",StringComparison.OrdinalIgnoreCase))
        {
            if(bytes.Length<20||BinaryPrimitives.ReadUInt32LittleEndian(bytes)!=0x46546c67||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))!=2||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))!=bytes.Length)throw new InvalidDataException("Invalid GLB header.");
            int p=12;byte[]? json=null;while(p<bytes.Length){if(bytes.Length-p<8)throw new InvalidDataException("Truncated GLB chunk.");int size=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p)));uint kind=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+4));p+=8;if(size<0||size>bytes.Length-p||size%4!=0)throw new InvalidDataException("Invalid GLB chunk.");if(kind==0x4e4f534a){if(json!=null)throw new InvalidDataException("Duplicate JSON.");json=bytes[p..(p+size)];}else if(kind==0x004e4942){if(bin!=null)throw new InvalidDataException("Duplicate BIN.");bin=bytes[p..(p+size)];}p+=size;}root=JsonDocument.Parse(json??throw new InvalidDataException("Missing GLB JSON.")).RootElement.Clone();
        }
        else root=JsonDocument.Parse(bytes).RootElement.Clone();
        if(!Text(root.GetProperty("asset"),"version").StartsWith("2."))throw new InvalidDataException("Expected glTF 2.x.");
        foreach(var ex in Array(root,"extensionsRequired"))if(ex.GetString()!="KHR_materials_unlit")throw new InvalidDataException("Unsupported required extension: "+ex.GetString());
        foreach(var b in Items("buffers")){byte[] data=b.TryGetProperty("uri",out var uri)?ReadUri(uri.GetString()!):bin??throw new InvalidDataException("Missing buffer.");if(data.Length<Int(b,"byteLength"))throw new InvalidDataException("Truncated buffer.");buffers.Add(data);}
    }
    private byte[] ReadUri(string uri)
    {
        if(uri.StartsWith("data:",StringComparison.Ordinal)){int comma=uri.IndexOf(',');if(comma<0||!uri[..comma].EndsWith(";base64",StringComparison.Ordinal))throw new InvalidDataException("Only base64 data URIs supported.");return System.Convert.FromBase64String(uri[(comma+1)..]);}
        if(Uri.TryCreate(uri,UriKind.Absolute,out _))throw new InvalidDataException("Remote URI unsupported.");string path=Path.GetFullPath(Path.Combine(directory,Uri.UnescapeDataString(uri)));Dependencies.Add(path);return File.ReadAllBytes(path);
    }
    private byte[] View(int index)
    {
        var views=Items("bufferViews");if((uint)index>=views.Length)throw new InvalidDataException("Invalid buffer view.");var v=views[index];int b=Int(v,"buffer"),o=Int(v,"byteOffset"),n=Int(v,"byteLength");if((uint)b>=buffers.Count||o<0||n<0||o>buffers[b].Length-n)throw new InvalidDataException("Buffer view range.");return buffers[b][o..(o+n)];
    }
    private float[][] Access(int index,string type)
    {
        var list=Items("accessors");if((uint)index>=list.Length)throw new InvalidDataException("Invalid accessor.");var a=list[index];if(Text(a,"type")!=type)throw new InvalidDataException("Accessor shape.");if(a.TryGetProperty("sparse",out _))throw new InvalidDataException("Sparse accessors require expansion.");
        int width=type switch{"SCALAR"=>1,"VEC2"=>2,"VEC3"=>3,"VEC4"=>4,"MAT4"=>16,_=>throw new InvalidDataException("Accessor type.")},component=Int(a,"componentType"),size=component switch{5120 or 5121=>1,5122 or 5123=>2,5125 or 5126=>4,_=>throw new InvalidDataException("Component type.")},count=Int(a,"count"),v=Int(a,"bufferView",-1);if(count<1||count>1000000||v<0)throw new InvalidDataException("Accessor count/view.");byte[] data=View(v);int stride=Int(Items("bufferViews")[v],"byteStride",size*width),offset=Int(a,"byteOffset");if(offset<0||stride<size*width||(long)offset+(long)(count-1)*stride+size*width>data.Length)throw new InvalidDataException("Accessor range/stride.");bool normalized=a.TryGetProperty("normalized",out var norm)&&norm.GetBoolean();
        var result=new float[count][];for(int i=0;i<count;i++){result[i]=new float[width];for(int j=0;j<width;j++){var b=data.AsSpan(offset+i*stride+j*size,size);float x=component switch{5120=>(sbyte)b[0],5121=>b[0],5122=>BinaryPrimitives.ReadInt16LittleEndian(b),5123=>BinaryPrimitives.ReadUInt16LittleEndian(b),5125=>BinaryPrimitives.ReadUInt32LittleEndian(b),_=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b))};if(normalized)x=component switch{5120=>Math.Max(-1,x/127),5121=>x/255,5122=>Math.Max(-1,x/32767),5123=>x/65535,_=>throw new InvalidDataException("Invalid normalization.")};if(!float.IsFinite(x))throw new InvalidDataException("Non-finite accessor.");result[i][j]=x;}}return result;
    }
}
