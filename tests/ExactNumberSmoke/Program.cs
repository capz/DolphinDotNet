using System;
using System.Globalization;
using System.Numerics;
internal static class Program
{
    private static int stage=1;
    private static int check;
    private static void Check(bool value){check++;if(!value)throw new InvalidOperationException("Exact number contract failed: "+check); }
    private static void Run()
    {
        stage=1;BigInteger n=BigInteger.Parse("1234567890123456789012345678901234567890");
        Check(n.ToString()=="1234567890123456789012345678901234567890");Check((n+9-n)==9);
        Check((n*17/17)==n);Check(n%17==BigInteger.Parse("14"));
        stage=2;var huge=BigInteger.One<<200;Check((huge>>200).IsOne&&huge.IsPowerOfTwo);
        Check((-huge>>210)==-1&&(-huge-1>>1)==-(huge>>1)-1);
        Check((~huge)==-huge-1&&((huge|15)&31)==15&&((huge^7)^7)==huge);
        Check(BigInteger.Pow(10,30).ToString()=="1000000000000000000000000000000");
        Check(BigInteger.GreatestCommonDivisor(n*5,n*7)==n);
        Check(BigInteger.ModPow(7,128,101)==97);
        stage=3;foreach(var value in new[]{BigInteger.Zero,BigInteger.One,new BigInteger(-1),new BigInteger(127),new BigInteger(128),new BigInteger(-128),new BigInteger(-129),huge,-huge}){
            var bytes=value.ToByteArray();Check(new BigInteger(bytes)==value);Check(value.Equals((object)value));
        }
        Check((long)new BigInteger(long.MinValue)==long.MinValue&&(ulong)new BigInteger(ulong.MaxValue)==ulong.MaxValue);
        Check((double)((BigInteger.One<<100)+(BigInteger.One<<47)+1)==Math.Pow(2,100));
        Check((float)((BigInteger.One<<50)+(BigInteger.One<<26)+1)>MathF.Pow(2,50));
        try{var bad=(long)huge;Check(false);}catch(OverflowException){}
        try{var bad=n/BigInteger.Zero;Check(false);}catch(DivideByZeroException){}
        stage=4;decimal tenth=.1m,fifth=.2m,oneValue=1m,three=3m;Check(tenth+fifth==.3m);Check(oneValue/three==.3333333333333333333333333333m);
        Check(decimal.MaxValue-1==79228162514264337593543950334m);
        Check(7922816251426433759354395033.5m*10==decimal.MaxValue);
        Check(decimal.Parse("123456789.0000000000000000001").ToString()=="123456789.0000000000000000001");
        Check(decimal.Round(2.5m)==2m&&decimal.Round(3.5m)==4m&&decimal.Round(-2.5m,MidpointRounding.AwayFromZero)==-3m);
        Check(decimal.Floor(-2.1m)==-3&&decimal.Ceiling(-2.1m)==-2&&decimal.Truncate(-2.9m)==-2);
        Check(decimal.Remainder(-7.25m,2m)==-1.25m);
        stage=5;Check(new decimal(decimal.GetBits(-1.2300m)).ToString()=="-1.2300");
        Check(1.00m.GetHashCode()==1m.GetHashCode());Check((-0.00m).GetHashCode()==0m.GetHashCode());
        Check((int)-12.9m==-12&&Convert.ToInt32(12.5m)==12&&Convert.ToInt32(13.5m)==14);
        Check((double).1m==.1&&new decimal(123.456)==123.456m);
        Check((decimal)new BigInteger(ulong.MaxValue)==18446744073709551615m&&new BigInteger(-12.9m)==-12);
        try{var maximum=decimal.MaxValue;var overflow=maximum+1m;Check(false);}catch(OverflowException){}
        try{var zero=0m;var overflow=1m/zero;Check(false);}catch(DivideByZeroException){}
        stage=6;Check((-1234.50m).ToString("C2",CultureInfo.InvariantCulture)=="(¤1,234.50)");
        Check((.125m).ToString("P1",CultureInfo.InvariantCulture)=="12.5 %");
        Check((.0012m).ToString("F5",CultureInfo.InvariantCulture)=="0.00120");
        GoldenNumbers.Run();DecimalStress.Run();
    }
    private static int Main(){try{Run();return 0;}catch(Exception error){Console.WriteLine(error.Message);return stage;}}
}
