using System;
using Dolphin.Collections;
namespace Dolphin.Graphics;
public struct Vector3
{
    public float X,Y,Z;
    public Vector3(float x,float y,float z){X=x;Y=y;Z=z;}
    public static Vector3 Zero=>new Vector3(0,0,0);
    public static Vector3 One=>new Vector3(1,1,1);
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.X*b,a.Y*b,a.Z*b);
}
public struct Quaternion
{
    public float X,Y,Z,W;
    public Quaternion(float x,float y,float z,float w){X=x;Y=y;Z=z;W=w;}
    public static Quaternion Identity=>new Quaternion(0,0,0,1);
}
/// <summary>Cached TRS transform. Parent changes preserve local coordinates; Euler rotation uses local XYZ degrees.</summary>
public sealed class Transform
{
    private readonly float[] values=new float[10];
    private readonly float[] local=new float[16],world=new float[16];
    private readonly List<Transform> children=new List<Transform>();
    private Transform? parent;
    private bool localDirty=true,worldDirty=true;
    public Transform(){values[6]=1;values[7]=values[8]=values[9]=1;}
    public Vector3 LocalPosition {get=>new Vector3(values[0],values[1],values[2]);set{values[0]=value.X;values[1]=value.Y;values[2]=value.Z;Changed();}}
    public Quaternion LocalRotation {get=>new Quaternion(values[3],values[4],values[5],values[6]);set{values[3]=value.X;values[4]=value.Y;values[5]=value.Z;values[6]=value.W;Changed();}}
    public Vector3 LocalScale {get=>new Vector3(values[7],values[8],values[9]);set{values[7]=value.X;values[8]=value.Y;values[9]=value.Z;Changed();}}
    public Vector3 Position
    {
        get{var m=WorldMatrix;return new Vector3(m[12],m[13],m[14]);}
        set{if(parent==null)LocalPosition=value;else{if(NativeModel.ModelPosition(values,parent.WorldMatrix,value.X,value.Y,value.Z)<0)throw new ArgumentException("Invalid world position or parent transform.");Changed();}}
    }
    public Transform? Parent
    {
        get=>parent;
        set
        {
            if(value==parent)return;
            int depth=1;for(var p=value;p!=null;p=p.parent){if(p==this)throw new ArgumentException("Transform cycle.");depth++;if(depth>128)throw new ArgumentException("Transform hierarchy exceeds 128 levels.");}
            ValidateDepth(depth);
            if(value!=null)value.children.Add(this);if(parent!=null)parent.children.Remove(this);parent=value;DirtyWorld();
        }
    }
    private void ValidateDepth(int depth){if(depth>128)throw new ArgumentException("Transform hierarchy exceeds 128 levels.");for(int i=0;i<children.Count;i++)children[i].ValidateDepth(depth+1);}
    private void Changed(){localDirty=true;DirtyWorld();}
    private void DirtyWorld(){if(worldDirty)return;worldDirty=true;for(int i=0;i<children.Count;i++)children[i].DirtyWorld();}
    public void Translate(Vector3 offset,bool worldSpace=false){if(worldSpace)Position=Position+offset;else{if(NativeModel.ModelTranslate(values,offset.X,offset.Y,offset.Z)<0)throw new ArgumentException("Invalid translation.");Changed();}}
    public void Rotate(float xDegrees,float yDegrees,float zDegrees){if(NativeModel.ModelRotate(values,xDegrees,yDegrees,zDegrees)<0)throw new ArgumentException("Invalid rotation.");Changed();}
    internal float[] WorldMatrix
    {
        get
        {
            if(worldDirty){if(localDirty){if(NativeModel.ModelTrs(values,local)<0)throw new ArgumentException("Transform must be finite, with nonzero quaternion and scale.");localDirty=false;}
                if(parent==null){for(int i=0;i<16;i++)world[i]=local[i];}else if(NativeModel.ModelMultiply(parent.WorldMatrix,local,world)<0)throw new ArgumentException("World transform is invalid.");worldDirty=false;}
            return world;
        }
    }
    /// <summary>Copy into a caller-owned column-major 16-float buffer.</summary>
    public void CopyWorldMatrix(float[] destination){if(destination==null||destination.Length!=16)throw new ArgumentException("Expected 16-float matrix.");var m=WorldMatrix;for(int i=0;i<16;i++)destination[i]=m[i];}
}
public sealed class GameObject : IDisposable
{
    public Transform Transform {get;}=new Transform();
    public ModelInstance Model {get;}
    public GameObject(ModelInstance model){if(model==null)throw new ArgumentNullException("model");Model=model;}
    public void Update(float deltaSeconds)=>Model.Update(deltaSeconds);
    public void Dispose()=>Model.Dispose();
}
/// <summary>Perspective camera with explicit look-at view. Coordinates use right-handed, Y-up model space.</summary>
public sealed class Camera
{
    internal readonly float[] View=new float[16],Projection=new float[16];
    private readonly float[] settings=new float[13];
    public Camera(float fieldOfViewDegrees=60,float aspectRatio=4f/3f,float nearPlane=0.1f,float farPlane=100)
    {settings[2]=5;settings[7]=1;settings[9]=fieldOfViewDegrees;settings[10]=aspectRatio;settings[11]=nearPlane;settings[12]=farPlane;Refresh();}
    public void LookAt(Vector3 eye,Vector3 target,Vector3 up)
    {settings[0]=eye.X;settings[1]=eye.Y;settings[2]=eye.Z;settings[3]=target.X;settings[4]=target.Y;settings[5]=target.Z;settings[6]=up.X;settings[7]=up.Y;settings[8]=up.Z;Refresh();}
    private void Refresh(){if(NativeModel.ModelCamera(settings,View,Projection)<0)throw new ArgumentException("Invalid camera frustum or look-at vectors.");}
}
