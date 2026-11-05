using Dolphin.System;
using System.Text;
static void Check(bool value){if(!value)throw new Exception("Assertion failed");}
static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Accepted malformed input");}
static void Big(byte[] data,int at,int value){data[at]=(byte)(value>>24);data[at+1]=(byte)(value>>16);data[at+2]=(byte)(value>>8);data[at+3]=(byte)value;}
static byte[] Literal(byte[] value){int table=16+(value.Length+31)/32*4;var r=new byte[table+value.Length];Encoding.ASCII.GetBytes("Yay0").CopyTo(r,0);Big(r,4,value.Length);Big(r,8,table);Big(r,12,table);for(int i=16;i<table;i++)r[i]=255;value.CopyTo(r,table);return r;}
static byte[] Model(int size,int header,int count=1)
{
    var d=new byte[size];Encoding.ASCII.GetBytes("J3D1bmd1").CopyTo(d,0);Big(d,8,size);Big(d,12,2);
    int tex=header-32;Encoding.ASCII.GetBytes("INF1").CopyTo(d,32);Big(d,36,tex-32);
    Encoding.ASCII.GetBytes("TEX1").CopyTo(d,tex);Big(d,tex+4,size-tex);d[tex+9]=(byte)count;Big(d,tex+12,32);
    for(int i=0;i<count;i++){int h=header+i*32;d[h+3]=8;d[h+5]=8;d[h+24]=1;Big(d,h+28,count*32+i*32-i*32);}
    return d;
}

