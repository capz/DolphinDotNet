using Dolphin.Graphics;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
// Independent fixed-point TEV interpreter. It consumes the serialized package,
// including register writes and channel selectors, rather than calling preset code.
static int[] Evaluate(byte[] p,int raster,int tex)
{
    int[][] regs=[new int[3],new int[3],new int[3],new int[3]];
    for(int i=0;i<p[8];i++){
        int at=176+i*8,k=p[at+6];
        int Read(int input,int channel)=>input switch {
            0 or 2 or 4 or 6=>regs[input/2][channel],8=>tex,10=>raster,12=>255,15=>0,
            14=>p[64+(k-12)%4*36+(k<16?channel:(k-16)/4)],_=>throw new Exception("Invalid selector")};
        int[] result=new int[3];
        for(int c=0;c<3;c++){
            int a=Read(p[at],c),b=Read(p[at+1],c),t=Read(p[at+2],c),d=Read(p[at+3],c);
            result[c]=Math.Clamp(p[at+4]==8?d+(Read(p[at],0)>Read(p[at+1],0)?t:0):a+((b-a)*(t+(t>>7))>>8)+d,0,255);
        }
        regs[p[at+5]]=result;
    }
    return regs[0];
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
Reject<ArgumentException>(()=>b.AddStage((TevInput)9,TevInput.Zero,TevInput.Zero,TevInput.Zero));
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
