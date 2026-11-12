using Dolphin.Graphics;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
// Independent fixed-point TEV interpreter. It consumes the serialized package,
// including register writes and channel selectors, rather than calling preset code.
// Integer TEV model follows Dolphin's hardware shader generator: A/B/C are
// bytes, D is signed, scale is applied before interpolation rounding, and
// unclamped registers saturate to signed 11-bit before low-byte reads/output.
static int Arithmetic(int a,int b,int c,int d,int op,int control)
{
    a&=255;b&=255;c&=255;
    int scale=(control>>4)&3,left=scale==1?1:scale==2?2:0;
    int mix=((a<<8)+(b-a)*(c+(c>>7)))<<left;
    mix=(mix+(scale==3?0:op==1?127:128))>>8;
    int bias=((control>>2)&3) switch{1=>128,2=>-128,_=>0};
    int value=((d+bias)<<left)+(op==1?-mix:mix);
    if(scale==3)value>>=1;
    return (control&64)!=0?Math.Clamp(value,0,255):Math.Clamp(value,-1024,1023);
}
static int[] Evaluate(byte[] p,int raster,int tex,int secondary=0)
{
    int[][] regs=[new int[3],new int[3],new int[3],new int[3]];var alpha=new int[4];
    for(int i=0;i<p[8];i++){
        int at=176+i*(p[4]>=4?16:8),k=p[at+6],control=p[at+5];
        int sample=p[at+7]==1?secondary:tex;
        int Read(int input,int channel)=>input switch {
            0 or 2 or 4 or 6=>regs[input/2][channel],1 or 3 or 5 or 7=>alpha[input/2],
            8 or 9=>sample,10 or 11=>raster,12=>255,13=>128,15=>0,
            14=>p[64+(k-12)%4*36+(k<16?channel:(k-16)/4)],_=>throw new Exception("Invalid selector")};
        int[] result=new int[3];
        for(int c=0;c<3;c++){
            int a=Read(p[at],c),b=Read(p[at+1],c),t=Read(p[at+2],c),d=Read(p[at+3],c);
            result[c]=p[at+4]==8?Math.Clamp(d+((a&255)>(b&255)?t&255:0),0,255):Arithmetic(a,b,t,d,p[at+4],(control&128)!=0?control:64|control);
        }
        regs[control&3]=result;
        if(p[4]>=4&&(p[at+13]&128)!=0){
            int aa=at+8,ka=p[aa+6];
            int ReadAlpha(int input)=>input switch{0 or 1 or 2 or 3=>alpha[input],4=>sample,5=>raster,6=>ka==0?255:p[64+(ka-16)%4*36+(ka-16)/4],7=>0,_=>throw new Exception()};
            alpha[p[aa+5]&3]=Arithmetic(ReadAlpha(p[aa]),ReadAlpha(p[aa+1]),ReadAlpha(p[aa+2]),ReadAlpha(p[aa+3]),p[aa+4],p[aa+5]);
        }else alpha[0]=Arithmetic(0,sample,raster,0,0,64);
    }
    return regs[0].Select(v=>v&255).ToArray();
}

var toon=TevMaterialPresets.Toon(new ToonMaterialOptions{
    Dark=new Color32(20,30,40),Middle=new Color32(80,90,100),Bright=new Color32(210,220,230)});
