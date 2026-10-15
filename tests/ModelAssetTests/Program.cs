using DolphinDotNet.Imaging;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using DolphinDotNet.Assets;

string output=args.Length==0?Path.Combine(Path.GetTempPath(),"dnd-model-fixtures"):Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"sd"));
var binary=new MemoryStream();var views=new JsonArray();var accessors=new JsonArray();
int Attribute(float[] values,string type,int components)
{
    int offset=(int)binary.Position;foreach(float v in values)binary.Write(BitConverter.GetBytes(v));int view=views.Count;views.Add(new JsonObject{["buffer"]=0,["byteOffset"]=offset,["byteLength"]=values.Length*4});int index=accessors.Count;accessors.Add(new JsonObject{["bufferView"]=view,["componentType"]=5126,["count"]=values.Length/components,["type"]=type});return index;
}
int Joints(ushort[] values){int offset=(int)binary.Position;foreach(ushort v in values)binary.Write(BitConverter.GetBytes(v));int view=views.Count;views.Add(new JsonObject{["buffer"]=0,["byteOffset"]=offset,["byteLength"]=values.Length*2});int index=accessors.Count;accessors.Add(new JsonObject{["bufferView"]=view,["componentType"]=5123,["count"]=values.Length/4,["type"]="VEC4"});return index;}
int position=Attribute([0,0,0,1,0,0,0,1,0],"VEC3",3),normal=Attribute([0,0,1,0,0,1,0,0,1],"VEC3",3),uv=Attribute([0,0,1,0,0,1],"VEC2",2),joints=Joints([0,1,0,0,0,1,0,0,0,1,0,0]),weights=Attribute([0.5f,0.5f,0,0,0.5f,0.5f,0,0,0.5f,0.5f,0,0],"VEC4",4),inverse=Attribute([1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1,1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],"MAT4",16),time=Attribute([0,1],"SCALAR",1),translation=Attribute([0,0,0,2,0,0],"VEC3",3),step=Attribute([0,0,0,4,0,0],"VEC3",3),cubic=Attribute([0,0,0,0,0,0,0,0,0,0,0,0,2,0,0,0,0,0],"VEC3",3),rot=Attribute([0,0,0,1,0,0,1,0],"VEC4",4);
using var image=new RasterImage(4,4);for(int y=0;y<4;y++)for(int x=0;x<4;x++)image[x,y]=new Rgba32((byte)(x*30),(byte)(y*40),50,255);using var png=new MemoryStream();image.SaveAsPng(png);
var root=new JsonObject{
 ["asset"]=new JsonObject{["version"]="2.0"},["scene"]=0,["scenes"]=new JsonArray(new JsonObject{["nodes"]=new JsonArray(0,2)}),
 ["buffers"]=new JsonArray(new JsonObject{["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(binary.ToArray()),["byteLength"]=(int)binary.Length}),["bufferViews"]=views,["accessors"]=accessors,
 ["nodes"]=new JsonArray(new JsonObject{["name"]="Ancestor",["children"]=new JsonArray(1)},new JsonObject{["name"]="Bone"},new JsonObject{["name"]="Mesh",["mesh"]=0,["skin"]=0}),
 ["skins"]=new JsonArray(new JsonObject{["joints"]=new JsonArray(0,1),["inverseBindMatrices"]=inverse}),
 ["images"]=new JsonArray(new JsonObject{["uri"]="data:image/png;base64,"+Convert.ToBase64String(png.ToArray())}),["textures"]=new JsonArray(new JsonObject{["source"]=0}),
 ["materials"]=new JsonArray(new JsonObject{["extensions"]=new JsonObject{["KHR_materials_unlit"]=new JsonObject()},["alphaMode"]="MASK",["pbrMetallicRoughness"]=new JsonObject{["baseColorTexture"]=new JsonObject{["index"]=0},["metallicFactor"]=0}}),
 ["meshes"]=new JsonArray(new JsonObject{["primitives"]=new JsonArray(new JsonObject{["material"]=0,["attributes"]=new JsonObject{["POSITION"]=position,["NORMAL"]=normal,["TEXCOORD_0"]=uv,["JOINTS_0"]=joints,["WEIGHTS_0"]=weights}})})};
JsonObject Animation(string name,int target,int property,int outputAccessor,string interpolation)=>new(){["name"]=name,["samplers"]=new JsonArray(new JsonObject{["input"]=time,["output"]=outputAccessor,["interpolation"]=interpolation}),["channels"]=new JsonArray(new JsonObject{["sampler"]=0,["target"]=new JsonObject{["node"]=target,["path"]=property==0?"translation":"rotation"}})};
root["animations"]=new JsonArray(Animation("Walk",1,0,translation,"LINEAR"),Animation("Step",1,0,step,"STEP"),Animation("Cubic",1,0,cubic,"CUBICSPLINE"),Animation("Turn",1,1,rot,"LINEAR"),Animation("Parent",0,0,translation,"LINEAR"));
string gltf=Path.Combine(output,"animated.gltf"),package=Path.Combine(output,"sd","animated.dndmodel");File.WriteAllText(gltf,root.ToJsonString());ModelConverter.Convert(gltf,package);
byte[] bytes=File.ReadAllBytes(package);if(BinaryPrimitives.ReadUInt32BigEndian(bytes)!=0x444e444d)throw new Exception("Package magic.");
int textureOffset=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16+3*16+4)));var texture=bytes.AsSpan(textureOffset+4+28,64);if(texture[0]!=255||texture[1]!=0||texture[3]!=30||texture[32]!=0||texture[33]!=50||texture[40]!=40)throw new Exception("GX RGBA8 tile fixture.");
ModelConverter.Convert(gltf,package+".second");if(!bytes.SequenceEqual(File.ReadAllBytes(package+".second")))throw new Exception("Nondeterministic package.");
// External buffers and images, plus GLB binary geometry.
root["buffers"]![0]!["uri"]="geometry.bin";root["images"]![0]!["uri"]="texture.png";File.WriteAllBytes(Path.Combine(output,"geometry.bin"),binary.ToArray());File.WriteAllBytes(Path.Combine(output,"texture.png"),png.ToArray());File.WriteAllText(gltf,root.ToJsonString());ModelConverter.Convert(gltf,package+".external");if(!bytes.SequenceEqual(File.ReadAllBytes(package+".external")))throw new Exception("External dependencies differ.");
root["buffers"]![0]!.AsObject().Remove("uri");var json=Encoding.UTF8.GetBytes(root.ToJsonString());int jsonLength=(json.Length+3)&~3,binLength=((int)binary.Length+3)&~3;using(var file=File.Create(Path.Combine(output,"animated.glb"))){using var w=new BinaryWriter(file);w.Write(0x46546c67);w.Write(2);w.Write(12+8+jsonLength+8+binLength);w.Write(jsonLength);w.Write(0x4e4f534a);w.Write(json);w.Write(Enumerable.Repeat((byte)32,jsonLength-json.Length).ToArray());w.Write(binLength);w.Write(0x004e4942);w.Write(binary.ToArray());w.Write(new byte[binLength-(int)binary.Length]);}ModelConverter.Convert(Path.Combine(output,"animated.glb"),package+".glb");if(!bytes.SequenceEqual(File.ReadAllBytes(package+".glb")))throw new Exception("GLB differs.");
void Reject(Action mutate){var copy=root.DeepClone();mutate();File.WriteAllText(gltf,root.ToJsonString());try{ModelConverter.Convert(gltf,package+".bad");throw new Exception("Invalid source accepted.");}catch(InvalidDataException){}root=copy.AsObject();}
root["buffers"]![0]!["uri"]="geometry.bin";
Reject(()=>root["nodes"]![1]!["children"]=new JsonArray(0));Reject(()=>root["meshes"]![0]!["primitives"]![0]!["targets"]=new JsonArray(new JsonObject()));Reject(()=>root["materials"]![0]!["normalTexture"]=new JsonObject{["index"]=0});Reject(()=>root["accessors"]![position]!["count"]=999999);
byte[] corrupt=(byte[])bytes.Clone();corrupt[200]^=1;File.WriteAllBytes(Path.Combine(output,"sd","corrupt.dndmodel"),corrupt);
// Leave the original valid authoring fixture for the independently exercised Assimp adapter.
File.WriteAllText(gltf,root.ToJsonString());
File.Copy(package,Path.Combine(output,"sd","cache.dndmodel"),true);
var large=new byte[bytes.Length+65536];bytes.CopyTo(large,0);BinaryPrimitives.WriteUInt32BigEndian(large.AsSpan(8),(uint)large.Length);File.WriteAllBytes(Path.Combine(output,"sd","large.dndmodel"),large);
if(args.Length>1)
{
    string assimp=args[1];
    void Export(params string[] arguments){var info=new System.Diagnostics.ProcessStartInfo(assimp){UseShellExecute=false};foreach(var arg in arguments)info.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(info)!;process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Assimp fixture export failed.");}
    string fbx=Path.Combine(output,"animated.fbx");Export("export",gltf,fbx,"-ffbxa");
    // Assimp 5.3's FBX exporter writes key timestamps in milliseconds while stacks use seconds.
    // This independently specified 1-second fixture uses FBX's 46186158000 ticks/second.
    File.WriteAllText(fbx,File.ReadAllText(fbx).Replace("46186158000000","46186158000",StringComparison.Ordinal));
    ModelConverter.Convert(fbx,Path.Combine(output,"sd","fbx.dndmodel"),new(){Assimp=assimp,ApproximateMaterials=true});
    string obj=Path.Combine(output,"model.obj");File.WriteAllText(obj,"mtllib model.mtl\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nusemtl Surface\nf 1/1 2/2 3/3 4/4\n");File.WriteAllText(Path.Combine(output,"model.mtl"),"newmtl Surface\nKd 1 1 1\nmap_Kd texture.png\n");ModelConverter.Convert(obj,Path.Combine(output,"sd","obj.dndmodel"),new(){Assimp=assimp,ApproximateMaterials=true});
}
var buildProject=new System.Xml.Linq.XElement("Project",new System.Xml.Linq.XAttribute("Sdk","Microsoft.NET.Sdk"),
    new System.Xml.Linq.XElement("PropertyGroup",new System.Xml.Linq.XElement("TargetFramework","net8.0")),
    new System.Xml.Linq.XElement("Import",new System.Xml.Linq.XAttribute("Project",Path.GetFullPath("tools/DolphinDotNet.Assets/msbuild/DolphinDotNet.Assets.targets"))),
    new System.Xml.Linq.XElement("ItemGroup",new System.Xml.Linq.XElement("DndModel",new System.Xml.Linq.XAttribute("Include",gltf),new System.Xml.Linq.XElement("DndOutput",Path.Combine(output,"sd","built.dndmodel")),new System.Xml.Linq.XElement("Options","--texture-limit 256"))));
File.WriteAllText(Path.Combine(output,"Build.csproj"),buildProject.ToString());
Console.WriteLine("Model converter fixtures passed: "+output);

using(var drawingImage=new RasterImage(5,3,new Rgba32(10,20,30,40)))
{
    byte[] packed=DrawingPackages.Texture(drawingImage);
    if(packed.Length!=160||BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(4,4))!=1||packed[32]!=40||packed[33]!=10||packed[64]!=20||packed[65]!=30||packed[96]!=40||packed[98]!=0)throw new Exception("Drawing texture packing or tile padding.");
    byte[] font=DrawingPackages.Font(drawingImage,1,1,32,15);
    if(font.Length!=192||font[0]!='D'||font[1]!='N'||font[2]!='F'||font[3]!='T'||BinaryPrimitives.ReadInt32LittleEndian(font.AsSpan(24,4))!=packed.Length)throw new Exception("Drawing font header.");
    try{DrawingPackages.Font(drawingImage,2,2);throw new Exception("Invalid font accepted.");}catch(ArgumentException){}
}
