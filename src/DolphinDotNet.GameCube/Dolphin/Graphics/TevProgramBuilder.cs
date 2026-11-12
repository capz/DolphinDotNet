using System;
namespace Dolphin.Graphics;

public enum TevInput { Previous=0, PreviousAlpha=1, Color0=2, Color0Alpha=3, Color1=4, Color1Alpha=5, Color2=6, Color2Alpha=7, Texture=8, TextureAlpha=9, Raster=10, RasterAlpha=11, One=12, Half=13, Constant=14, Zero=15, SecondaryTexture=16, SecondaryTextureAlpha=17 }
public enum TevRegister { Previous, Color0, Color1, Color2 }
public enum TevOperation { Add=0, Subtract=1, GreaterRed=8 }
public enum TevComponent { Rgb, Red, Green, Blue, Alpha }
public enum TevAlphaInput { Previous, Color0, Color1, Color2, Texture, Raster, Constant, Zero, One, SecondaryTexture }
public enum TevAlphaOperation { Add, Subtract }
public enum TevBias { Zero, AddHalf, SubtractHalf }
public enum TevScale { One, Two, Four, Half }
public enum AlphaComparison { Never, Less, Equal, LessOrEqual, Greater, NotEqual, GreaterOrEqual, Always }
public enum AlphaCompareOperation { And, Or, Xor, Xnor }
/// <summary>Color selects destination color in the source factor slot and
/// source color in the destination factor slot, matching GX's factor encoding.</summary>
public enum TevBlendFactor { Zero, One, Color, InverseColor, SourceAlpha, InverseSourceAlpha, DestinationAlpha, InverseDestinationAlpha }