Check(toon.StageCount==8&&toon.RequiresTexture&&toon.Lighting==LightingMode.Diffuse,"Toon pipeline");
for(int r=0;r<256;r++)for(int t=0;t<256;t++){
    int[] palette=r>165?[210,220,230]:r>65?[80,90,100]:[20,30,40];
    var actual=Evaluate(toon.GetPackage(),r,t);
    for(int c=0;c<3;c++)Check(Math.Abs(actual[c]-palette[c]*t/255)<=1,"Toon band or albedo mismatch");
}
var untextured=TevMaterialPresets.Toon(new ToonMaterialOptions{Textured=false});
Check(untextured.StageCount==7&&!untextured.RequiresTexture,"Untextured toon");
Reject<ArgumentException>(()=>TevMaterialPresets.Toon(new ToonMaterialOptions{Low=200,High=100}));
foreach(byte strength in new byte[]{0,64,128,255}){
    var metal=TevMaterialPresets.MetallicReflection(new MetallicMaterialOptions{
        BaseColor=Color32.White,ReflectionTint=Color32.White,ReflectionStrength=strength});
    Check(metal.StageCount==3&&metal.TextureCoordinates==TextureCoordinateMode.ViewNormal,"Metal pipeline");
    var glow=TevMaterialPresets.Emissive(new EmissiveMaterialOptions{
        BaseColor=Color32.White,EmissionColor=Color32.White,EmissionStrength=strength});
    Check(glow.StageCount==3&&glow.TextureCoordinates==TextureCoordinateMode.Uv0,"Emission pipeline");
    for(int r=0;r<256;r+=17)for(int t=0;t<256;t++){
        var reflected=Evaluate(metal.GetPackage(),r,t)[0];
        Check(Math.Abs(reflected-(r+(t-r)*strength/255.0))<=2,"Reflection blend");
        var emitted=Evaluate(glow.GetPackage(),r,t)[0];
        Check(Math.Abs(emitted-Math.Min(255,r+t*strength/255.0))<=2,"Emission independence/saturation");
    }
}
var definition=TevMaterialPresets.Emissive();var copy=definition.GetPackage();copy[8]=0;
Check(definition.StageCount==3,"Definition leaked mutable package");
Reject<ArgumentException>(()=>definition.Create()); // no native calls on missing binding
var b=new TevProgramBuilder();
Reject<InvalidOperationException>(()=>b.BuildPackage());
Reject<ArgumentException>(()=>b.AddStage(TevInput.Previous,TevInput.Zero,TevInput.Zero,TevInput.Zero));
Reject<ArgumentException>(()=>b.AddStage((TevInput)18,TevInput.Zero,TevInput.Zero,TevInput.Zero));
Reject<ArgumentException>(()=>b.AddStage(TevInput.Constant,TevInput.Zero,TevInput.Zero,TevInput.Zero));
Check(b.StageCount==0,"Failed stages changed builder");
Reject<ArgumentException>(()=>b.AddColor("not a name",Color32.White));
Reject<ArgumentException>(()=>b.AddColor(new string('x',32),Color32.White));
for(int i=0;i<4;i++)b.AddColor("K"+i,Color32.White);
Reject<ArgumentException>(()=>b.AddColor("K0",Color32.White));
Reject<InvalidOperationException>(()=>b.AddColor("Fifth",Color32.White));
for(int i=0;i<16;i++)b.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
Check(b.BuildPackage().Length==304,"Stage budget");
Reject<InvalidOperationException>(()=>b.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Zero));
var unfinished=new TevProgramBuilder();unfinished.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.One,TevRegister.Color0);
Reject<InvalidOperationException>(()=>unfinished.BuildPackage());
Console.WriteLine("TEV materials: exhaustive toon, reflection/emission equations, package isolation and budgets passed.");
foreach(byte strength in new byte[]{0,64,128,255}){
    var metal=TevMaterialPresets.MetallicReflection(new MetallicMaterialOptions{SeparateBaseTexture=true,BaseColor=Color32.White,ReflectionTint=Color32.White,ReflectionStrength=strength,ReflectionCoordinates=TextureCoordinateMode.WorldReflection});
    var glow=TevMaterialPresets.Emissive(new EmissiveMaterialOptions{SeparateBaseTexture=true,BaseColor=Color32.White,EmissionColor=Color32.White,EmissionStrength=strength});
    Check(metal.StageCount==5&&metal.RequiresTexture&&metal.RequiresSecondaryTexture,"Dual texture pipeline");
    foreach(int r in new[]{0,128,255})foreach(int t in new[]{0,64,255})foreach(int aux in new[]{0,100,255}){
        double lit=r*t/255.0;
        Check(Math.Abs(Evaluate(metal.GetPackage(),r,t,aux)[0]-(lit+(aux-lit)*strength/255.0))<=3,"Independent reflection texture");
        Check(Math.Abs(Evaluate(glow.GetPackage(),r,t,aux)[0]-Math.Min(255,lit+aux*strength/255.0))<=3,"Independent emission texture");
    }
    var p=metal.GetPackage();Check(p[4]==2&&p[12]==3&&p[^1]==0,"DNEF v2 opacity epilogue");
}
var mixed=new TevProgramBuilder();
Reject<ArgumentException>(()=>mixed.AddStage(TevInput.Texture,TevInput.SecondaryTexture,TevInput.One,TevInput.Zero));
Check(mixed.StageCount==0,"Mixed texture failure mutated builder");
mixed.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.SecondaryTexture);
for(int i=1;i<16;i++)mixed.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Previous);
Reject<InvalidOperationException>(()=>mixed.BuildPackage());
Console.WriteLine("Dual-texture TEV arithmetic and opacity-stage budgets passed.");

