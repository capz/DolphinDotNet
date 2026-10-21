using DolphinDotNet.Imaging;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using DolphinDotNet.Assets;

string output=args.Length==0?Path.Combine(Path.GetTempPath(),"dnd-model-fixtures"):Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"sd"));
PsoImportTests.Run(output);
ModelCompatibilityTests.Run(output);
FZeroImportTests.Run(output);
GameCubeImportTests.Run(output);
RenderWareMeleeFixtures.Run(output);
PsoEpisode3Fixtures.Run(output);
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
// Synthetic, byte-accurate GC PSO GJCM: one Ninja node, a GX property
// table, independently sized indices, and an indexed triangle primitive.
byte[] gj = new byte[8 + 194];
"GJCM"u8.CopyTo(gj.AsSpan(0,4));
BinaryPrimitives.WriteUInt32LittleEndian(gj.AsSpan(4,4), 194);
var body=gj.AsSpan(8);
void Gj32(int at,uint v)=>BinaryPrimitives.WriteUInt32BigEndian(gj.AsSpan(8+at,4),v);
void Gj16(int at,ushort v)=>BinaryPrimitives.WriteUInt16BigEndian(gj.AsSpan(8+at,2),v);
void GjFloat(int at,float v)=>Gj32(at,unchecked((uint)BitConverter.SingleToInt32Bits(v)));
Gj32(4,52); // mesh offset
GjFloat(32,1);GjFloat(36,1);GjFloat(40,1); // node scale
Gj32(52,88);Gj32(60,164);Gj16(68,1); // property offset and one strip group
body[88]=1;body[89]=12;Gj16(90,3);Gj32(96,128);Gj32(100,36);
body[104]=255; // property-list terminator
GjFloat(128,0);GjFloat(132,0);GjFloat(136,0);
GjFloat(140,1);GjFloat(144,0);GjFloat(148,0);
GjFloat(152,0);GjFloat(156,1);GjFloat(160,0);
Gj32(164,180);Gj32(168,1);Gj32(172,188);Gj32(176,6);
body[180]=1;Gj32(184,8); // GX position present, 8-bit indices
body[188]=0x90;Gj16(189,3);body[191]=0;body[192]=1;body[193]=2;
string gjPath=Path.Combine(output,"geometry.gj");
File.WriteAllBytes(gjPath,gj);
string gjPackage=Path.Combine(output,"sd","geometry-gj.dndmodel");
ModelConverter.Convert(gjPath,gjPackage);
byte[] gjOutput=File.ReadAllBytes(gjPackage);
if(BinaryPrimitives.ReadUInt32BigEndian(gjOutput)!=0x444e444d)
    throw new Exception("Direct PSO GJ package magic.");
ModelConverter.Convert(gjPath,gjPackage+".again");
if(!gjOutput.SequenceEqual(File.ReadAllBytes(gjPackage+".again")))
    throw new Exception("Nondeterministic direct PSO GJ import.");
