using System;
namespace Dolphin.Numerics;
public static class Int32
{
    public static int Parse(string text){int value=0;NativeNumeric.ParseI32(text,ref value,1);return value;}
    public static bool TryParse(string? text,out int value){value=0;return NativeNumeric.ParseI32(text,ref value,0)!=0;}
}
public static class UInt32
{
    public static uint Parse(string text){uint value=0;NativeNumeric.ParseU32(text,ref value,1);return value;}
    public static bool TryParse(string? text,out uint value){value=0;return NativeNumeric.ParseU32(text,ref value,0)!=0;}
}
public static class Int64
{
    public static long Parse(string text){long value=0;NativeNumeric.ParseI64(text,ref value,1);return value;}
    public static bool TryParse(string? text,out long value){value=0;return NativeNumeric.ParseI64(text,ref value,0)!=0;}
}
public static class UInt64
{
    public static ulong Parse(string text){ulong value=0;NativeNumeric.ParseU64(text,ref value,1);return value;}
    public static bool TryParse(string? text,out ulong value){value=0;return NativeNumeric.ParseU64(text,ref value,0)!=0;}
}
public static class Single
{
    public static float Parse(string text){float value=0;NativeNumeric.ParseF32(text,ref value,1);return value;}
    public static bool TryParse(string? text,out float value){value=0;return NativeNumeric.ParseF32(text,ref value,0)!=0;}
    public static bool IsNaN(float x)=>NativeNumeric.Classify(0,x)!=0;
    public static bool IsInfinity(float x)=>NativeNumeric.Classify(1,x)!=0;
    public static bool IsPositiveInfinity(float x)=>NativeNumeric.Classify(2,x)!=0;
    public static bool IsNegativeInfinity(float x)=>NativeNumeric.Classify(3,x)!=0;
    public static bool IsFinite(float x)=>NativeNumeric.Classify(4,x)!=0;
}
public static class Double
{
    public static double Parse(string text){double value=0;NativeNumeric.ParseF64(text,ref value,1);return value;}
    public static bool TryParse(string? text,out double value){value=0;return NativeNumeric.ParseF64(text,ref value,0)!=0;}
    public static bool IsNaN(double x)=>NativeNumeric.Classify(0,x)!=0;
    public static bool IsInfinity(double x)=>NativeNumeric.Classify(1,x)!=0;
    public static bool IsPositiveInfinity(double x)=>NativeNumeric.Classify(2,x)!=0;
    public static bool IsNegativeInfinity(double x)=>NativeNumeric.Classify(3,x)!=0;
    public static bool IsFinite(double x)=>NativeNumeric.Classify(4,x)!=0;
}
public static class Boolean
{
    public static bool Parse(string text)
    {
        if(text==null)throw new ArgumentNullException("text");
        if(TryParse(text,out bool result))return result;throw new FormatException();
    }
    public static bool TryParse(string? text,out bool result)
    {
        result=false;if(text==null)return false;
        int first=0,last=text.Length;
        while(first<last&&Space(text[first]))first++;while(last>first&&Space(text[last-1]))last--;
        if(last-first==4&&Equal(text,first,"true")){result=true;return true;}
        return last-first==5&&Equal(text,first,"false");
    }
    private static bool Equal(string text,int first,string word)
    {for(int i=0;i<word.Length;i++){char c=text[first+i];if(c>='A'&&c<='Z')c=(char)(c+32);if(c!=word[i])return false;}return true;}
    private static bool Space(char c)=>c>=9&&c<=13||c==32||c==0x85||c==0xa0||c==0x1680||c>=0x2000&&c<=0x200a||c==0x2028||c==0x2029||c==0x202f||c==0x205f||c==0x3000;
}

public static class Byte
{
    public static byte Parse(string text){int n=Int32.Parse(text);if(n<0||n>255)throw new OverflowException();return (byte)n;}
    public static bool TryParse(string? text,out byte value){int n;bool success=Int32.TryParse(text,out n)&&n>=0&&n<=255;value=success?(byte)n:(byte)0;return success;}
}

public static class SByte
{
    public static sbyte Parse(string text){int n=Int32.Parse(text);if(n<-128||n>127)throw new OverflowException();return (sbyte)n;}
    public static bool TryParse(string? text,out sbyte value){int n;bool success=Int32.TryParse(text,out n)&&n>=-128&&n<=127;value=success?(sbyte)n:(sbyte)0;return success;}
}

public static class Int16
{
    public static short Parse(string text){int n=Int32.Parse(text);if(n<-32768||n>32767)throw new OverflowException();return (short)n;}
    public static bool TryParse(string? text,out short value){int n;bool success=Int32.TryParse(text,out n)&&n>=-32768&&n<=32767;value=success?(short)n:(short)0;return success;}
}

public static class UInt16
{
    public static ushort Parse(string text){int n=Int32.Parse(text);if(n<0||n>65535)throw new OverflowException();return (ushort)n;}
    public static bool TryParse(string? text,out ushort value){int n;bool success=Int32.TryParse(text,out n)&&n>=0&&n<=65535;value=success?(ushort)n:(ushort)0;return success;}
}
