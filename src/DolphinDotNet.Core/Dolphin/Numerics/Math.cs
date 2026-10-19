using System;
namespace Dolphin.Numerics;
public enum MidpointRounding { ToEven=0,AwayFromZero=1,ToZero=2,ToNegativeInfinity=3,ToPositiveInfinity=4 }
public static class Math
{
    public static Decimal Abs(Decimal x)=>Decimal.Abs(x);
    public static int Sign(Decimal x)=>Decimal.Sign(x);
    public static Decimal Min(Decimal a,Decimal b)=>Decimal.Min(a,b);
    public static Decimal Max(Decimal a,Decimal b)=>Decimal.Max(a,b);
    public static Decimal Clamp(Decimal x,Decimal min,Decimal max){if(min>max)throw new ArgumentException();return x<min?min:x>max?max:x;}
    public static Decimal Floor(Decimal x)=>Decimal.Floor(x);
    public static Decimal Ceiling(Decimal x)=>Decimal.Ceiling(x);
    public static Decimal Truncate(Decimal x)=>Decimal.Truncate(x);
    public static Decimal Round(Decimal x)=>Decimal.Round(x);
    public static Decimal Round(Decimal x,int digits)=>Decimal.Round(x,digits);
    public static Decimal Round(Decimal x,MidpointRounding mode)=>Decimal.Round(x,mode);
    public static Decimal Round(Decimal x,int digits,MidpointRounding mode)=>Decimal.Round(x,digits,mode);
    public static double Cbrt(double x)=>NativeNumeric.Unary(17,x);
    public static double Log2(double x)=>NativeNumeric.Unary(18,x);
    public static double Acosh(double x)=>NativeNumeric.Unary(19,x);
    public static double Asinh(double x)=>NativeNumeric.Unary(20,x);
    public static double Atanh(double x)=>NativeNumeric.Unary(21,x);
    public static double BitIncrement(double x)=>NativeNumeric.Unary(22,x);
    public static double BitDecrement(double x)=>NativeNumeric.Unary(23,x);
    public static int ILogB(double x)=>(int)NativeNumeric.Unary(24,x);
    public static double CopySign(double x,double sign)=>NativeNumeric.Binary(5,x,sign);
    public static double ScaleB(double x,int n)=>NativeNumeric.Binary(6,x,n);
    public static double FusedMultiplyAdd(double x,double y,double z)=>NativeNumeric.Fma(0,x,y,z);
    public static double Abs(double x)=>NativeNumeric.Unary(0,x);
    public static double Sqrt(double x)=>NativeNumeric.Unary(1,x);
    public static double Sin(double x)=>NativeNumeric.Unary(2,x);
    public static double Cos(double x)=>NativeNumeric.Unary(3,x);
    public static double Tan(double x)=>NativeNumeric.Unary(4,x);
    public static double Asin(double x)=>NativeNumeric.Unary(5,x);
    public static double Acos(double x)=>NativeNumeric.Unary(6,x);
    public static double Atan(double x)=>NativeNumeric.Unary(7,x);
    public static double Sinh(double x)=>NativeNumeric.Unary(8,x);
    public static double Cosh(double x)=>NativeNumeric.Unary(9,x);
    public static double Tanh(double x)=>NativeNumeric.Unary(10,x);
    public static double Exp(double x)=>NativeNumeric.Unary(11,x);
    public static double Log(double x)=>NativeNumeric.Unary(12,x);
    public static double Log10(double x)=>NativeNumeric.Unary(13,x);
    public static double Floor(double x)=>NativeNumeric.Unary(14,x);
    public static double Ceiling(double x)=>NativeNumeric.Unary(15,x);
    public static double Truncate(double x)=>NativeNumeric.Unary(16,x);
    public static double Pow(double a,double b)=>NativeNumeric.Binary(0,a,b);
    public static double Atan2(double a,double b)=>NativeNumeric.Binary(1,a,b);
    public static double IEEERemainder(double a,double b)=>NativeNumeric.Binary(2,a,b);
    public static double Min(double a,double b)=>NativeNumeric.Binary(3,a,b);
    public static double Max(double a,double b)=>NativeNumeric.Binary(4,a,b);
    public static double Log(double x,double newBase)=>newBase==1||x!=1&&(newBase==0||double.IsPositiveInfinity(newBase))?double.NaN:Log(x)/Log(newBase);
    public static double Round(double x)=>Round(x,0,MidpointRounding.ToEven);
    public static double Round(double x,int digits)=>Round(x,digits,MidpointRounding.ToEven);
    public static double Round(double x,MidpointRounding mode)=>Round(x,0,mode);
    public static double Round(double x,int digits,MidpointRounding mode)=>NativeNumeric.Round(x,digits,(int)mode);
    public static int Sign(double x)=>NativeNumeric.Sign(x);
    public static int Sign(float x)=>Sign((double)x);
    public static int Abs(int x){if(x==int.MinValue)throw new OverflowException();return x<0?(int)(-x):x;}
    public static int Sign(int x)=>x<0?-1:x>0?1:0;
    public static long Abs(long x){if(x==long.MinValue)throw new OverflowException();return x<0?(long)(-x):x;}
    public static int Sign(long x)=>x<0?-1:x>0?1:0;
    public static short Abs(short x){if(x==short.MinValue)throw new OverflowException();return x<0?(short)(-x):x;}
    public static int Sign(short x)=>x<0?-1:x>0?1:0;
    public static sbyte Abs(sbyte x){if(x==sbyte.MinValue)throw new OverflowException();return x<0?(sbyte)(-x):x;}
    public static int Sign(sbyte x)=>x<0?-1:x>0?1:0;
    public static int Min(int a,int b)=>a<b?a:b;
    public static int Max(int a,int b)=>a>b?a:b;
    public static long Min(long a,long b)=>a<b?a:b;
    public static long Max(long a,long b)=>a>b?a:b;
    public static uint Min(uint a,uint b)=>a<b?a:b;
    public static uint Max(uint a,uint b)=>a>b?a:b;
    public static ulong Min(ulong a,ulong b)=>a<b?a:b;
    public static ulong Max(ulong a,ulong b)=>a>b?a:b;
    public static short Min(short a,short b)=>a<b?a:b;
    public static short Max(short a,short b)=>a>b?a:b;
    public static ushort Min(ushort a,ushort b)=>a<b?a:b;
    public static ushort Max(ushort a,ushort b)=>a>b?a:b;
    public static byte Min(byte a,byte b)=>a<b?a:b;
    public static byte Max(byte a,byte b)=>a>b?a:b;
    public static sbyte Min(sbyte a,sbyte b)=>a<b?a:b;
    public static sbyte Max(sbyte a,sbyte b)=>a>b?a:b;
    public static float Abs(float x)=>(float)Abs((double)x);
    public static float Min(float a,float b)=>(float)Min((double)a,(double)b);
    public static float Max(float a,float b)=>(float)Max((double)a,(double)b);
    public static int Clamp(int x,int min,int max){if(min>max)throw new ArgumentException("min exceeds max.");return x<min?min:x>max?max:x;}
    public static long Clamp(long x,long min,long max){if(min>max)throw new ArgumentException("min exceeds max.");return x<min?min:x>max?max:x;}
    public static float Clamp(float x,float min,float max){if(min>max)throw new ArgumentException("min exceeds max.");return x<min?min:x>max?max:x;}
    public static double Clamp(double x,double min,double max){if(min>max)throw new ArgumentException("min exceeds max.");return x<min?min:x>max?max:x;}
    public static long BigMul(int a,int b)=>(long)a*b;
    public static int DivRem(int a,int b,out int remainder){if(b==0)throw new DivideByZeroException();if(a==int.MinValue&&b==-1)throw new OverflowException();remainder=a%b;return a/b;}
    public static long DivRem(long a,long b,out long remainder){if(b==0)throw new DivideByZeroException();if(a==long.MinValue&&b==-1)throw new OverflowException();remainder=a%b;return a/b;}
}
public static class MathF
{
    public static float Cbrt(float x)=>(float)Math.Cbrt(x);
    public static float Log2(float x)=>(float)Math.Log2(x);
    public static float Acosh(float x)=>(float)Math.Acosh(x);
    public static float Asinh(float x)=>(float)Math.Asinh(x);
    public static float Atanh(float x)=>(float)Math.Atanh(x);
    public static float BitIncrement(float x)=>(float)NativeNumeric.Unary(25,x);
    public static float BitDecrement(float x)=>(float)NativeNumeric.Unary(26,x);
    public static int ILogB(float x)=>Math.ILogB(x);
    public static float CopySign(float x,float sign)=>(float)Math.CopySign(x,sign);
    public static float ScaleB(float x,int n)=>(float)NativeNumeric.Binary(7,x,n);
    public static float FusedMultiplyAdd(float x,float y,float z)=>(float)NativeNumeric.Fma(1,x,y,z);
    public static float Abs(float x)=>(float)Math.Abs(x);
    public static float Sqrt(float x)=>(float)Math.Sqrt(x);
    public static float Sin(float x)=>(float)Math.Sin(x);
    public static float Cos(float x)=>(float)Math.Cos(x);
    public static float Tan(float x)=>(float)Math.Tan(x);
    public static float Asin(float x)=>(float)Math.Asin(x);
    public static float Acos(float x)=>(float)Math.Acos(x);
    public static float Atan(float x)=>(float)Math.Atan(x);
    public static float Sinh(float x)=>(float)Math.Sinh(x);
    public static float Cosh(float x)=>(float)Math.Cosh(x);
    public static float Tanh(float x)=>(float)Math.Tanh(x);
    public static float Exp(float x)=>(float)Math.Exp(x);
    public static float Log(float x)=>(float)Math.Log(x);
    public static float Log10(float x)=>(float)Math.Log10(x);
    public static float Floor(float x)=>(float)Math.Floor(x);
    public static float Ceiling(float x)=>(float)Math.Ceiling(x);
    public static float Truncate(float x)=>(float)Math.Truncate(x);
    public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
    public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b);
    public static float IEEERemainder(float a,float b)=>(float)Math.IEEERemainder(a,b);
    public static float Min(float a,float b)=>(float)Math.Min(a,b);
    public static float Max(float a,float b)=>(float)Math.Max(a,b);
    public static float Log(float a,float b)=>(float)Math.Log(a,b);
    public static float Round(float x)=>(float)Math.Round(x);
    public static float Round(float x,int digits)=>Round(x,digits,MidpointRounding.ToEven);
    public static float Round(float x,MidpointRounding mode)=>Round(x,0,mode);
    public static float Round(float x,int digits,MidpointRounding mode){if(digits<0||digits>6)throw new ArgumentOutOfRangeException("digits");return (float)Math.Round(x,digits,mode);}
    public static int Sign(float x)=>Math.Sign(x);
}
internal static class NativeNumeric
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static double Unary(int op,double x)=>throw Unsupported();
    internal static double Binary(int op,double a,double b)=>throw Unsupported();
    internal static double Fma(int op,double x,double y,double z)=>throw Unsupported();
    internal static double Round(double x,int digits,int mode)=>throw Unsupported();
    internal static int Sign(double x)=>throw Unsupported();
    internal static int Classify(int op,double x)=>throw Unsupported();
    internal static int ParseI32(string? text,ref int value,int throwing)=>throw Unsupported();
    internal static int ParseU32(string? text,ref uint value,int throwing)=>throw Unsupported();
    internal static int ParseI64(string? text,ref long value,int throwing)=>throw Unsupported();
    internal static int ParseU64(string? text,ref ulong value,int throwing)=>throw Unsupported();
    internal static int ParseF32(string? text,ref float value,int throwing)=>throw Unsupported();
    internal static int ParseF64(string? text,ref double value,int throwing)=>throw Unsupported();
}
