using System;
namespace Dolphin.Graphics;
public sealed class ModelInstance : IDisposable
{
    public Model Model {get;}
    public Animator Animator {get;}
    internal readonly int Token;
    private bool disposed;
    internal ModelInstance(Model model,int token){Model=model;Token=token;Animator=new Animator(this);}
    internal void Check(){if(disposed)throw new ObjectDisposedException("ModelInstance");Model.CheckInstanceUse();}
    internal void Control(int op,float value){Check();if(NativeModel.ModelControl(Token,op,value)<0)throw new InvalidOperationException("Model is cold, the frame is active, or animation input/budget is invalid.");}
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