var waterWarp=TevMaterialPresets.WaterWarp(new Color32(198,234,229),new Color32(230,246,240),76);
var waterBytes=waterWarp.GetPackage();
Check(waterWarp.StageCount==4&&waterWarp.RequiresTexture&&waterWarp.RequiresSecondaryTexture,"Warp requires two textures and a final opacity stage");
Check(waterBytes[4]==3&&waterBytes[12]==3&&waterBytes[20]==76&&waterBytes[23]==0&&waterBytes[24]==0,"DNEF v3 indirect strength and reserved header");
Check(waterBytes[176+8+7]==0&&waterBytes[176+16+7]==2&&waterBytes[176+24+7]==0,"Second foam layer uses alternate UV, never auxiliary RGB");
var noWarp=new TevProgramBuilder();
Reject<ArgumentException>(()=>noWarp.AddStage(TevInput.Zero,TevInput.Texture,TevInput.One,TevInput.Zero,alternateUv:true));
Reject<ArgumentOutOfRangeException>(()=>noWarp.EnableIndirectWarp(0));
noWarp.EnableIndirectWarp(76);
Reject<ArgumentException>(()=>noWarp.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.SecondaryTexture));
Reject<ArgumentException>(()=>noWarp.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster,alternateUv:true));
var rgbFirst=new TevProgramBuilder();rgbFirst.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.SecondaryTexture);
Reject<InvalidOperationException>(()=>rgbFirst.EnableIndirectWarp());
Console.WriteLine("DNEF v3 indirect texture authoring and two-UV water preset passed.");

static int EvaluateAlpha(byte[] p,int raster,int tex0,int tex1)
{
    var regs=new int[4];
    for(int i=0;i<p[8];i++){
        int at=176+i*16,alpha=at+8;
        if((p[alpha+5]&128)==0){regs[0]=tex0*raster/255;continue;}
        int Read(int v)=>v switch{0 or 1 or 2 or 3=>regs[v],4=>p[at+7]==1?tex1:tex0,5=>raster,6=>p[alpha+6]==0?255:p[64+(p[alpha+6]-16)%4*36+(p[alpha+6]-16)/4],7=>0,_=>throw new Exception()};
        int a=Read(p[alpha]),b=Read(p[alpha+1]),c=Read(p[alpha+2]),d=Read(p[alpha+3]);
        regs[p[alpha+5]&3]=Arithmetic(a,b,c,d,p[alpha+4],p[alpha+5]);
    }
    return regs[0];
}
var glints=TevMaterialPresets.DualTextureGlints(new Color32(102,150,176),new Color32(215,248,244));
var glintBytes=glints.GetPackage();
Check(glintBytes[4]==4&&glints.StageCount==3&&glintBytes.Length==224&&glints.RequiresTexture&&glints.RequiresSecondaryTexture,"Explicit alpha uses three shared RGB/alpha stages without an opacity epilogue");
Check(glintBytes[21]==1&&glintBytes[24]==3&&glintBytes[25]==33&&glintBytes[26]==1&&glintBytes[27]==6&&glintBytes[28]==235,"Exact GX outside-band alpha comparison");
for(int a=0;a<256;a++)for(int aux=0;aux<256;aux++){
    int actual=EvaluateAlpha(glintBytes,255,a,aux);
    Check(actual==(a+aux)/2,"Independent samples both contribute to alpha");
    Check((actual<=glintBytes[25]||actual>=glintBytes[28])==(actual<=33||actual>=235),"Two-sided cutout");
}
foreach(int opacity in new[]{0,17,128,206,255})foreach(int a in new[]{0,31,128,255})foreach(int aux in new[]{0,64,235,255})
    Check(Math.Abs(EvaluateAlpha(glintBytes,opacity,a,aux)-(a+aux)*opacity/510.0)<=2,"Raster opacity retained");
