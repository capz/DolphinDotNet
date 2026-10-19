using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
internal static class TextNumberChecks
{
    internal static int CheckCount;
    private static void Check(bool value){CheckCount++;if(!value)throw new InvalidOperationException("Wishlist text/numeric contract failed.");}
    internal static void Run()
    {
        Check(Math.Abs(Math.Cbrt(27)-3)<1e-12&&Math.Log2(1024)==10);
        Check(Math.ILogB(0)==int.MinValue&&Math.ILogB(double.NaN)==int.MaxValue&&Math.ILogB(8)==3);
        Check(Math.BitIncrement(1)>1&&Math.BitDecrement(1)<1&&Math.CopySign(3,-0.0)==-3);
        Check(Math.ScaleB(1.5,3)==12&&Math.FusedMultiplyAdd(2,3,4)==10&&Math.Acosh(1)==0&&Math.Asinh(0)==0&&Math.Atanh(0)==0);
        Check(MathF.BitIncrement(1)>1&&MathF.BitDecrement(1)<1&&MathF.ScaleB(1.5f,3)==12&&MathF.FusedMultiplyAdd(2,3,4)==10);
        var random=new Random(4321);bool positive=false,negative=false;
        for(int i=0;i<128;i++){long v=random.NextInt64(long.MinValue,long.MaxValue);positive|=v>0;negative|=v<0;Check(random.NextInt64(7)>=0&&random.NextInt64(7)<7);float f=random.NextSingle();Check(f>=0&&f<1);}
        Check(positive&&negative&&random.NextInt64(4,4)==4);
        Check(byte.Parse("255")==255&&sbyte.Parse("-128")==-128&&short.Parse("-32768")==short.MinValue&&ushort.Parse("65535")==ushort.MaxValue);
        Check(!byte.TryParse("256",out byte bad)&&bad==0&&!short.TryParse("-32769",out _));
        int? nil=null,number=42;bool? yes=true;char? letter='Z';Check(nil.ToString()==""&&number.ToString()=="42"&&yes.ToString()=="True"&&letter.ToString()=="Z");
        var chars="AΩ".GetEnumerator();Check(chars.MoveNext()&&chars.Current=='A');var clone=(CharEnumerator)chars.Clone();Check(clone.MoveNext()&&clone.Current=='Ω');Check(chars.MoveNext()&&chars.Current=='Ω'&&!chars.MoveNext());
        chars.Reset();Check(chars.MoveNext()&&chars.Current=='A');
        Check(string.Compare("xAb",1,"ABz",0,2,StringComparison.OrdinalIgnoreCase)==0&&string.CompareOrdinal("a",0,"ab",0,8)<0);
        IEnumerable<int> numbers=new[]{1,2,3};Check(new StringBuilder().AppendJoin(':',numbers).ToString()=="1:2:3");
        FormattableString formatted=$"value={42:D4}";Check(formatted.Format=="value={0:D4}"&&formatted.ArgumentCount==1&&FormattableString.Invariant(formatted)=="value=0042");
        Check((-1234).ToString("C2",CultureInfo.InvariantCulture)=="(¤1,234.00)"&&(.125).ToString("P1",CultureInfo.InvariantCulture)=="12.5 %");
    }
}