/// <summary>Builds bounded DNEF v1-v5 programs without native allocations. RGB computes
/// D +/- ((1-C)*A + C*B) with optional bias, scale and clamp, or a GreaterRed comparison.
/// Alpha defaults to raster alpha times bound texture alpha. Explicit alpha
/// programs and two-reference alpha tests opt into DNEF v4. Extended RGB
/// selectors/arithmetic or custom blend factors opt into DNEF v5.</summary>
public sealed class TevProgramBuilder
{
    public const int MaximumStages=16, MaximumColors=4;
    private readonly byte[] stages=new byte[MaximumStages*8];
    private readonly string[] names=new string[MaximumColors];
    private readonly Color32[] colors=new Color32[MaximumColors];
    private int count,uniforms,initialized;
    private int textures;
    private int indirectStrength;
    private readonly byte[] alpha=new byte[MaximumStages*8];
    private readonly int[] alphaTextures=new int[MaximumStages];
    private int alphaMask;
    private bool extendedRgb;
    private readonly byte[] alphaCompare=new byte[5];
    private bool compareEnabled;
    private bool blendEnabled;
    private byte blendSource,blendDestination;
    public int StageCount => count;
    public int ColorCount => uniforms;
    /// <summary>Use the second texture only as a GX indirect UV warp map.
    /// The first texture is sampled for all color stages; stages may select either
    /// UV matrix without any CPU texture or vertex updates.</summary>
    public void EnableIndirectWarp(byte strength=76)
    {
        if(strength==0)throw new ArgumentOutOfRangeException("strength");
        if((textures&2)!=0)throw new InvalidOperationException("Secondary RGB sampling and indirect warp are mutually exclusive.");
        for(int i=0;i<count;i++)if((alphaTextures[i]&2)!=0)throw new InvalidOperationException("Secondary alpha sampling and indirect warp are mutually exclusive.");
        indirectStrength=strength;
        textures|=2;
    }
    /// <summary>Configure the alpha equation for an existing RGB stage. When
    /// using explicit alpha, configure every stage; RGB and alpha share a texture
    /// sample but have independent registers, constants, bias, scale and clamp.
    /// Read-before-write validation runs when building the complete program.</summary>
    public void SetAlphaStage(int stage,TevAlphaInput a,TevAlphaInput b,TevAlphaInput c,TevAlphaInput d,
        TevRegister destination=TevRegister.Previous,TevAlphaOperation operation=TevAlphaOperation.Add,
        TevBias bias=TevBias.Zero,TevScale scale=TevScale.One,bool clamp=true,
        int colorSlot=-1,TevComponent component=TevComponent.Alpha)
    {
        if(stage<0||stage>=count)throw new ArgumentOutOfRangeException("stage");
        if((int)destination<0||(int)destination>3||(int)operation<0||(int)operation>1||
           (int)bias<0||(int)bias>2||(int)scale<0||(int)scale>3||colorSlot < -1||colorSlot>=uniforms||
           (int)component<1||(int)component>4)throw new ArgumentException("Invalid alpha stage configuration.");
        var inputs=new[]{a,b,c,d};int sampled=0;bool one=false,constant=false;
        for(int i=0;i<4;i++){
            int v=(int)inputs[i];if(v<0||v>9)throw new ArgumentException("Invalid alpha input.");
            sampled|=inputs[i]==TevAlphaInput.Texture?1:inputs[i]==TevAlphaInput.SecondaryTexture?2:0;
            one|=inputs[i]==TevAlphaInput.One;constant|=inputs[i]==TevAlphaInput.Constant;
        }
        if(constant&&(colorSlot<0||one))throw new ArgumentException("Alpha constants need a color slot and cannot share a stage with One.");
        int at=stage*8,combined=sampled|StageTextures(stage);
        if(combined==3||(indirectStrength!=0&&(sampled&2)!=0))throw new ArgumentException("RGB and alpha must share one texture sample per stage.");
        for(int i=0;i<4;i++)alpha[at+i]=(byte)(inputs[i]==TevAlphaInput.SecondaryTexture?4:inputs[i]==TevAlphaInput.One?6:(int)inputs[i]);
        alpha[at+4]=(byte)operation;
        alpha[at+5]=(byte)(128|(int)destination|((int)bias<<2)|((int)scale<<4)|(clamp?64:0));
        alpha[at+6]=(byte)(constant?12+(int)component*4+colorSlot:0);
        alphaTextures[stage]=sampled;alphaMask|=1<<stage;
    }
    /// <summary>Override Material.AlphaCutoff with GX's two-reference test.
    /// References are exact 8-bit GX values; for example &lt;=33 OR &gt;=235.</summary>
    public void SetAlphaCompare(AlphaComparison comparison0,byte reference0,AlphaCompareOperation operation,
        AlphaComparison comparison1,byte reference1)
    {
        if((int)comparison0<0||(int)comparison0>7||(int)comparison1<0||(int)comparison1>7||(int)operation<0||(int)operation>3)
            throw new ArgumentException("Invalid alpha comparison.");
        alphaCompare[0]=(byte)comparison0;alphaCompare[1]=reference0;alphaCompare[2]=(byte)operation;
        alphaCompare[3]=(byte)comparison1;alphaCompare[4]=reference1;compareEnabled=true;
    }
    /// <summary>Override GX blend factors. Keep Material.Blend set to Alpha
    /// for transparent sorting. Later ordinary draws restore their factors.</summary>
    public void SetBlendFactors(TevBlendFactor source,TevBlendFactor destination)
    {
        if((int)source<0||(int)source>7||(int)destination<0||(int)destination>7)
            throw new ArgumentException("Invalid GX blend factor.");
        blendSource=(byte)source;blendDestination=(byte)destination;blendEnabled=true;
    }
    private int StageTextures(int stage)
    {
        int at=stage*8;
        return stages[at]==8||stages[at]==9||stages[at+1]==8||stages[at+1]==9||stages[at+2]==8||stages[at+2]==9||stages[at+3]==8||stages[at+3]==9?(stages[at+7]==1?2:1):0;
    }
    public int AddColor(string name,Color32 color)
    {
        if(name==null)throw new ArgumentNullException("name");
        if(name.Length<1||name.Length>31)throw new ArgumentException("Color names must contain 1 to 31 printable ASCII characters.");
        for(int i=0;i<name.Length;i++)if(name[i]<33||name[i]>126)throw new ArgumentException("Invalid color name.");
        for(int i=0;i<uniforms;i++)if(names[i]==name)throw new ArgumentException("Duplicate color name.");
        if(uniforms==MaximumColors)throw new InvalidOperationException("TEV supports at most four named colors.");
        names[uniforms]=name;colors[uniforms]=color;return uniforms++;
    }
    // Retain the original CLR signature for already-compiled engine packages.
    public void AddStage(TevInput a,TevInput b,TevInput c,TevInput d,
        TevRegister destination,TevOperation operation,int colorSlot,
        TevComponent component,bool alternateUv)
        =>AddStage(a,b,c,d,destination,operation,colorSlot,component,alternateUv,TevBias.Zero,TevScale.One,true);
    public void AddStage(TevInput a,TevInput b,TevInput c,TevInput d,
        TevRegister destination=TevRegister.Previous,TevOperation operation=TevOperation.Add,
        int colorSlot=-1,TevComponent component=TevComponent.Rgb,bool alternateUv=false,
        TevBias bias=TevBias.Zero,TevScale scale=TevScale.One,bool clamp=true)
    {
        if(count==MaximumStages)throw new InvalidOperationException("TEV supports at most sixteen stages.");
        if((int)destination<0||(int)destination>3||(operation!=TevOperation.Add&&operation!=TevOperation.Subtract&&operation!=TevOperation.GreaterRed)
            ||(int)component<0||(int)component>4||colorSlot < -1||colorSlot>=uniforms||
            (int)bias<0||(int)bias>2||(int)scale<0||(int)scale>3||
            (operation==TevOperation.GreaterRed&&(bias!=TevBias.Zero||scale!=TevScale.One||!clamp)))
            throw new ArgumentException("Invalid TEV stage configuration.");
        Validate(a,colorSlot);Validate(b,colorSlot);Validate(c,colorSlot);Validate(d,colorSlot);
        bool primary=Primary(a)||Primary(b)||Primary(c)||Primary(d);
        bool secondary=Secondary(a)||Secondary(b)||Secondary(c)||Secondary(d);
        if(primary&&secondary)throw new ArgumentException("A TEV stage can sample only one texture; use an intermediate register.");
        if(indirectStrength!=0&&secondary)throw new ArgumentException("An indirect warp map cannot also be sampled as an RGB texture.");
        if(alternateUv&&(indirectStrength==0||secondary||!primary))throw new ArgumentException("Alternate UVs require a primary-texture stage in an indirect-warp effect.");
        int at=count*8;
        stages[at]=Packed(a);stages[at+1]=Packed(b);stages[at+2]=Packed(c);stages[at+3]=Packed(d);
        stages[at+7]=(byte)(alternateUv?2:secondary?1:0);
        bool extended=operation==TevOperation.Subtract||bias!=TevBias.Zero||scale!=TevScale.One||!clamp||NewInput(a)||NewInput(b)||NewInput(c)||NewInput(d);
        stages[at+4]=(byte)operation;stages[at+5]=(byte)(extended?128|(clamp?64:0)|((int)scale<<4)|((int)bias<<2)|(int)destination:(int)destination);
        stages[at+6]=(byte)(colorSlot<0?0:component==TevComponent.Rgb?12+colorSlot:12+(int)component*4+colorSlot);
        textures|=(primary?1:0)|(secondary?2:0);
        extendedRgb|=extended;initialized|=1<<(int)destination;count++;
    }
    private static bool Primary(TevInput v)=>v==TevInput.Texture||v==TevInput.TextureAlpha;
    private static bool Secondary(TevInput v)=>v==TevInput.SecondaryTexture||v==TevInput.SecondaryTextureAlpha;
    private static byte Packed(TevInput v)=>(byte)(v==TevInput.SecondaryTexture?8:v==TevInput.SecondaryTextureAlpha?9:(int)v);
    private static bool NewInput(TevInput v)=>((int)v>=1&&(int)v<=13&&((int)v&1)!=0)||v==TevInput.SecondaryTextureAlpha;
    private void Validate(TevInput input,int slot)
    {
        int v=(int)input;
        if(v==0||v==2||v==4||v==6){if((initialized&(1<<(v/2)))==0)throw new ArgumentException("Read of an uninitialized TEV register.");}
        else if(v==1||v==3||v==5||v==7){} // Alpha dataflow is validated after all stages are configured.
        else if(input==TevInput.Constant){if(slot<0)throw new ArgumentException("Constant input needs a color slot.");}
        else if(NewInput(input)){}
        else if(input!=TevInput.Texture&&input!=TevInput.SecondaryTexture&&input!=TevInput.Raster&&input!=TevInput.One&&input!=TevInput.Zero)
            throw new ArgumentException("Invalid TEV input.");
    }
    /// <summary>Returns an independent package. Native validation runs again when creating an effect.</summary>
    public byte[] BuildPackage()
    {
        if(count==0||(stages[(count-1)*8+5]&3)!=0)throw new InvalidOperationException("The final stage must write Previous.");
        bool explicitAlpha=alphaMask!=0,v4=explicitAlpha||compareEnabled||extendedRgb||blendEnabled;int required=textures;
        int alphaReady=0;
        for(int i=0;i<count;i++){
            for(int j=0;j<4;j++){int v=stages[i*8+j];if(v<=7&&(v&1)!=0&&(alphaReady&(1<<(v/2)))==0)throw new InvalidOperationException("Read of an uninitialized alpha register in RGB.");}
            alphaReady|=(alphaMask&(1<<i))!=0?1<<(alpha[i*8+5]&3):1;
        }
        if(explicitAlpha){
            if(alphaMask!=(1<<count)-1)throw new InvalidOperationException("Configure alpha for every stage in an explicit alpha program.");
            int initializedAlpha=0;
            for(int i=0;i<count;i++){
                for(int j=0;j<4;j++){int input=alpha[i*8+j];if(input<=3&&(initializedAlpha&(1<<input))==0)throw new InvalidOperationException("Read of an uninitialized alpha register.");}
                initializedAlpha|=1<<(alpha[i*8+5]&3);required|=alphaTextures[i];
            }
            if((alpha[(count-1)*8+5]&3)!=0)throw new InvalidOperationException("Final alpha must write Previous.");
        }
        bool dual=(required&2)!=0,epilogue=dual&&!explicitAlpha;
        if(indirectStrength!=0&&required!=3)throw new InvalidOperationException("An indirect warp needs a primary texture sample.");
        if(epilogue&&count==MaximumStages)throw new InvalidOperationException("Legacy two-texture programs reserve one stage for base opacity.");
        int stride=v4?16:8;
        var p=new byte[176+(count+(epilogue?1:0))*stride];p[0]=68;p[1]=78;p[2]=69;p[3]=70;p[4]=(byte)(extendedRgb||blendEnabled?5:v4?4:indirectStrength!=0?3:dual?2:1);
        p[8]=(byte)(count+(epilogue?1:0));p[12]=(byte)required;p[16]=(byte)uniforms;p[20]=(byte)indirectStrength;
        if(compareEnabled){p[21]=1;Array.Copy(alphaCompare,0,p,24,5);}
        if(blendEnabled){p[22]=1;p[29]=blendSource;p[30]=blendDestination;}
        for(int i=0;i<uniforms;i++){
            for(int j=0;j<names[i].Length;j++)p[32+i*36+j]=(byte)names[i][j];
            int at=64+i*36;p[at]=colors[i].R;p[at+1]=colors[i].G;p[at+2]=colors[i].B;p[at+3]=colors[i].A;
        }
        for(int i=0;i<count;i++){
            int at=176+i*stride;Array.Copy(stages,i*8,p,at,8);
            if(v4){Array.Copy(alpha,i*8,p,at+8,8);if((alphaTextures[i]&2)!=0)p[at+7]=1;}
        }
        if(epilogue){int at=176+count*stride;p[at]=15;p[at+1]=15;p[at+2]=15;/* D=Previous, TEX0 alpha */}
        return p;
    }
    public GxEffect CreateEffect()=>GxEffect.FromPackage(BuildPackage());
}
