using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dolphin.Graphics;

/// <summary>Validated immutable TEV program with four or fewer named color parameters.
/// RGB expressions use fixed-point, saturating TEV arithmetic. Alpha remains the
/// vertex/material alpha multiplied by texture alpha when a texture is bound.</summary>
public sealed class GxEffect : IDisposable
{
    internal readonly int Token;
    private readonly string[] names;
    private bool disposed;
    public int StageCount {get;}
    public int UniformCount => names.Length;
    public bool RequiresTexture {get;}
    public bool RequiresSecondaryTexture {get;}
    private GxEffect(int token,byte[] package)
    {
        Token=token;StageCount=package[8];RequiresTexture=(package[12]&1)!=0;RequiresSecondaryTexture=(package[12]&2)!=0;
        names=new string[package[16]];
        for(int i=0;i<names.Length;i++){
            int at=32+i*36,n=0;while(package[at+n]!=0)n++;
            var chars=new char[n];for(int j=0;j<n;j++)chars[j]=(char)package[at+j];
            names[i]=new string(chars);
        }
    }
    public static GxEffect FromPackage(byte[] package)
    {
        if(package==null)throw new ArgumentNullException("package");
        int token=NativeGraphics.EffectCreate(package);
        if(token==0)throw new ArgumentException("Invalid DNEF program, budgets or active frame.");
        try{return new GxEffect(token,package);}
        catch{NativeGraphics.DrawRelease(token);throw;}
    }
    public static async Task<GxEffect> LoadAsync(string path,CancellationToken token=default)
    {
        var bytes=await Dolphin.IO.File.ReadAllBytesAsync(path,token);
        token.ThrowIfCancellationRequested();return FromPackage(bytes);
    }
    public string GetUniformName(int slot){Check();if(slot<0||slot>=names.Length)throw new ArgumentOutOfRangeException("slot");return names[slot];}
    public int GetUniformSlot(string name)
    {
        Check();if(name==null)throw new ArgumentNullException("name");
        for(int i=0;i<names.Length;i++)if(names[i]==name)return i;
        throw new ArgumentException("Unknown effect parameter.");
    }
    /// <summary>Update between frames. Cache the slot to avoid a name lookup each frame.</summary>
    public void SetColor(int slot,Color32 color)
    {Check();if(slot<0||slot>=names.Length)throw new ArgumentOutOfRangeException("slot");GraphicsDevice.Check(NativeGraphics.EffectColor(Token,slot,color.Packed),"Invalid parameter slot or active frame.");}
    public void SetColor(string name,Color32 color)=>SetColor(GetUniformSlot(name),color);
    internal void Check(){if(disposed)throw new ObjectDisposedException("GxEffect");}
    public void Dispose(){if(disposed)return;GraphicsDevice.Check(NativeGraphics.DrawRelease(Token),"Cannot dispose effect during a raw stream.");disposed=true;}

    public static GxEffect ColorRamp(Color32 dark,Color32 bright)
        =>FromPackage(Package(new[]{"Dark","Bright"},new[]{dark,bright},new byte[]{
            15,15,15,14,0,1,12,0, // C0 = Dark
            2,14,10,15,0,0,13,0  // prev = mix(C0, Bright, raster)
        }));
    public static GxEffect ToonBands(byte low,byte high,Color32 dark,Color32 middle,Color32 bright)
    {
        if(low>=high)throw new ArgumentException("Toon thresholds must be ordered.");
        return FromPackage(Package(new[]{"Thresholds","Dark","Middle","Bright"},new[]{new Color32(low,high,0),dark,middle,bright},new byte[]{
            15,15,15,14,0,1,16,0, // C0 = Thresholds.r
            10,2,12,15,8,1,0,0,  // C0 = raster.r > low ? 1 : 0
            15,15,15,14,0,2,13,0,// C1 = Dark
            4,14,2,15,0,2,14,0, // C1 = mix(Dark, Middle, C0)
            15,15,15,14,0,1,20,0,// C0 = Thresholds.g
            10,2,12,15,8,1,0,0,  // C0 = raster.r > high ? 1 : 0
            4,14,2,15,0,0,15,0  // prev = mix(C1, Bright, C0)
        }));
    }
    private static byte[] Package(string[] names,Color32[] colors,byte[] stages)
    {
        var p=new byte[176+stages.Length];p[0]=68;p[1]=78;p[2]=69;p[3]=70;p[4]=1;p[8]=(byte)(stages.Length/8);p[16]=(byte)names.Length;
        for(int i=0;i<names.Length;i++){
            for(int j=0;j<names[i].Length;j++)p[32+i*36+j]=(byte)names[i][j];
            int at=64+i*36;p[at]=colors[i].R;p[at+1]=colors[i].G;p[at+2]=colors[i].B;p[at+3]=colors[i].A;
        }
        Array.Copy(stages,0,p,176,stages.Length);return p;
    }
}