Reject<ArgumentException>(()=>TevMaterialPresets.DualTextureGlints(Color32.White,Color32.White,235,33));
var alphaBuilder=new TevProgramBuilder();alphaBuilder.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
Reject<ArgumentException>(()=>alphaBuilder.SetAlphaStage(0,TevAlphaInput.One,TevAlphaInput.Constant,TevAlphaInput.Zero,TevAlphaInput.Zero));
Reject<ArgumentException>(()=>alphaBuilder.SetAlphaStage(0,(TevAlphaInput)10,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero));
alphaBuilder.SetAlphaStage(0,TevAlphaInput.Previous,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero);
Reject<InvalidOperationException>(()=>alphaBuilder.BuildPackage());
alphaBuilder.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.One,TevAlphaInput.One,TevAlphaInput.Zero,operation:TevAlphaOperation.Subtract,bias:TevBias.AddHalf,scale:TevScale.Two,clamp:false);
Check(EvaluateAlpha(alphaBuilder.BuildPackage(),255,0,0)==-254,"Subtract, signed output, bias and scale");
alphaBuilder.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Previous);
Reject<InvalidOperationException>(()=>alphaBuilder.BuildPackage());
alphaBuilder.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Previous);
Check(alphaBuilder.BuildPackage().Length==208,"Complete alpha program");
var conflict=new TevProgramBuilder();conflict.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Texture);
Reject<ArgumentException>(()=>conflict.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.SecondaryTexture));
Check(conflict.BuildPackage()[4]==1,"Failed alpha configuration leaves legacy builder unchanged");
conflict.SetAlphaCompare(AlphaComparison.LessOrEqual,33,AlphaCompareOperation.Or,AlphaComparison.GreaterOrEqual,235);
Check(conflict.BuildPackage()[4]==4&&conflict.BuildPackage()[189]==0,"Compare-only program keeps legacy opacity");
Console.WriteLine("DNEF v4: exhaustive two-texture alpha, raster opacity, outside-band comparison, signed arithmetic and trust-boundary configuration passed.");

// Alpha constants choose scalar channels independently of the RGB konst selector.
var constants=new TevProgramBuilder();int constantSlot=constants.AddColor("Channels",new Color32(17,63,129,231));
constants.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Constant,colorSlot:constantSlot);
foreach(var component in new[]{TevComponent.Red,TevComponent.Green,TevComponent.Blue,TevComponent.Alpha}){
    constants.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Constant,colorSlot:constantSlot,component:component);
    Check(EvaluateAlpha(constants.BuildPackage(),255,0,0)==new[]{17,63,129,231}[(int)component-1],"Independent alpha konst channel");
}
Reject<ArgumentException>(()=>constants.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Constant,colorSlot:constantSlot,component:TevComponent.Rgb));
Reject<ArgumentException>(()=>constants.SetAlphaCompare((AlphaComparison)8,0,AlphaCompareOperation.Or,AlphaComparison.Always,255));
var sixteen=new TevProgramBuilder();
for(int i=0;i<16;i++){
    sixteen.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.One);
    sixteen.SetAlphaStage(i,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,(i&1)==0?TevAlphaInput.Texture:TevAlphaInput.SecondaryTexture);
}
Check(sixteen.BuildPackage().Length==432&&sixteen.BuildPackage()[8]==16,"Explicit dual alpha supports all sixteen physical stages");
var compareDual=new TevProgramBuilder();compareDual.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.SecondaryTexture);
compareDual.SetAlphaCompare(AlphaComparison.Greater,10,AlphaCompareOperation.And,AlphaComparison.Always,0);
var compareBytes=compareDual.BuildPackage();
Check(compareBytes[8]==2&&compareBytes[4]==4&&compareBytes[^9]==0&&compareBytes[^1]==0,"Compare-only dual effect preserves legacy primary alpha epilogue");
var warpAlpha=new TevProgramBuilder();warpAlpha.EnableIndirectWarp();
warpAlpha.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Texture,alternateUv:true);
Reject<ArgumentException>(()=>warpAlpha.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.SecondaryTexture));
warpAlpha.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Texture,TevAlphaInput.Raster,TevAlphaInput.Zero);
Check(warpAlpha.BuildPackage()[4]==4&&warpAlpha.BuildPackage()[20]==76&&warpAlpha.BuildPackage()[183]==2,"Explicit alpha retains primary indirect UV selection");
Console.WriteLine("DNEF v4: scalar konst channels, sixteen-stage budget, legacy dual opacity and indirect-alpha compatibility passed.");

