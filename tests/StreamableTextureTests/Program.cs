using System.Buffers.Binary;
using Dolphin.Formats;
using DolphinDotNet.Assets;
using DolphinDotNet.Imaging;

int checks=0;
void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
void Reject(Action action,string label){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Check(rejected,label);}
void Invalid(Action action,string label){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,label);}
void Put(byte[] b,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at,4),value);
int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at,4));
void MetadataCrc(byte[] b){int n=StreamableTextureDescriptor.GetMetadataBytes(b);Put(b,44,(int)StreamableTextureDescriptor.ComputeCrc32(b,64,n-64));}
byte[] Rgba(StreamableTextureAsset asset,int level)
{
    var d=StreamableTextureDescriptor.Read(asset.Bytes);var l=d.GetLevel(level);byte[] bti=new byte[32+l.Length];bti[0]=(byte)l.Format;BinaryPrimitives.WriteUInt16BigEndian(bti.AsSpan(2),(ushort)l.Width);BinaryPrimitives.WriteUInt16BigEndian(bti.AsSpan(4),(ushort)l.Height);bti[0x18]=1;BinaryPrimitives.WriteInt32BigEndian(bti.AsSpan(0x1c),32);Array.Copy(asset.Bytes,l.Offset,bti,32,l.Length);return BtiTexture.Read(bti).Rgba;
}
using var rectangular=new RasterImage(8,2,new Rgba32(240,80,16,255));
var rgba=StreamableTextures.Encode(rectangular,new(){Mipmaps=true,Format=StreamableTextureFormat.Rgba8});var desc=StreamableTextureDescriptor.Read(rgba.Bytes);
Check(desc.Count==4&&desc.GetLevel(3).Width==1&&desc.GetLevel(3).Height==1,"rectangular mip chain clamps each axis");
Check(desc.GetLevel(desc.CoarseIndex).Offset==Int(rgba.Bytes,36),"coarsest payload is physically first");
for(int i=0;i<desc.Count;i++)Check(desc.ValidatePayload(i,rgba.Bytes,desc.GetLevel(i).Offset),"independent payload CRC");
byte[] prefix=rgba.Bytes[..desc.MetadataBytes];Check(StreamableTextureDescriptor.Read(prefix,rgba.Bytes.Length).Count==4,"parser needs metadata only");
Check(StreamableTextures.Encode(rectangular,new(){Mipmaps=true,Format=StreamableTextureFormat.Rgba8}).Bytes.SequenceEqual(rgba.Bytes),"deterministic byte output");
Check(StreamableTextures.Encode(rectangular,new(){Mipmaps=true,Format=StreamableTextureFormat.Rgba8}).Manifest==rgba.Manifest,"deterministic manifest");
using var tiny=new RasterImage(1,1,new Rgba32(40,180,60,255));
foreach(var format in new[]{StreamableTextureFormat.Cmpr,StreamableTextureFormat.Rgba8}){
    var a=StreamableTextures.Encode(tiny,new(){Mipmaps=true,Format=format});var l=StreamableTextureDescriptor.Read(a.Bytes).GetLevel(0);Check(l.Length==(format==StreamableTextureFormat.Cmpr?32:64),"small mips retain native tile padding");var decoded=Rgba(a,0);Check(decoded[3]==255&&Math.Abs(decoded[1]-180)<6,"independent BTI decoder reads encoded pixels");
}
using var high=new RasterImage(13,7,new Rgba32(0,255,0,255));using var low=new RasterImage(3,2,new Rgba32(255,0,0,255));
var pair=StreamableTextures.Encode(high,new(){Format=StreamableTextureFormat.Rgba8},low);var p=StreamableTextureDescriptor.Read(pair.Bytes);Check(p.IsQualityPair&&p.Count==2&&p.GetLevel(1).Width==3&&p.GetLevel(0).Width==13,"NPOT low/high pair has no synthesized levels");Check(Rgba(pair,0)[1]==255&&Rgba(pair,1)[0]==255,"HIGH/LOW pixel identity");
var sparse=StreamableTextures.Encode(rectangular,new(){Mipmaps=true,LogicalLevels=new[]{0,2},Format=StreamableTextureFormat.Rgba8});var s=StreamableTextureDescriptor.Read(sparse.Bytes);Check(s.Count==2&&s.GetLevel(1).LogicalLod==2&&(s.Flags&StreamableTextureFlags.SparseLevels)!=0,"sparse logical exponents preserved");
var gutter=StreamableTextures.Encode(rectangular,new(){Mipmaps=true,Gutter=2,Format=StreamableTextureFormat.Rgba8});var g=StreamableTextureDescriptor.Read(gutter.Bytes);Check(g.GetLevel(0).Width==12&&Math.Abs(g.GetLevel(0).Uv.ScaleU-8f/12)<.00001&&Math.Abs(g.GetLevel(3).Uv.OffsetU-2f/5)<.00001,"per-level gutter mapping");Check(Rgba(gutter,0)[0]==240,"gutter pixels extrude source edges");
using var alpha=new RasterImage(2,2,new Rgba32(0,0,255,0));alpha[0,0]=new Rgba32(255,0,0,128);
var smooth=StreamableTextures.Encode(alpha,new(){Mipmaps=true});Check(StreamableTextureDescriptor.Read(smooth.Bytes).DefaultFormat==6,"auto preserves smooth alpha in RGBA8");var last=Rgba(smooth,1);Check(last[0]==255&&last[2]==0&&last[3]==32,"premultiplied-aware downsample avoids transparent blue fringe");Invalid(()=>StreamableTextures.Encode(alpha,new(){Format=StreamableTextureFormat.Cmpr}),"explicit CMPR rejects smooth alpha");
using var binary=new RasterImage(8,8,new Rgba32(255,0,0,255));for(int y=0;y<8;y++)for(int x=0;x<4;x++)binary[x,y]=new Rgba32(0,0,255,0);
var coverage=StreamableTextures.Encode(binary,new(){Mipmaps=true,Format=StreamableTextureFormat.Cmpr});var cd=StreamableTextureDescriptor.Read(coverage.Bytes);for(int i=0;i<cd.Count;i++){var l=cd.GetLevel(i);var pixels=Rgba(coverage,i);int covered=Enumerable.Range(0,l.Width*l.Height).Count(n=>pixels[n*4+3]==255);Check(Math.Abs(covered-l.Width*l.Height*.5)<=.5,"binary alpha coverage retained");}
Invalid(()=>StreamableTextures.Encode(high,new(){Mipmaps=true}),"NPOT full mip source rejected");Invalid(()=>StreamableTextures.Encode(rectangular,new(){Mipmaps=true,LogicalLevels=new[]{0,0}}),"duplicate sparse entries rejected");
foreach(int at in new[]{0,4,8,24,28,32,36,40,44,48,52,56,60}){var broken=(byte[])rgba.Bytes.Clone();Put(broken,at,-1);Reject(()=>StreamableTextureDescriptor.Read(broken),"corrupt fixed-header field rejected");}
foreach(int at in new[]{64,68,72,76,80,84,92}){var broken=(byte[])rgba.Bytes.Clone();Put(broken,at,-1);MetadataCrc(broken);Reject(()=>StreamableTextureDescriptor.Read(broken),"bad indexed entry rejected with valid metadata CRC");}
var invalidUv=(byte[])gutter.Bytes.Clone();Put(invalidUv,Int(invalidUv,48),0x7fc00000);MetadataCrc(invalidUv);Reject(()=>StreamableTextureDescriptor.Read(invalidUv),"NaN UV rejected");
var corrupt=(byte[])rgba.Bytes.Clone();corrupt[desc.GetLevel(0).Offset]^=1;var intact=StreamableTextureDescriptor.Read(corrupt);Check(!intact.ValidatePayload(0,corrupt,intact.GetLevel(0).Offset),"corrupted payload never passes level validation");
Reject(()=>StreamableTextureDescriptor.Read(prefix,rgba.Bytes.Length+32),"actual/declared file length mismatch");
for(int i=0;i<desc.MetadataBytes;i++)Reject(()=>StreamableTextureDescriptor.Read(prefix[..i],rgba.Bytes.Length),"all truncated metadata prefixes rejected");
var random=new Random(7319);for(int i=0;i<2000;i++){var fuzz=(byte[])rgba.Bytes.Clone();int at=random.Next(desc.MetadataBytes);fuzz[at]^=(byte)(1<<random.Next(8));try{StreamableTextureDescriptor.Read(fuzz);}catch(InvalidDataException){}checks++;}
var legacy1=DrawingPackages.Texture(rectangular);var legacy2=DrawingPackages.Texture(rectangular,true);Check(Int(legacy1,0)==0x58544e44&&Int(legacy1,4)==1&&Int(legacy2,4)==2&&Int(legacy2,24)==4,"DNTX v1/v2 remain unchanged");
Console.WriteLine($"Streamable texture authoring/parser: {checks} checks passed");
