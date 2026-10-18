using System;
internal static class Program
{
    private static void Check(bool x){if(!x)throw new Exception("Numeric contract failed.");}
    private static void Near(double a,double b,double tolerance=1e-12){Check(Math.Abs(a-b)<=tolerance);}
    private static double Narrow(double value)=>(float)value;
    private static float AddFloat(float a,float b)=>a+b;
    private static float RefArgument(float x){RefFloat(ref x);return x;}
    private static double Unsigned(ulong value)=>(double)value;
    private static void RefFloat(ref float value){value=3.25f;}
    private static double Remainder(double a,double b)=>a%b;
    private static int phase;
    private static int Main()
    {
        try{Run();return 0;}catch(Exception){return phase;}
    }
    private static void Run()
    {
        phase=1;
        Check(Math.Abs(-42)==42&&Math.Abs(-42L)==42&&Math.Abs((sbyte)-3)==3);
        Check(Math.Min(5u,3u)==3&&Math.Max(9UL,4UL)==9&&Math.Clamp(10,0,5)==5);
        Check(Math.Sign(-0.0)==0&&Math.Sign(long.MinValue)==-1);
        try{Math.Abs(int.MinValue);Check(false);}catch(OverflowException){}
        try{Math.Abs(long.MinValue);Check(false);}catch(ArithmeticException){}
        try{Math.Sign(double.NaN);Check(false);}catch(ArithmeticException){}
        Check(double.IsNaN(Math.Min(1,double.NaN))&&double.IsNaN(Math.Max(double.NaN,1)));
        Check(double.IsNegativeInfinity(1/Math.Min(0.0,-0.0))&&double.IsPositiveInfinity(1/Math.Max(-0.0,0.0)));
        Check(Math.BigMul(int.MaxValue,2)==4294967294L);
        Check(Math.DivRem(-17,5,out int remainder)==-3&&remainder==-2);
        try{Math.DivRem(1,0,out remainder);Check(false);}catch(DivideByZeroException){}
        phase=2;
        Near(Math.Sqrt(9),3);Near(Math.Sin(Math.PI/2),1);Near(Math.Cos(0),1);Near(Math.Tan(0),0);
        Near(Math.Asin(1),Math.PI/2);Near(Math.Acos(1),0);Near(Math.Atan(1),Math.PI/4);Near(Math.Atan2(1,1),Math.PI/4);
        Near(Math.Sinh(0),0);Near(Math.Cosh(0),1);Near(Math.Tanh(0),0);Near(Math.Exp(1),Math.E);
        Near(Math.Log(Math.E),1);Near(Math.Log10(100),2);Near(Math.Log(8,2),3);Near(Math.Pow(3,4),81);
        Check(double.IsNaN(Math.Sqrt(-1))&&double.IsNegativeInfinity(Math.Log(0)));
        Near(Math.Floor(-1.25),-2);Near(Math.Ceiling(-1.25),-1);Near(Math.Truncate(-1.25),-1);
        Near(Math.IEEERemainder(5,2),1);Near(Math.IEEERemainder(7,2),-1);
        Near(MathF.Sqrt(9),3);Near(MathF.Sin(MathF.PI/2),1,1e-6);
        phase=3;
        Check(Math.Round(2.5)==2&&Math.Round(3.5)==4&&Math.Round(-2.5)==-2&&Math.Round(-3.5)==-4);
        Check(Math.Round(-2.5,MidpointRounding.AwayFromZero)==-3);
        Check(Math.Round(1.9,MidpointRounding.ToZero)==1&&Math.Round(-1.2,MidpointRounding.ToPositiveInfinity)==-1);
        Check(Math.Round(-1.2,MidpointRounding.ToNegativeInfinity)==-2);
        Near(Math.Round(1.125,2),1.12);Near(Math.Round(1.125,2,MidpointRounding.AwayFromZero),1.13);
        Check(double.IsNaN(Math.Round(double.NaN))&&double.IsPositiveInfinity(Math.Round(double.PositiveInfinity)));
        Check(double.IsNegativeInfinity(1/Math.Round(-0.1)));
        try{Math.Round(1d,16);Check(false);}catch(ArgumentOutOfRangeException){}
        phase=4;
        Check(Narrow(16777217)==16777216);Check(AddFloat(16777216,1)==16777216&&RefArgument(9)==3.25f);Check(Unsigned(ulong.MaxValue)>1.8e19);
        int signed=int.Parse("2147483647");Check(unchecked(signed+1)<0&&unchecked(signed*2)==-2);
        long wide=long.Parse("9223372036854775807");Check(unchecked(wide+1)==long.MinValue);
        uint unsigned=uint.MaxValue;Check((double)unsigned==4294967295d);
        Near(Remainder(-5.5,2),-1.5);Check(double.IsNaN(Remainder(1,0)));
        double nan=double.Parse("NaN");Check(!(nan>=0)&&!(nan<=0)&&nan!=0);
        float f=1.25f;RefFloat(ref f);Near(f,3.25);Check(float.IsFinite(f)&&!float.IsFinite(float.NaN));
        phase=5;
        Check(int.Parse(" -2147483648 ")==int.MinValue&&int.Parse("+2147483647")==int.MaxValue);
        Check(uint.Parse("4294967295")==uint.MaxValue&&uint.Parse("-0")==0);
        Check(long.Parse("-9223372036854775808")==long.MinValue&&long.Parse("9223372036854775807")==long.MaxValue);
        Check(ulong.Parse("18446744073709551615")==ulong.MaxValue);
        Check(!int.TryParse((string?)null,out int i)&&i==0);Check(!int.TryParse("2147483648",out i)&&i==0);
        Check(!uint.TryParse("-1",out uint u)&&u==0);Check(!ulong.TryParse("18446744073709551616",out ulong ul)&&ul==0);
        Check(!long.TryParse("-9223372036854775809",out long l)&&l==0);
        Check(!int.TryParse("\u2003-12\u00a0",out i)&&i==0);
        try{int.Parse("999999999999999999999x");Check(false);}catch(FormatException){}
        try{long.Parse("9223372036854775808");Check(false);}catch(OverflowException){}
        try{double.Parse(null!);Check(false);}catch(ArgumentNullException){}
        phase=6;
        Near(double.Parse(" -1,234.5e-2 "),-12.345);
        Check(float.TryParse("1.25",out f));Near(f,1.25);
        Check(double.TryParse("1.25",out double d));Near(d,1.25);
        Check(!float.TryParse("1e",out f)&&f==0&&!double.TryParse("0x1p2",out d)&&d==0);
        Check(double.IsNaN(double.Parse("NaN"))&&double.IsNegativeInfinity(double.Parse("-Infinity")));
        Check(double.IsPositiveInfinity(double.Parse("1e9999"))&&float.IsPositiveInfinity(float.Parse("1e99")));
        Check(double.IsNegativeInfinity(1/double.Parse("-0")));
        Check(bool.Parse(" TRUE ")&&!bool.Parse("false")&&!bool.TryParse("1",out bool flag)&&!flag);
        phase=7;
        Check(Convert.ToInt32(2.5)==2&&Convert.ToInt32(3.5)==4&&Convert.ToInt32(-3.5)==-4);
        Check(Convert.ToInt32((string?)null)==0&&Convert.ToInt32("42")==42);
        Check(Convert.ToUInt64(uint.MaxValue)==4294967295UL&&Convert.ToUInt64(ulong.MaxValue)==ulong.MaxValue&&Convert.ToInt64(long.MinValue)==long.MinValue);
        Check(Convert.ToByte(255)==255&&Convert.ToSByte(-128)==-128&&Convert.ToUInt16(65535)==65535);
        Check(Convert.ToChar(65)=='A'&&Convert.ToChar("Z")=='Z');
        Check(Convert.ToDouble(uint.MaxValue)==4294967295d&&Convert.ToDouble(ulong.MaxValue)>1.8e19);
        Check(Convert.ToSingle(16777217)==16777216&&Convert.ToBoolean(double.NaN)&&!Convert.ToBoolean((string?)null));
        try{Convert.ToInt32(double.NaN);Check(false);}catch(OverflowException){}
        try{Convert.ToInt64(9223372036854775808d);Check(false);}catch(OverflowException){}
        try{Convert.ToUInt64(18446744073709551616d);Check(false);}catch(OverflowException){}
        try{Convert.ToUInt32(-1);Check(false);}catch(OverflowException){}
        try{Convert.ToByte(256);Check(false);}catch(OverflowException){}
        phase=8;
        var random=new Random(1);int[] expected={534011718,237820880,1002897798,1657007234,1412011072};
        for(int n=0;n<expected.Length;n++)Check(random.Next()==expected[n]);
        var first=new Random(int.MinValue);var second=new Random(int.MaxValue);
        for(int n=0;n<100;n++)Check(first.Next()==second.Next());
        first=new Random(-123);second=new Random(123);var a=new byte[128];var b=new byte[128];first.NextBytes(a);second.NextBytes(b);
        for(int n=0;n<a.Length;n++)Check(a[n]==b[n]);
        Check(first.Next(0)==0&&first.Next(5,5)==5);
        for(int n=0;n<1000;n++){
            int small=first.Next(17),range=first.Next(-30,40),large=first.Next(int.MinValue,int.MaxValue);double unit=first.NextDouble();
            Check(small>=0&&small<17&&range>=-30&&range<40&&large<int.MaxValue&&unit>=0&&unit<1);
        }
        try{first.Next(-1);Check(false);}catch(ArgumentOutOfRangeException){}
        try{first.Next(2,1);Check(false);}catch(ArgumentOutOfRangeException){}
        try{first.NextBytes(null!);Check(false);}catch(ArgumentNullException){}
        var defaultA=new Random();var defaultB=new Random();Check(defaultA.Next()!=defaultB.Next());
    }
}
