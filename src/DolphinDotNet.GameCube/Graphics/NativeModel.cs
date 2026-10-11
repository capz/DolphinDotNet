using System;
namespace Dolphin.Graphics;
internal static class NativeModel
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static int ModelTrs(float[] values,float[] matrix)=>throw Unsupported();
    internal static int ModelMultiply(float[] a,float[] b,float[] output)=>throw Unsupported();
    internal static int ModelPosition(float[] values,float[] parent,float x,float y,float z)=>throw Unsupported();
    internal static int ModelTranslate(float[] values,float x,float y,float z)=>throw Unsupported();
    internal static int ModelRotate(float[] values,float x,float y,float z)=>throw Unsupported();
    internal static int ModelCamera(float[] settings,float[] view,float[] projection)=>throw Unsupported();
    internal static int ModelBegin(string path,int budget)=>throw Unsupported();
    internal static int ModelStep(int token)=>throw Unsupported();
    internal static int ModelDispose(int token)=>throw Unsupported();
    internal static int ModelStat(int token,int kind)=>throw Unsupported();
    internal static string ModelName(int token,int clip,int node)=>throw Unsupported();
    internal static float ModelDuration(int token,int clip)=>throw Unsupported();
    internal static int ModelCopy(int token,int offset,byte[] buffer,int count,int write)=>throw Unsupported();
    internal static int ModelRestoreBegin(int token)=>throw Unsupported();
    internal static int ModelRestoreEnd(int token,int commit)=>throw Unsupported();
    internal static int ModelUnload(int token)=>throw Unsupported();
    internal static int ModelInstance(int token,int budget)=>throw Unsupported();
    internal static int ModelInstanceBytes(int token)=>throw Unsupported();
    internal static int ModelInstanceDispose(int token)=>throw Unsupported();
    internal static int ModelPlay(int token,int clip,int loop,float fade)=>throw Unsupported();
    internal static int ModelControl(int token,int op,float value)=>throw Unsupported();
    internal static int ModelNodeMatrix(int token,int node,float[] matrix)=>throw Unsupported();
    internal static int ModelDraw(int token,float[] matrix)=>throw Unsupported();
    internal static int ModelFrameBegin(float[] view,float[] projection)=>throw Unsupported();
    internal static int ModelFrameEnd()=>throw Unsupported();
}
