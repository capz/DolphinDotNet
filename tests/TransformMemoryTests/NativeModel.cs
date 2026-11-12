// CLR-only matrix backend. The AOT ModelSmoke suite separately verifies the
// same hierarchy transitions against the production native matrix functions.
using System;
using System.Numerics;
namespace Dolphin.Graphics;
internal static class NativeModel
{
    internal static int ModelTrs(float[] values,float[] matrix)
    {
        for(int i=0;i<10;i++)if(!float.IsFinite(values[i]))return -1;
        var q=new System.Numerics.Quaternion(values[3],values[4],values[5],values[6]);
        if(q.LengthSquared()==0||values[7]==0||values[8]==0||values[9]==0)return -1;
        Write(Matrix4x4.CreateScale(values[7],values[8],values[9])*
            Matrix4x4.CreateFromQuaternion(System.Numerics.Quaternion.Normalize(q))*
            Matrix4x4.CreateTranslation(values[0],values[1],values[2]),matrix);
        return 0;
    }
    internal static int ModelMultiply(float[] a,float[] b,float[] output)
    {Write(Read(b)*Read(a),output);return 0;}
    private static Matrix4x4 Read(float[] a)=>new Matrix4x4(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);
    private static void Write(Matrix4x4 a,float[] b)
    {
        b[0]=a.M11;b[1]=a.M12;b[2]=a.M13;b[3]=a.M14;
        b[4]=a.M21;b[5]=a.M22;b[6]=a.M23;b[7]=a.M24;
        b[8]=a.M31;b[9]=a.M32;b[10]=a.M33;b[11]=a.M34;
        b[12]=a.M41;b[13]=a.M42;b[14]=a.M43;b[15]=a.M44;
    }
    internal static int ModelPosition(float[] a,float[] b,float x,float y,float z)=>throw new NotSupportedException();
    internal static int ModelTranslate(float[] a,float x,float y,float z)=>throw new NotSupportedException();
    internal static int ModelRotate(float[] a,float x,float y,float z)=>throw new NotSupportedException();
}