var extended=new TevProgramBuilder();
extended.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.TextureAlpha,TevRegister.Color0,clamp:false);
extended.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Texture,TevAlphaInput.One,TevAlphaInput.Zero,TevRegister.Color0,scale:TevScale.Two,clamp:false);
extended.AddStage(TevInput.Zero,TevInput.SecondaryTextureAlpha,TevInput.Color0Alpha,TevInput.Color0,scale:TevScale.Two,clamp:false);
extended.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Color0);
var extendedBytes=extended.BuildPackage();
Check(extendedBytes[4]==5&&extendedBytes.Length==208&&extendedBytes[183]==0&&extendedBytes[199]==1,"v5 envelope and alpha texture order");
for(int first=0;first<256;first++)for(int second=0;second<256;second++){
    int expected=Arithmetic(0,second,(first*2)&255,first,0,144)&255;
    Check(Evaluate(extendedBytes,255,first,second)[0]==expected,"Unclamped RGB, replicated alpha and wrapped alpha-register input");
}
var invalidAlpha=new TevProgramBuilder();
invalidAlpha.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.PreviousAlpha);
invalidAlpha.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Raster);
Reject<InvalidOperationException>(()=>invalidAlpha.BuildPackage());
var subtractRgb=new TevProgramBuilder();subtractRgb.AddStage(TevInput.Zero,TevInput.One,TevInput.Half,TevInput.Zero,operation:TevOperation.Subtract,bias:TevBias.SubtractHalf,scale:TevScale.Two,clamp:false);
Check(Evaluate(subtractRgb.BuildPackage(),0,0)[0]==255,"Signed subtract/bias/scale writes signed register then emits its low byte");
Reject<ArgumentException>(()=>subtractRgb.AddStage(TevInput.Zero,TevInput.TextureAlpha,TevInput.SecondaryTextureAlpha,TevInput.Zero));
Reject<ArgumentException>(()=>subtractRgb.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.One,operation:TevOperation.GreaterRed,clamp:false));
Console.WriteLine("DNEF v5: exhaustive two-sample RGB/alpha wrapping, signed arithmetic, independent dataflow and sampler conflicts passed.");
var blending=new TevProgramBuilder();blending.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
blending.SetBlendFactors(TevBlendFactor.SourceAlpha,TevBlendFactor.Color);
var blendingBytes=blending.BuildPackage();
Check(blendingBytes[4]==5&&blendingBytes[22]==1&&blendingBytes[29]==4&&blendingBytes[30]==2&&blendingBytes[31]==0,"Exact custom GX blend factors in bounded v5 header");
Reject<ArgumentException>(()=>blending.SetBlendFactors((TevBlendFactor)8,TevBlendFactor.Zero));
Check(blending.BuildPackage().SequenceEqual(blendingBytes),"Failed blend mutation leaves valid configuration intact");
// Existing engine IL binds the original nine-parameter CLR signature.
var legacySignature=new TevProgramBuilder();
Action<TevInput,TevInput,TevInput,TevInput,TevRegister,TevOperation,int,TevComponent,bool> oldAdd=legacySignature.AddStage;
oldAdd(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster,TevRegister.Previous,TevOperation.Add,-1,TevComponent.Rgb,false);
var defaultSignature=new TevProgramBuilder();defaultSignature.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
Check(legacySignature.BuildPackage().SequenceEqual(defaultSignature.BuildPackage()),"Original binary signature and new optional source API preserve legacy bytes");
