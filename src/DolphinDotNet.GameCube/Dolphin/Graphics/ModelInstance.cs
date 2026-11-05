using System;
namespace Dolphin.Graphics;
public sealed class ModelInstance : IDisposable
{
    public Model Model {get;}
    public Animator Animator {get;}
    public int NativeBytes {get{Check();return NativeModel.ModelInstanceBytes(Token);}}
    internal readonly int Token;
    private bool disposed;
    private readonly float[] scratch=new float[10];
    private readonly float[] colorScratch=new float[4];
    internal ModelInstance(Model model,int token){Model=model;Token=token;Animator=new Animator(this);}
    internal void Check(){if(disposed)throw new ObjectDisposedException("ModelInstance");Model.CheckInstanceUse();}
    internal void Control(int op,float value){Check();if(NativeModel.ModelControl(Token,op,value)<0)throw new InvalidOperationException("Model is cold, the frame is active, or animation input/budget is invalid.");}
    /// <summary>Override local pose relative to the sampled/rest joint. Updates
    /// are between frames and private to this instance; ResetNodeOffset clears it.</summary>
    public void SetNodeOffset(int node,Vector3 translation,Quaternion rotation,Vector3 scale)
    {
        Check();scratch[0]=translation.X;scratch[1]=translation.Y;scratch[2]=translation.Z;
        scratch[3]=rotation.X;scratch[4]=rotation.Y;scratch[5]=rotation.Z;scratch[6]=rotation.W;
        scratch[7]=scale.X;scratch[8]=scale.Y;scratch[9]=scale.Z;
        if(NativeModel.ModelNodeOffset(Token,node,scratch)<0)throw new ArgumentException("Invalid pose, active frame or exhausted instance budget.");
    }
    public void ResetNodeOffset(int node){Check();if(NativeModel.ModelNodeOffset(Token,node,null)<0)throw new ArgumentException("Invalid node or active frame.");}
    /// <summary>Absolute authored MAT1 color, including originally transparent
    /// materials. Existing material-animation multipliers still apply afterward.</summary>
    public void SetMaterialColor(int material,Color32 color)=>SetAuthoredColor(material,0,color.R,color.G,color.B,color.A);
    /// <summary>Override a signed GX TEV register (0..2) for an authored MAT1 material.</summary>
    public void SetTevColor(int material,int register,int red,int green,int blue,int alpha)
    {if(register<0||register>2)throw new ArgumentOutOfRangeException(nameof(register));SetAuthoredColor(material,register+1,red,green,blue,alpha);}
    private void SetAuthoredColor(int material,int slot,int r,int g,int b,int a)
    {
        Check();colorScratch[0]=r;colorScratch[1]=g;colorScratch[2]=b;colorScratch[3]=a;
        if(NativeModel.ModelColor(Token,material,slot,colorScratch)<0)throw new ArgumentException("Invalid authored color, active frame or exhausted instance budget.");
    }
    public void ResetMaterialColors(int material)
    {Check();for(int slot=0;slot<4;slot++)if(NativeModel.ModelColor(Token,material,slot,null)<0)throw new ArgumentException("Invalid authored material or active frame.");}
    public void Update(float deltaSeconds)=>Control(0,deltaSeconds);
    /// <summary>Returns a column-major joint/node matrix in model space, suitable for a socket attachment.</summary>
    public void GetNodeTransform(int nodeIndex,float[] matrix){Check();if(NativeModel.ModelNodeMatrix(Token,nodeIndex,matrix)<0)throw new ArgumentException("Invalid node or matrix buffer; expected 16 floats.");}
    public void Dispose(){if(disposed)return;if(NativeModel.ModelInstanceDispose(Token)<0)throw new InvalidOperationException("End the render frame before disposing instances.");disposed=true;}
}
public sealed class Animator
{
    private readonly ModelInstance instance;
    internal Animator(ModelInstance instance){this.instance=instance;}
    public void Play(string name,bool loop=false){instance.Check();if(NativeModel.ModelPlay(instance.Token,instance.Model.Clip(name),loop?1:0,0)<0)throw new InvalidOperationException("Model is cold, the frame is active, or animation budget is exhausted.");}
    public void CrossFade(string name,float durationSeconds,bool loop=false){instance.Check();if(NativeModel.ModelPlay(instance.Token,instance.Model.Clip(name),loop?1:0,durationSeconds)<0)throw new InvalidOperationException("Invalid fade, active fade/frame, cold model or exhausted animation budget.");}
    public void Pause()=>instance.Control(1,0);
    public void Resume()=>instance.Control(2,0);
    public void Stop()=>instance.Control(3,0);
    public void Seek(float seconds)=>instance.Control(4,seconds);
    public void SetSpeed(float speed)=>instance.Control(5,speed);
}
