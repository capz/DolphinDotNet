using Dolphin.System;
using System.Text;
static void Check(bool value){if(!value)throw new Exception("Assertion failed");}
static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Accepted malformed input");}
static byte[] Literal(byte[] value){var r=new byte[20+value.Length];Encoding.ASCII.GetBytes("Yay0").CopyTo(r,0);r[7]=(byte)value.Length;r[11]=20;r[15]=20;for(int i=16;i<20;i++)r[i]=255;value.CopyTo(r,20);return r;}
var payload=Literal(Encoding.ASCII.GetBytes("J3D1bmd1synthetic"));
Check(FirmwareCatalog.Decompress(payload).SequenceEqual(Encoding.ASCII.GetBytes("J3D1bmd1synthetic")));
for(int i=0;i<payload.Length;i++)Reject(()=>FirmwareCatalog.Decompress(payload.Take(i).ToArray()));
var invalid=(byte[])payload.Clone();invalid[16]=0;Reject(()=>FirmwareCatalog.Decompress(invalid));
invalid=(byte[])payload.Clone();invalid[11]=17;Reject(()=>FirmwareCatalog.Decompress(invalid));
var rom=new byte[FirmwareImage.ImageBytes];Encoding.ASCII.GetBytes("Nintendo GameCube IPL\r\nsynthetic").CopyTo(rom,0);
payload.CopyTo(rom,0x1000);payload.CopyTo(rom,0x2000);rom[0x1aff00]=123;
using(var firmware=FirmwareImage.Read(rom))
{
    Check(firmware.Encoding==FirmwareEncoding.Descrambled);
    var catalog=firmware.CreateCatalog();firmware.Dispose();Check(catalog.Count==2);
    Check(catalog[0].Kind==FirmwareResourceKind.UnsupportedModel);Check(catalog.Extract(0).Length==17);catalog.Dispose();
    try{catalog.Extract(0);throw new Exception();}catch(ObjectDisposedException){}
}
var original=(byte[])rom.Clone();FirmwareImage.Transform(rom);Check(rom[0x1aff00]==123);Check(rom.Take(256).SequenceEqual(original.Take(256)));
using(var scrambled=FirmwareImage.Read(rom)){Check(scrambled.Encoding==FirmwareEncoding.Scrambled);Check(scrambled.CreateCatalog().Count==2);}
FirmwareImage.Transform(rom);Check(rom.SequenceEqual(original));
Reject(()=>FirmwareImage.Read(new byte[12]));Reject(()=>FirmwareImage.Read(new byte[FirmwareImage.ImageBytes]));
Array.Clear(rom,0x100,0x1afe00);Reject(()=>FirmwareImage.Read(rom));
using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{FirmwareImage.Transform(original,cancel.Token);throw new Exception();}catch(OperationCanceledException){}}
// Independent reference vector: Segher's C implementation applied to 32 zero bytes.
var zeros=new byte[FirmwareImage.ImageBytes];FirmwareImage.Transform(zeros);
Check(Convert.ToHexString(zeros.AsSpan(256,32))=="897E477FF4423FE2A14432A63013BCD1DC12E0CCA565368CDF2ABA9AEF2883AD");
var texture=new byte[160];int header=32;texture[header+3]=8;texture[header+5]=8;texture[header+24]=1;texture[header+31]=64;
for(int i=96;i<128;i++)texture[i]=255;
var bti=FirmwareTextures.Extract(texture,header);Check(bti.Length==64);Check(bti[31]==32);
Check(Dolphin.Formats.BtiTexture.Read(bti).Rgba.All(x=>x==255));
texture[header+28]=255;Reject(()=>FirmwareTextures.Extract(texture,header));
var backref=new byte[]{89,97,121,48,0,0,0,4,0,0,0,20,0,0,0,22,128,0,0,0,16,0,65};
Check(FirmwareCatalog.Decompress(backref).SequenceEqual(new byte[]{65,65,65,65}));
backref[7]=3;Reject(()=>FirmwareCatalog.Decompress(backref));
var pair=new byte[96];pair[3]=8;pair[5]=8;pair[24]=1;pair[31]=64;
pair[35]=8;pair[37]=8;pair[56]=1;pair[63]=32;
for(int i=64;i<96;i++)pair[i]=123;
Check(FirmwareTextures.Extract(pair,0).SequenceEqual(FirmwareTextures.Extract(pair,32)));
var path=Path.GetTempFileName();
try { global::System.IO.File.WriteAllBytes(path,new byte[20]);Reject(()=>FirmwareImage.Load(path)); }
finally { global::System.IO.File.Delete(path); }
Console.WriteLine("System firmware synthetic tests passed");
