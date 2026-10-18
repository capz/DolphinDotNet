using DolphinDotNet.Effects;
using System.Text.Json;
using System.Text.Json.Nodes;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
static void Reject(string graph,string text){try{EffectCompiler.Compile(graph);}catch(ArgumentException e){Check(e.Message.Contains(text),e.Message);return;}throw new Exception("Graph accepted: "+text);}
static int[] Evaluate(byte[] p,int intensity,int? thresholdLow=null,int? thresholdHigh=null)
{
    var registers=new int[4][];var colors=new int[4][];
    for(int i=0;i<4;i++)colors[i]=p.Skip(64+i*36).Take(4).Select(x=>(int)x).ToArray();
    if(thresholdLow.HasValue){colors[0][0]=thresholdLow.Value;colors[0][1]=thresholdHigh!.Value;}
    for(int i=0;i<p[8];i++){
        int at=176+i*8,ksel=p[at+6];int[] k=ksel>=12?colors[(ksel-12)%4]:[255,255,255,255];
        int channel=ksel>=16?(ksel-16)/4:-1;
        int Read(int selector,int c)=>selector switch {15=>0,12=>255,10=>intensity,14=>k[channel<0?c:channel],0 or 2 or 4 or 6=>registers[selector/2][c],_=>throw new Exception("Bad selector")};
        var value=new int[3];
        for(int c=0;c<3;c++){
            int a=Read(p[at],c),b=Read(p[at+1],c),t=Read(p[at+2],c),d=Read(p[at+3],c);
            value[c]=Math.Clamp(p[at+4]==8?d+(Read(p[at],0)>Read(p[at+1],0)?t:0):a+((b-a)*(t+(t>>7))>>8)+d,0,255);
        }
        registers[p[at+5]]=value;
    }
    return registers[0];
}
string source=File.ReadAllText("samples/ShaderDemo/toon.dndfx.json");
var compiled=EffectCompiler.Compile(source);Check(compiled.StageCount==9&&compiled.PeakRegisters<=4,"Unexpected toon budgets");
for(int low=40;low<=120;low+=20)for(int value=0;value<256;value++){
    int high=low+100;var actual=Evaluate(compiled.Package,value,low,high);
    int[] expected=value>high?[70,220,248]:value>low?[30,90,158]:[12,24,64];
    Check(actual.SequenceEqual(expected),$"Toon mismatch: {low}/{high}/{value}");
}
string Graph(object[] nodes,string output="result")=>JsonSerializer.Serialize(new{uniforms=Array.Empty<object>(),nodes,output});
var basic=Graph([new{id="x",op="raster"},new{id="result",op="multiply",a="x",b="x"}]);
var square=EffectCompiler.Compile(basic);
for(int i=0;i<256;i++)Check(Math.Abs(Evaluate(square.Package,i)[0]-i*i/255)<=1,"Multiply quantization");
var identity=EffectCompiler.Compile(Graph([new{id="x",op="raster"},new{id="one",op="one"},new{id="result",op="multiply",a="x",b="one"}]));
Check(identity.StageCount==1,"Identity folding failed");
Reject(Graph([new{id="result",op="sin"}]),"unsupported operation");
Reject(Graph([new{id="result",op="add",a="result",b="result"}]),"Cycle");
Reject(Graph([new{id="result",op="add",a="missing",b="missing"}]),"Unknown input");
// A chain uses very few registers but must exceed the stage budget.
var chain=new JsonArray(new JsonObject{["id"]="x",["op"]="raster"});string last="x";
for(int i=0;i<20;i++){string id="n"+i;chain.Add(new JsonObject{["id"]=id,["op"]="multiply",["a"]=last,["b"]="x"});last=id;}
Reject(new JsonObject{["uniforms"]=new JsonArray(),["nodes"]=chain,["output"]=last}.ToJsonString(),"stage budget");
// Shared expressions should not manufacture redundant stages.
var tree=new JsonArray(new JsonObject{["id"]="x",["op"]="raster"});int next=0;
string Tree(int depth){if(depth==0){string leaf="leaf"+(next++);tree.Add(new JsonObject{["id"]=leaf,["op"]="greater",["a"]="x",["b"]="x"});return leaf;}string a=Tree(depth-1),b=Tree(depth-1),id="t"+(next++);tree.Add(new JsonObject{["id"]=id,["op"]="add",["a"]=a,["b"]=b});return id;}
// Verify common-expression sharing instead of manufacturing redundant stages.
string output=Tree(3);var shared=EffectCompiler.Compile(new JsonObject{["uniforms"]=new JsonArray(),["nodes"]=tree,["output"]=output}.ToJsonString());Check(shared.StageCount<=5,"Expression sharing failed");
var leftOne=EffectCompiler.Compile(Graph([new{id="x",op="raster"},new{id="one",op="one"},new{id="result",op="multiply",a="one",b="x"}]));
Check(Evaluate(leftOne.Package,64)[0]==63,"Left-one folding changed TEV interpolation rounding");
var folded=EffectCompiler.Compile(Graph([new{id="x",op="raster"},new{id="zero",op="zero"},new{id="result",op="multiply",a="x",b="zero"}]));
Check(folded.StageCount==1&&Evaluate(folded.Package,200).SequenceEqual(new[]{0,0,0}),"Constant folding failed");
var texture=EffectCompiler.Compile(Graph([new{id="result",op="texture"}]));Check(texture.RequiresTexture,"Missing texture capability");

var pressure=JsonNode.Parse("""
{"uniforms":[{"name":"A","rgba":[1,2,3,4]}],"nodes":[
{"id":"a","op":"uniform","name":"A","channel":"r"},
{"id":"b","op":"uniform","name":"A","channel":"g"},
{"id":"c","op":"uniform","name":"A","channel":"b"},
{"id":"d","op":"uniform","name":"A","channel":"a"},
{"id":"ab","op":"add","a":"a","b":"b"},
{"id":"abc","op":"add","a":"ab","b":"c"},
{"id":"abcd","op":"add","a":"abc","b":"d"},
{"id":"ac","op":"multiply","a":"a","b":"c"},
{"id":"bd","op":"multiply","a":"b","b":"d"},
{"id":"reuse","op":"add","a":"ac","b":"bd"},
{"id":"result","op":"add","a":"abcd","b":"reuse"}],"output":"result"}
""")!;
Reject(pressure.ToJsonString(),"register budget");
var tooMany=JsonNode.Parse(source)!;tooMany["uniforms"]!.AsArray().Add(new JsonObject{["name"]="Extra",["rgba"]=new JsonArray(0,0,0,255)});
Reject(tooMany.ToJsonString(),"Uniform budget");


string delimited="""
{"uniforms":[{"name":"A","rgba":[1,2,4,8]}],"nodes":[
{"id":"x,y","op":"uniform","name":"A","channel":"r"},
{"id":"z","op":"uniform","name":"A","channel":"g"},
{"id":"x","op":"uniform","name":"A","channel":"b"},
{"id":"y,z","op":"uniform","name":"A","channel":"a"},
{"id":"first","op":"add","a":"x,y","b":"z"},
{"id":"second","op":"add","a":"x","b":"y,z"},
{"id":"result","op":"add","a":"first","b":"second"}],"output":"result"}
""";
Check(Evaluate(EffectCompiler.Compile(delimited).Package,0)[0]==15,"Expression IDs aliased");

Check(compiled.Package.SequenceEqual(EffectCompiler.Compile(source).Package),"Non-deterministic package");
Console.WriteLine("Effect compiler: toon boundary reference, quantization, folding, sharing, capability and diagnostic checks passed.");