static byte[] WaveSystem(int samples)
{
    var d=new byte[320];Encoding.ASCII.GetBytes("WSYS").CopyTo(d,0);Big(d,4,d.Length);Big(d,16,32);
    Encoding.ASCII.GetBytes("WINF").CopyTo(d,32);Big(d,36,1);Big(d,40,64);
    Encoding.ASCII.GetBytes("ipl_0.aw").CopyTo(d,64);Big(d,176,2);Big(d,180,192);Big(d,184,232);
    int bytes=(samples+15)/16*9;
    for(int i=0;i<2;i++){int w=192+i*40;Big(d,w+4,0x46fa0000);Big(d,w+8,i*bytes);Big(d,w+12,bytes);Big(d,w+24,samples);Big(d,w+28,samples);}
    return d;
}
var payload=Literal(Encoding.ASCII.GetBytes("J3D1bmd1synthetic"));
Check(FirmwareCatalog.Decompress(payload).SequenceEqual(Encoding.ASCII.GetBytes("J3D1bmd1synthetic")));
for(int i=0;i<payload.Length;i++)Reject(()=>FirmwareCatalog.Decompress(payload.Take(i).ToArray()));
var invalid=(byte[])payload.Clone();invalid[16]=0;Reject(()=>FirmwareCatalog.Decompress(invalid));
invalid=(byte[])payload.Clone();invalid[11]=17;Reject(()=>FirmwareCatalog.Decompress(invalid));
var rom=new byte[FirmwareImage.ImageBytes];Encoding.ASCII.GetBytes("Nintendo GameCube IPL\r\nsynthetic").CopyTo(rom,0);
payload.CopyTo(rom,0x1000);payload.CopyTo(rom,0x2000);rom[0x1aff00]=123;
using(var firmware=FirmwareImage.Read(rom))
{
    Check(firmware.Encoding==FirmwareEncoding.Descrambled);Check(firmware.Source==FirmwareSource.Memory);
    var catalog=firmware.CreateCatalog();firmware.Dispose();Check(catalog.Count==2);
    Check(catalog[0].Kind==FirmwareResourceKind.UnsupportedModel);Check(catalog.Extract(0).Length==17);catalog.Dispose();
    try{catalog.Extract(0);throw new Exception();}catch(ObjectDisposedException){}
}
var original=(byte[])rom.Clone();FirmwareImage.Transform(rom);Check(rom[0x1aff00]==123);Check(rom.Take(256).SequenceEqual(original.Take(256)));
// Generated publisher/revision headers exercise both retail header families
// without including bytes taken from a firmware dump.
foreach(var suffix in new[]{"", "NTSC Revision 9.9", "PAL  Revision 9.9"})
{
    var retail=(byte[])original.Clone();Array.Clear(retail,0,256);
    Encoding.ASCII.GetBytes("(C) 2099 Nintendo. Synthetic.(C) 2099 ArtX Inc. Synthetic."+suffix).CopyTo(retail,0);
    using var retailImage=FirmwareImage.Read(retail);
    Check(retailImage.Encoding==FirmwareEncoding.Descrambled&&retailImage.Revision.EndsWith(suffix));
    FirmwareImage.Transform(retail);
    using var encryptedRetail=FirmwareImage.Read(retail);
    Check(encryptedRetail.Encoding==FirmwareEncoding.Scrambled);
}
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
path=Path.GetTempFileName();
try {
    global::System.IO.File.WriteAllBytes(path,original);
    using(var file=FirmwareImage.Load(path))Check(file.Source==FirmwareSource.ExplicitFile);
    using(var fallback=SystemFirmware.Open(path))Check(fallback.Source==FirmwareSource.ExplicitFile);
    using(var cancellation=new CancellationTokenSource()){
        cancellation.Cancel();
        try{SystemFirmware.Open(path,cancellation.Token);throw new Exception("Cancellation ignored");}
        catch(OperationCanceledException){}
    }
} finally {global::System.IO.File.Delete(path);}
// Structural J3D1 inspection, per-header TEX1 extraction, and both regional
// catalog families. All model/texture bytes are generated original fixtures.
var model=Model(0x16f60,5056,3);var info=FirmwareModelInfo.Inspect(model);
Check(info.SectionCount==2&&info.ReadTextures(model).Length==3);
var staleLength=(byte[])model.Clone();Big(staleLength,8,staleLength.Length-128);
Check(FirmwareModelInfo.Inspect(staleLength).ReadTextures(staleLength).Length==3);
var trailingModel=new byte[model.Length+32];model.CopyTo(trailingModel,0);
Reject(()=>FirmwareModelInfo.Inspect(trailingModel));
var excessive=Model(0x16f60,5056,5);
for(int i=0;i<5;i++){int h=5056+i*32;excessive[h+2]=4;excessive[h+3]=0;excessive[h+4]=4;excessive[h+5]=0;}
try{FirmwareModelInfo.Inspect(excessive).ReadTextures(excessive);throw new Exception("Texture work budget ignored");}
catch(InvalidDataException error){Check(error.Message.Contains("budget"));}
var bad=(byte[])model.Clone();Big(bad,36,int.MaxValue);Reject(()=>FirmwareModelInfo.Inspect(bad));
bad=(byte[])model.Clone();Big(bad,12,3);Reject(()=>FirmwareModelInfo.Inspect(bad));
bad=(byte[])model.Clone();Big(bad,5024+12,int.MaxValue);Reject(()=>FirmwareModelInfo.Inspect(bad).ReadTextures(bad));
foreach(int regionalCount in new[]{14,31})
{
    var image=new byte[FirmwareImage.ImageBytes];Encoding.ASCII.GetBytes("Nintendo GameCube IPL synthetic region").CopyTo(image,0);
    var tile=new byte[0x1020];tile[3]=64;tile[5]=128;tile[24]=1;Big(tile,28,32);
    var blocks=new[]{Model(0x6140,8448),tile,model,Model(0x64a0,11616)};
    int position=0x1000;
    for(int i=0;i<regionalCount;i++)
    {
        var compressed=Literal(i<4?blocks[i]:new byte[]{65,66,67,68});compressed.CopyTo(image,position);position+=compressed.Length+32;
    }
    using(var firmware=FirmwareImage.Read(image))using(var catalog=firmware.CreateCatalog())
    {
        Check(catalog.Count==regionalCount);
        Check(catalog.GetTexture(SystemTexture.Environment).HeaderOffset==8448);
        Check(catalog.GetTexture(SystemTexture.Tiles).Width==64);
        Check(catalog.GetTexture(SystemTexture.LogoTop).HeaderOffset==5056);
        Check(catalog.GetTexture(SystemTexture.LogoLeft).HeaderOffset==5088);
        Check(catalog.GetTexture(SystemTexture.LogoRight).HeaderOffset==5120);
        Check(catalog.GetTexture(SystemTexture.Wordmark).HeaderOffset==11616);
        var returned=catalog.GetTextures(0);returned[0]=null!;
        Check(catalog.GetTexture(SystemTexture.Environment).HeaderOffset==8448);
        var timer=global::System.Diagnostics.Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int lookup=0;lookup<100;lookup++)Check(catalog.GetTexture(SystemTexture.Environment).Width==8);
        timer.Stop();long lookupBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;Console.WriteLine($"{regionalCount}-record cached texture lookup: {timer.Elapsed.TotalMilliseconds:F3} ms / 100; {lookupBytes} managed bytes");
    }
}
var descriptor=WaveSystem(16);var archive=new byte[18];
// Generate the compact WSYS bank layout independently of any original samples.
var compactDescriptor=WaveSystem(16);Big(compactDescriptor,64+68,2);
Big(compactDescriptor,64+72,192);Big(compactDescriptor,64+76,232);
Big(compactDescriptor,64+112,0);
Check(FirmwareWaveSystem.Inspect(compactDescriptor).Length==2);
for(int i=1;i<9;i++){archive[i]=0x11;archive[i+9]=0xee;}
// Non-looping WSYS descriptors may use ignored sentinel loop coordinates.
Big(descriptor,192+20,-1);Big(descriptor,192+24,-1);
var stereo=FirmwareStereoAudio.Read(descriptor,archive,0,1);Array.Clear(archive);
var packet=stereo.ExtractAfc();Check(packet.Length==50&&packet[33]==0x11&&packet[42]==0xee&&stereo.SampleRate==32000);
var corrupt=(byte[])descriptor.Clone();Big(corrupt,232+8,0);Reject(()=>FirmwareStereoAudio.Read(corrupt,new byte[18],0,1));
corrupt=(byte[])descriptor.Clone();Big(corrupt,180,int.MaxValue);Reject(()=>FirmwareWaveSystem.Inspect(corrupt));
var filesystemImage=new byte[FirmwareImage.ImageBytes];Encoding.ASCII.GetBytes("Nintendo GameCube IPL synthetic filesystem").CopyTo(filesystemImage,0);
var literal=Literal(new byte[]{65,66,67,68});literal.CopyTo(filesystemImage,0x10000);literal.CopyTo(filesystemImage,0x11000);
int tableAt=0x4000;Big(filesystemImage,tableAt,0x01000000);Big(filesystemImage,tableAt+8,5);
Big(filesystemImage,tableAt+12,0x01000001);Big(filesystemImage,tableAt+20,3);
Big(filesystemImage,tableAt+24,7);Big(filesystemImage,tableAt+28,0x20000);Big(filesystemImage,tableAt+32,320);
Big(filesystemImage,tableAt+36,14);Big(filesystemImage,tableAt+40,0x22000);Big(filesystemImage,tableAt+44,195840);
Big(filesystemImage,tableAt+48,23);Big(filesystemImage,tableAt+52,0x10000);Big(filesystemImage,tableAt+56,literal.Length);
Encoding.ASCII.GetBytes("\0Banks\0ipl.ws\0ipl_0.aw\0cube.bin\0").CopyTo(filesystemImage,tableAt+60);
WaveSystem(174080).CopyTo(filesystemImage,0x20000);
using(var firmware=FirmwareImage.Read(filesystemImage))
using(var catalog=firmware.CreateCatalog())
{
    Check(catalog.Files!=null&&catalog.Files.Count==3&&catalog.Files.Find("Banks/ipl.ws")==0);
    Check(catalog.WaveSystemCount==1&&catalog.GetWaveSystem(0).Count==2);
    var startup=catalog.GetStartupAudio();firmware.Dispose();
    Check(startup.SampleRate==32000&&startup.SampleCount==174080&&startup.ExtractAfc().Length==195872);
    catalog.Dispose();try{startup.ExtractAfc();throw new Exception("Disposed audio usable");}catch(ObjectDisposedException){}
}
Array.Copy(filesystemImage,tableAt,filesystemImage,0x5000,92);
Reject(()=>FirmwareFileTable.Inspect(filesystemImage));
Console.WriteLine("System firmware synthetic tests passed");