byte[] broken=(byte[])gj.Clone();
BinaryPrimitives.WriteUInt32BigEndian(broken.AsSpan(8+172,4),0xfffffff0);
File.WriteAllBytes(gjPath,broken);
try{ModelConverter.Convert(gjPath,gjPackage+".broken");throw new Exception("Broken GJ stream accepted.");}
catch(InvalidDataException){}
File.WriteAllBytes(gjPath,gj);
// Blue Burst XJ uses LE NJCM and fixed vertex-info/strip descriptors.
byte[] xj = new byte[8+174];
"NJCM"u8.CopyTo(xj.AsSpan(0,4));
BinaryPrimitives.WriteUInt32LittleEndian(xj.AsSpan(4,4),174);
void Xj32(int p,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(xj.AsSpan(8+p,4),v);
void Xj16(int p,ushort v)=>BinaryPrimitives.WriteUInt16LittleEndian(xj.AsSpan(8+p,2),v);
void XjFloat(int p,float v)=>Xj32(p,unchecked((uint)BitConverter.SingleToInt32Bits(v)));
Xj32(4,52);XjFloat(32,1);XjFloat(36,1);XjFloat(40,1);
Xj32(56,96);Xj32(60,1);Xj32(64,112);Xj32(68,1);
Xj32(96+4,132);Xj32(96+8,12);Xj32(96+12,3);
Xj32(112,168);Xj32(112+4,0);Xj32(112+8,168);Xj32(112+12,3);
XjFloat(132,0);XjFloat(136,0);XjFloat(140,0);
XjFloat(144,1);XjFloat(148,0);XjFloat(152,0);
XjFloat(156,0);XjFloat(160,1);XjFloat(164,0);
Xj16(168,0);Xj16(170,1);Xj16(172,2);
string xjPath=Path.Combine(output,"geometry.xj");
File.WriteAllBytes(xjPath,xj);
string xjPackage=Path.Combine(output,"sd","geometry-xj.dndmodel");
ModelConverter.Convert(xjPath,xjPackage);
byte[] xjData=File.ReadAllBytes(xjPackage);
if(BinaryPrimitives.ReadUInt32BigEndian(xjData)!=0x444e444d)
    throw new Exception("Direct PSO XJ binary import failed.");
File.WriteAllText(Path.Combine(output,"sd","live.obj"),
    "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nf 1/1 2/2 3/3\n");
string gltf=Path.Combine(output,"animated.gltf"),package=Path.Combine(output,"sd","animated.dndmodel");File.WriteAllText(gltf,root.ToJsonString());ModelConverter.Convert(gltf,package);
byte[] bytes=File.ReadAllBytes(package);if(BinaryPrimitives.ReadUInt32BigEndian(bytes)!=0x444e444d)throw new Exception("Package magic.");
int textureOffset=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16+3*16+4)));var texture=bytes.AsSpan(textureOffset+4+28,64);if(texture[0]!=255||texture[1]!=0||texture[3]!=30||texture[32]!=0||texture[33]!=50||texture[40]!=40)throw new Exception("GX RGBA8 tile fixture.");
ModelConverter.Convert(gltf,package+".second");if(!bytes.SequenceEqual(File.ReadAllBytes(package+".second")))throw new Exception("Nondeterministic package.");
{
    // glTF BLEND maps to DNMD mode bit 2, consumed by the existing depth-sorted
    // GX alpha-blend render path without approximation.
    var blended=root.DeepClone().AsObject();
    blended["materials"]![0]!["alphaMode"]="BLEND";
    blended["materials"]![0]!["pbrMetallicRoughness"]!["baseColorFactor"]=new JsonArray(1,1,1,0.4);
    File.WriteAllText(gltf,blended.ToJsonString());
    ModelConverter.Convert(gltf,package+".blend");
    var blendPackage=File.ReadAllBytes(package+".blend");
    int materialsOffset=checked((int)BinaryPrimitives.ReadUInt32BigEndian(blendPackage.AsSpan(52,4)));
    int modeOffset=materialsOffset+4+32+16+4;
    if(BinaryPrimitives.ReadUInt32BigEndian(blendPackage.AsSpan(modeOffset,4))!=6)
        throw new Exception("BLEND/unlit bits not retained in material.");
    blended["materials"]![0]!["alphaMode"]="UNKNOWN";
    File.WriteAllText(gltf,blended.ToJsonString());
    try{ModelConverter.Convert(gltf,package+".invalid-blend");throw new Exception("Unrecognized alpha mode accepted.");}
    catch(InvalidDataException){}
    File.WriteAllText(gltf,root.ToJsonString());
}

