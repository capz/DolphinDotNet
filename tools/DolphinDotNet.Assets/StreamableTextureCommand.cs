using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DolphinDotNet.Imaging;
namespace DolphinDotNet.Assets;

internal static class StreamableTextureCommand
{
    internal static void Run(string[] args)
    {
        var options=new StreamableTextureOptions();var positional=new List<string>();string? low=null,high=null;bool pair=false;
        for(int i=1;i<args.Length;i++){
            string Value(){if(++i>=args.Length)throw new ArgumentException("Missing value for "+args[i-1]);return args[i];}
            switch(args[i]){
                case "--streamable":break;
                case "--mipmaps":options.Mipmaps=true;break;
                case "--quality-pair":pair=true;break;
                case "--low":low=Value();break;case "--high":high=Value();break;
                case "--min-size":options.MinimumSize=int.Parse(Value());break;
                case "--gutter":options.Gutter=int.Parse(Value());break;
                case "--levels":options.LogicalLevels=Value().Split(',').Select(int.Parse).ToArray();break;
                case "--format":options.Format=Value() switch {"auto"=>StreamableTextureFormat.Auto,"rgba8"=>StreamableTextureFormat.Rgba8,"cmpr"=>StreamableTextureFormat.Cmpr,_=>throw new ArgumentException("Format must be auto, rgba8 or cmpr.")};break;
                default:if(args[i].StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("Unknown texture option "+args[i]);positional.Add(args[i]);break;
            }
        }
        if(pair?(low==null||high==null||positional.Count!=1):(low!=null||high!=null||positional.Count!=2))throw new ArgumentException("Use texture INPUT OUTPUT --streamable [--mipmaps], or texture --low LOW --high HIGH OUTPUT --streamable --quality-pair.");
        string input=pair?high!:positional[0],output=positional[pair?0:1];byte[] source=File.ReadAllBytes(input);using var image=RasterImage.Load(source,true);
        byte[]? lowSource=low==null?null:File.ReadAllBytes(low);using var lowImage=lowSource==null?null:RasterImage.Load(lowSource,true);
        var asset=StreamableTextures.Encode(image,options,lowImage);var manifest=JsonNode.Parse(asset.Manifest)!.AsObject();
        manifest["sourceFileSha256"]=Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        if(lowSource!=null)manifest["lowFileSha256"]=Convert.ToHexString(SHA256.HashData(lowSource)).ToLowerInvariant();
        File.WriteAllBytes(output,asset.Bytes);File.WriteAllText(output+".manifest.json",manifest.ToJsonString(new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
}
