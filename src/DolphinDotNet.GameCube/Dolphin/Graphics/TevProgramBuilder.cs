using System;
namespace Dolphin.Graphics;

public enum TevInput { Previous=0, Color0=2, Color1=4, Color2=6, Texture=8, Raster=10, One=12, Constant=14, Zero=15 }
public enum TevRegister { Previous, Color0, Color1, Color2 }
public enum TevOperation { Add=0, GreaterRed=8 }
public enum TevComponent { Rgb, Red, Green, Blue, Alpha }

/// <summary>Builds bounded DNEF v1 programs without native allocations. Each stage computes
/// clamp((1-C)*A + C*B + D), or D + (A.r &gt; B.r ? C : 0), into its RGB destination.
/// Alpha is always raster alpha times bound texture alpha.</summary>
public sealed class TevProgramBuilder
{
    public const int MaximumStages=16, MaximumColors=4;
    private readonly byte[] stages=new byte[MaximumStages*8];
    private readonly string[] names=new string[MaximumColors];
    private readonly Color32[] colors=new Color32[MaximumColors];
    private int count,uniforms,initialized;
    private bool texture;
    public int StageCount => count;
    public int ColorCount => uniforms;
    public int AddColor(string name,Color32 color)
    {
        if(name==null)throw new ArgumentNullException("name");
        if(name.Length<1||name.Length>31)throw new ArgumentException("Color names must contain 1 to 31 printable ASCII characters.");
        for(int i=0;i<name.Length;i++)if(name[i]<33||name[i]>126)throw new ArgumentException("Invalid color name.");
        for(int i=0;i<uniforms;i++)if(names[i]==name)throw new ArgumentException("Duplicate color name.");
        if(uniforms==MaximumColors)throw new InvalidOperationException("TEV supports at most four named colors.");
        names[uniforms]=name;colors[uniforms]=color;return uniforms++;
    }
    public void AddStage(TevInput a,TevInput b,TevInput c,TevInput d,
        TevRegister destination=TevRegister.Previous,TevOperation operation=TevOperation.Add,
        int colorSlot=-1,TevComponent component=TevComponent.Rgb)
    {
        if(count==MaximumStages)throw new InvalidOperationException("TEV supports at most sixteen stages.");
        if((int)destination<0||(int)destination>3||(operation!=TevOperation.Add&&operation!=TevOperation.GreaterRed)
            ||(int)component<0||(int)component>4||colorSlot < -1||colorSlot>=uniforms)
            throw new ArgumentException("Invalid TEV stage configuration.");
        Validate(a,colorSlot);Validate(b,colorSlot);Validate(c,colorSlot);Validate(d,colorSlot);
        int at=count*8;
        stages[at]=(byte)a;stages[at+1]=(byte)b;stages[at+2]=(byte)c;stages[at+3]=(byte)d;
        stages[at+4]=(byte)operation;stages[at+5]=(byte)destination;
        stages[at+6]=(byte)(colorSlot<0?0:component==TevComponent.Rgb?12+colorSlot:12+(int)component*4+colorSlot);
        texture=texture||a==TevInput.Texture||b==TevInput.Texture||c==TevInput.Texture||d==TevInput.Texture;
        initialized|=1<<(int)destination;count++;
    }
    private void Validate(TevInput input,int slot)
    {
        int v=(int)input;
        if(v==0||v==2||v==4||v==6){if((initialized&(1<<(v/2)))==0)throw new ArgumentException("Read of an uninitialized TEV register.");}
        else if(input==TevInput.Constant){if(slot<0)throw new ArgumentException("Constant input needs a color slot.");}
        else if(input!=TevInput.Texture&&input!=TevInput.Raster&&input!=TevInput.One&&input!=TevInput.Zero)
            throw new ArgumentException("Invalid TEV input.");
    }
    /// <summary>Returns an independent package. Native validation runs again when creating an effect.</summary>
    public byte[] BuildPackage()
    {
        if(count==0||stages[(count-1)*8+5]!=0)throw new InvalidOperationException("The final stage must write Previous.");
        var p=new byte[176+count*8];p[0]=68;p[1]=78;p[2]=69;p[3]=70;p[4]=1;
        p[8]=(byte)count;p[12]=(byte)(texture?1:0);p[16]=(byte)uniforms;
        for(int i=0;i<uniforms;i++){
            for(int j=0;j<names[i].Length;j++)p[32+i*36+j]=(byte)names[i][j];
            int at=64+i*36;p[at]=colors[i].R;p[at+1]=colors[i].G;p[at+2]=colors[i].B;p[at+3]=colors[i].A;
        }
        Array.Copy(stages,0,p,176,count*8);return p;
    }
    public GxEffect CreateEffect()=>GxEffect.FromPackage(BuildPackage());
}
