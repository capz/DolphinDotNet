using System.Text.Json;
namespace DolphinDotNet.Effects;

public sealed record EffectCompilation(byte[] Package,int StageCount,int UniformCount,int PeakRegisters,bool RequiresTexture);

/// <summary>Build-time compiler for a bounded RGB DAG. No runtime code generation.</summary>
public static class EffectCompiler
{
    private sealed class Node
    {
        internal string Id="",Op="";
        internal Node[] Inputs=[];
        internal int Slot,Channel,Uses,Serial,Register=-1;
    }
    public static EffectCompilation Compile(string json)
    {
        if(json.Length>65536||System.Text.Encoding.UTF8.GetByteCount(json)>65536)throw new ArgumentException("Graph exceeds 64 KiB.");
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=32});
        var root=doc.RootElement;
        var names=new List<string>();var colors=new List<byte[]>();
        foreach(var u in root.GetProperty("uniforms").EnumerateArray()) {
            string name=u.GetProperty("name").GetString()!;
            if(string.IsNullOrEmpty(name)||name.Length>31||name.Any(c=>c<33||c>126)||names.Contains(name))throw new ArgumentException("Uniform names must be unique printable ASCII (1..31 bytes).");
            var rgba=u.GetProperty("rgba").EnumerateArray().Select(c=>c.GetByte()).ToArray();
            if(rgba.Length!=4)throw new ArgumentException($"Uniform {name}: expected four RGBA bytes.");
            names.Add(name);colors.Add(rgba);
        }
        if(names.Count>4)throw new ArgumentException("Uniform budget exceeded: at most 4 color parameters. Pack scalar thresholds into channels.");
        var definitions=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var n in root.GetProperty("nodes").EnumerateArray()) {
            string id=n.GetProperty("id").GetString()!;
            if(string.IsNullOrEmpty(id)||!definitions.TryAdd(id,n))throw new ArgumentException("Node IDs must be nonempty and unique.");
        }
        if(definitions.Count>128)throw new ArgumentException("Graph exceeds 128 nodes.");
        int nextNode=0;
        var built=new Dictionary<string,Node>();var visiting=new HashSet<string>();var common=new Dictionary<string,Node>();
        Node Build(string id,int depth) {
            if(depth>64)throw new ArgumentException($"Node {id}: expression depth exceeds 64.");
            if(built.TryGetValue(id,out var old))return old;
            if(!definitions.TryGetValue(id,out var definition))throw new ArgumentException($"Unknown input node {id}.");
            if(!visiting.Add(id))throw new ArgumentException($"Cycle at node {id}.");
            string op=definition.GetProperty("op").GetString()!;
            string[] keys=op switch {"add" or "multiply" or "greater"=>["a","b"],"mix"=>["a","b","t"],"raster" or "texture" or "uniform" or "zero" or "one"=>[],_=>throw new ArgumentException($"Node {id}: unsupported operation {op}.")};
            var node=new Node{Id=id,Op=op,Inputs=keys.Select(k=>Build(definition.GetProperty(k).GetString()!,depth+1)).ToArray()};
            if(op=="uniform") {
                node.Slot=names.IndexOf(definition.GetProperty("name").GetString()!);
                if(node.Slot<0)throw new ArgumentException($"Node {id}: unknown uniform.");
                string channel=definition.TryGetProperty("channel",out var ch)?ch.GetString()!:"rgb";
                node.Channel=channel switch {"rgb"=>0,"r"=>1,"g"=>2,"b"=>3,"a"=>4,_=>throw new ArgumentException($"Node {id}: invalid channel {channel}.")};
            }
            // Exact constant/identity folding preserves TEV quantization.
            if(op=="add"&&node.Inputs[1].Op=="zero"||op=="multiply"&&node.Inputs[1].Op=="one")node=node.Inputs[0];
            else if(op=="add"&&node.Inputs[0].Op=="zero")node=node.Inputs[1];
            else if(op=="multiply"&&node.Inputs.Any(n=>n.Op=="zero")||op=="greater"&&node.Inputs[0]==node.Inputs[1])node=new Node{Id=id,Op="zero"};
            else if(op=="add"&&node.Inputs.Any(n=>n.Op=="one"))node=new Node{Id=id,Op="one"};
            else if(op=="mix"&&(node.Inputs[0]==node.Inputs[1]||node.Inputs[2].Op=="zero"))node=node.Inputs[0];
            else if(op=="mix"&&node.Inputs[2].Op=="one")node=node.Inputs[1];
            string key=node.Op+":"+node.Slot+":"+node.Channel+":"+string.Join(",",node.Inputs.Select(n=>n.Serial));
            if(common.TryGetValue(key,out var same))node=same;else {node.Serial=++nextNode;common.Add(key,node);}
            visiting.Remove(id);built[id]=node;return node;
        }
        Node output=Build(root.GetProperty("output").GetString()!,0);
        var order=new List<Node>();var seen=new HashSet<Node>();
        void Visit(Node n){if(!seen.Add(n))return;foreach(var input in n.Inputs){Visit(input);input.Uses++;}order.Add(n);}
        Visit(output);output.Uses++;
        var stages=new List<byte[]>();var occupied=new Node?[4];int peak=0;bool texture=false;
        int Selector(Node n)=>n.Op switch {"zero"=>15,"one"=>12,"raster"=>10,"texture"=>8,_=>n.Register<0?throw new InvalidOperationException("Compiler register invariant."):n.Register*2};
        void Emit(byte a,byte b,byte c,byte d,byte op,int dest,int konst,Node n) {
            if(stages.Count==16)throw new ArgumentException($"Node {n.Id}: stage budget exceeded (requires more than 16 stages).");
            stages.Add([a,b,c,d,op,(byte)dest,(byte)konst,0]);
        }
        foreach(var n in order) {
            if(n.Op=="texture")texture=true;
            if(n.Op is "raster" or "texture" or "zero" or "one")continue;
            var inputs=n.Inputs.Select(Selector).ToArray();
            foreach(var input in n.Inputs)if(--input.Uses==0&&input.Register>=0)occupied[input.Register]=null;
            int dest=Array.FindIndex(occupied,x=>x==null);
            if(dest<0)throw new ArgumentException($"Node {n.Id}: register budget exceeded (4 live TEV results). Simplify or reassociate the expression.");
            // Reads happen before the output write, permitting last-use reuse.
            n.Register=dest;occupied[dest]=n;peak=Math.Max(peak,occupied.Count(x=>x!=null));
            byte a=15,b=15,c=15,d=15,op=0;int konst=0;
            if(n.Op=="uniform"){d=14;konst=12+4*n.Channel+n.Slot;}
            else if(n.Op=="add"){b=(byte)inputs[0];c=12;d=(byte)inputs[1];}
            else if(n.Op=="multiply"){b=(byte)inputs[0];c=(byte)inputs[1];}
            else if(n.Op=="mix"){a=(byte)inputs[0];b=(byte)inputs[1];c=(byte)inputs[2];}
            else if(n.Op=="greater"){a=(byte)inputs[0];b=(byte)inputs[1];c=12;op=8;}
            Emit(a,b,c,d,op,dest,konst,n);
        }
        if(output.Register!=0||stages.Count==0)Emit(15,15,15,(byte)Selector(output),0,0,0,output);
        var package=new byte[176+stages.Count*8];"DNEF"u8.CopyTo(package);package[4]=1;
        package[8]=(byte)stages.Count;package[12]=(byte)(texture?1:0);package[16]=(byte)names.Count;
        for(int i=0;i<names.Count;i++) {
            System.Text.Encoding.ASCII.GetBytes(names[i]).CopyTo(package,32+i*36);colors[i].CopyTo(package,64+i*36);
        }
        for(int i=0;i<stages.Count;i++)stages[i].CopyTo(package,176+i*8);
        return new(package,stages.Count,names.Count,peak,texture);
    }
}