{
    // UV1 and KHR_texture_transform must survive as baked target UV0 without
    // changing the runtime DNMD package layout.
    var transformed=root.DeepClone().AsObject();
    var primitive=transformed["meshes"]![0]!["primitives"]![0]!;
    primitive["attributes"]!["TEXCOORD_1"]=uv;
    transformed["extensionsUsed"]=new JsonArray("KHR_texture_transform");
    transformed["extensionsRequired"]=new JsonArray("KHR_texture_transform");
    transformed["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]!["texCoord"]=1;
    transformed["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]!["extensions"]=new JsonObject {
        ["KHR_texture_transform"]=new JsonObject {
            ["offset"]=new JsonArray(0.25,0.5),["scale"]=new JsonArray(0.5,0.5),
            ["rotation"]=0,["texCoord"]=1
        }
    };
    File.WriteAllText(gltf,transformed.ToJsonString());
    ModelConverter.Convert(gltf,package+".uv-transform");
    var converted=File.ReadAllBytes(package+".uv-transform");
    if(bytes.SequenceEqual(converted))
        throw new Exception("Texture transform failed to modify geometry.");
    // DNMD: mesh section offset at byte 36, one mesh count, 44-byte mesh
    // header, then position(12) + normal(12) before UV(8).
    int meshOffset=checked((int)BinaryPrimitives.ReadUInt32BigEndian(converted.AsSpan(36,4)));
    int firstUv=meshOffset+4+44+24;
    float U(int offset)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(converted.AsSpan(offset,4)));
    if(Math.Abs(U(firstUv)-0.25f)>0.0001f||Math.Abs(U(firstUv+4)-0.5f)>0.0001f)
        throw new Exception("Incorrect baked KHR_texture_transform offset/scale.");
    // Second vertex originally has UV (1,0), transformed to (0.75,0.5).
    if(Math.Abs(U(firstUv+80)-0.75f)>0.0001f||Math.Abs(U(firstUv+84)-0.5f)>0.0001f)
        throw new Exception("Incorrect baked KHR_texture_transform UV1.");
    transformed["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]!["extensions"]!["KHR_texture_transform"]!["texCoord"]=2;
    File.WriteAllText(gltf,transformed.ToJsonString());
    try { ModelConverter.Convert(gltf,package+".bad-uv");throw new Exception("Invalid texture coordinate accepted."); }
    catch(InvalidDataException) {}
    File.WriteAllText(gltf,root.ToJsonString());
}


{
    // Explicit material approximation composites multiple compatible source
    // maps instead of discarding emissive and occlusion textures.
    var layered=root.DeepClone().AsObject();
    layered["materials"]![0]!["emissiveTexture"]=new JsonObject{["index"]=0};
    layered["materials"]![0]!["emissiveFactor"]=new JsonArray(0.25,0,0);
    layered["materials"]![0]!["occlusionTexture"]=new JsonObject{["index"]=0,["strength"]=0.5};
    File.WriteAllText(gltf,layered.ToJsonString());
    try{ModelConverter.Convert(gltf,package+".layers-strict");throw new Exception("Multi-texture layers silently accepted in strict mode.");}
    catch(InvalidDataException){}
    ModelConverter.Convert(gltf,package+".layers",new(){ApproximateMaterials=true});
    var layerBytes=File.ReadAllBytes(package+".layers");
    int layersTextureOffset=checked((int)BinaryPrimitives.ReadUInt32BigEndian(layerBytes.AsSpan(68,4)));
    int layersTextureCount=checked((int)BinaryPrimitives.ReadUInt32BigEndian(layerBytes.AsSpan(layersTextureOffset,4)));
    if(layersTextureCount!=2)throw new Exception("Layered material did not bake a distinct GX texture.");
    if(bytes.SequenceEqual(layerBytes))throw new Exception("Layered material package unchanged.");
    // Constant emissive color is a material contribution even without a map.
    // It must never be silently dropped by the strict converter.
    var emissionOnly=root.DeepClone().AsObject();
    emissionOnly["materials"]![0]!["emissiveFactor"]=new JsonArray(0.2,0.1,0);
    File.WriteAllText(gltf,emissionOnly.ToJsonString());
    try{ModelConverter.Convert(gltf,package+".emissive-strict");throw new Exception("Constant emission silently dropped.");}
    catch(InvalidDataException){}
    ModelConverter.Convert(gltf,package+".emissive-baked",new(){ApproximateMaterials=true});
    if(bytes.SequenceEqual(File.ReadAllBytes(package+".emissive-baked")))
        throw new Exception("Constant emission baking produced no change.");
    File.WriteAllText(gltf,layered.ToJsonString());
    layered["materials"]![0]!["emissiveTexture"]!["texCoord"]=1;
    File.WriteAllText(gltf,layered.ToJsonString());
    try{ModelConverter.Convert(gltf,package+".layers-bad-uv",new(){ApproximateMaterials=true});
        throw new Exception("Mismatched multi-map UVs accepted.");}
    catch(InvalidDataException){}
    File.WriteAllText(gltf,root.ToJsonString());
}
GxMaterialTests.Run(root,output);
StudioBakeTests.Run(root,output);
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
    // STL and PLY are accepted by the Assimp adapter, but the stock Ubuntu
    // Assimp 5.3 GLB exporter fails for some untextured static input shapes.
    // Qualify these with verified fixtures before making them mandatory CI.
    string obj=Path.Combine(output,"model.obj");File.WriteAllText(obj,"mtllib model.mtl\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nusemtl Surface\nf 1/1 2/2 3/3 4/4\n");File.WriteAllText(Path.Combine(output,"model.mtl"),"newmtl Surface\nKd 1 1 1\nmap_Kd texture.png\n");ModelConverter.Convert(obj,Path.Combine(output,"sd","obj.dndmodel"),new(){Assimp=assimp,ApproximateMaterials=true});
}
var buildProject=new System.Xml.Linq.XElement("Project",new System.Xml.Linq.XAttribute("Sdk","Microsoft.NET.Sdk"),
    new System.Xml.Linq.XElement("PropertyGroup",new System.Xml.Linq.XElement("TargetFramework","net8.0")),
    new System.Xml.Linq.XElement("Import",new System.Xml.Linq.XAttribute("Project",Path.GetFullPath("tools/DolphinDotNet.Assets/msbuild/DolphinDotNet.Assets.targets"))),
    new System.Xml.Linq.XElement("ItemGroup",new System.Xml.Linq.XElement("DndModel",new System.Xml.Linq.XAttribute("Include",gltf),new System.Xml.Linq.XElement("DndOutput",Path.Combine(output,"sd","built.dndmodel")),new System.Xml.Linq.XElement("Options","--texture-limit 256"))));
File.WriteAllText(Path.Combine(output,"Build.csproj"),buildProject.ToString());
Console.WriteLine("Model converter fixtures passed: "+output);
NativeImportTests.Run(output);
GlbImportTests.Run(output);

using(var drawingImage=new RasterImage(5,3,new Rgba32(10,20,30,40)))
{
    byte[] packed=DrawingPackages.Texture(drawingImage);
    if(packed.Length!=160||BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(4,4))!=1||packed[32]!=40||packed[33]!=10||packed[64]!=20||packed[65]!=30||packed[96]!=40||packed[98]!=0)throw new Exception("Drawing texture packing or tile padding.");
    byte[] font=DrawingPackages.Font(drawingImage,1,1,32,15);
    if(font.Length!=192||font[0]!='D'||font[1]!='N'||font[2]!='F'||font[3]!='T'||BinaryPrimitives.ReadInt32LittleEndian(font.AsSpan(24,4))!=packed.Length)throw new Exception("Drawing font header.");
    try{DrawingPackages.Font(drawingImage,2,2);throw new Exception("Invalid font accepted.");}catch(ArgumentException){}
}
