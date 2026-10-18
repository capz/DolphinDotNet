using System;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

internal static partial class Program
{
    private static int phase,checks;
    private static int ReadChar(ref char value)=>value;
    private static int ReadByte(ref byte value)=>value;
    private static int ReadSByte(ref sbyte value)=>value;
    private class Label { public override string ToString()=>"label"; }
    private sealed class ChildLabel : Label {}
    private struct Point {public int X;public override string ToString()=>X.ToString("D2");}
    private sealed class Formatted : IFormattable {public override string ToString()=>"object";public string ToString(string? format,IFormatProvider? provider)=>"format/"+format;}

    private static void Check(bool condition){checks++;if(!condition)throw new Exception("String contract failed.");}
    private static int Main(){try{CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;Run();return 0;}catch(Exception){return phase*1000+checks;}}
    private static void Run()
    {
        phase=1;
        Check(string.Empty.Length==0&&string.IsNullOrEmpty(null)&&!string.IsNullOrEmpty("x"));
        Check(string.IsNullOrWhiteSpace("\u2003\t\r\n\u00a0")&&!string.IsNullOrWhiteSpace(" x "));
        Check("abc".Substring(1)=="bc"&&"abc".Substring(3)==""&&"abc".Substring(1,1)=="b");
        try{"abc".Substring(-1);Check(false);}catch(ArgumentOutOfRangeException){}
        Check(new string((char[]?)null!)=="");
        string? missing=null;try{missing!.Substring(0);Check(false);}catch(NullReferenceException){}
        Check(new string('x',3)=="xxx"&&new string(new[]{'a','\0','b'},1,2)=="\0b");
        Check(string.Concat("a",null,"b","c")=="abc"&&string.Concat(new string?[]{"a",null,"b"})=="ab");
        Check(string.Concat((object)12,(object)true)=="12True");
        Check("abc".Equals((object)"abc")&&!"abc".Equals((object)12));
        Check(string.Equals(null,null,StringComparison.Ordinal)&&!string.Equals(null,"",StringComparison.Ordinal));
        Check(string.Format("{0}/{1}/{2}",new Label(),new ChildLabel(),new Point{X=7})=="label/label/07");
        Check(string.Format("{0:tag}",new Formatted())=="format/tag");
        Check(string.Concat((object)new Formatted())=="object"&&new StringBuilder().Append((object)new Formatted()).ToString()=="object");
        try{string.Equals("a","a",(StringComparison)99);Check(false);}catch(ArgumentException){}
        Check("HELLO".Equals("hello",StringComparison.OrdinalIgnoreCase));
        phase=2;
        string text="abcABCabc";
        Check(text.StartsWith("ABC",StringComparison.OrdinalIgnoreCase)&&text.EndsWith("ABC",StringComparison.OrdinalIgnoreCase));
        Check(text.StartsWith('a')&&text.EndsWith('c')&&text.Contains('B'));
        Check(text.IndexOf('A')==3&&text.IndexOf("abc",1)==6&&text.IndexOf("abc",1,5)==-1);
        Check(text.IndexOf("ABC",1,StringComparison.OrdinalIgnoreCase)==3&&text.Contains("bCa",StringComparison.OrdinalIgnoreCase));
        Check(text.LastIndexOf('a')==6&&text.LastIndexOf("abc")==6&&text.LastIndexOf("ABC",StringComparison.OrdinalIgnoreCase)==6);
        Check(text.LastIndexOf('b',5,6)==1&&text.LastIndexOf("abc",5,6)==0&&text.IndexOf("",text.Length)==text.Length);
        Check(text.IndexOfAny(new[]{'Q','C'})==5&&text.IndexOfAny(new[]{'a'},1)==6);
        Check(!string.Equals("\u0131","I",StringComparison.OrdinalIgnoreCase));
        Check("\u00e9lan".StartsWith("\u00c9",StringComparison.OrdinalIgnoreCase));
        Check("x\U00010428y".IndexOf("\U00010400",StringComparison.OrdinalIgnoreCase)==1);
        try{text.IndexOf("x",-1);Check(false);}catch(ArgumentOutOfRangeException){}
        try{text.StartsWith((string)null!);Check(false);}catch(ArgumentNullException){}
        phase=3;
        Check("\u2003 hi \t".Trim()=="hi"&&"xxhellox".Trim('x')=="hello");
        Check("  x  ".TrimStart()=="x  "&&"  x  ".TrimEnd()=="  x");
        Check("ab".PadLeft(4,'0')=="00ab"&&"ab".PadRight(4)=="ab  ");
        Check("abc".Insert(1,"XY")=="aXYbc"&&"abc".Remove(1)=="a"&&"abc".Remove(1,1)=="ac");
        Check("banana".Replace('a','o')=="bonono"&&"aaaaa".Replace("aa","b")=="bba");
        Check("aBaB".Replace("ab","X",StringComparison.OrdinalIgnoreCase)=="XX");
        char[] chars="xyz".ToCharArray();"abcd".CopyTo(1,chars,0,2);Check(new string(chars)=="bcz");
        Check(string.Join("|",new string?[]{"a",null,"c"})=="a||c");
        Check(string.Join("-",new[]{"a","b","c"},1,2)=="b-c");
        string[] parts=" a,, b , ".Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        Check(parts.Length==2&&parts[0]=="a"&&parts[1]=="b");
        parts="a--b::c".Split(new[]{"--","::"},2,StringSplitOptions.None);Check(parts.Length==2&&parts[1]=="b::c");
        parts=" a\t b ".Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);Check(parts.Length==2&&parts[1]=="b");
        Check("\u00e9\U00010428i\u0131".ToUpperInvariant()=="\u00c9\U00010400I\u0131");
        Check("\u00c9\U00010400I\u0130".ToLowerInvariant()=="\u00e9\U00010428i\u0130");
        parts=",,a,b".Split(',',1,StringSplitOptions.RemoveEmptyEntries);Check(parts.Length==1&&parts[0]==",,a,b");
        var values=new List<int>();values.Add(1);values.Add(2);Check(string.Join("/",values)=="1/2"&&string.Concat(values)=="12");
        Check(string.Join("/",new[]{1,2,3})=="1/2/3"&&string.Concat(new[]{1,2,3})=="123");
        phase=4;
        char high='\uffff';byte octet=255;sbyte negative=-128;Check(ReadChar(ref high)==65535&&ReadByte(ref octet)==255&&ReadSByte(ref negative)==-128);

        Check(Convert.ToString(12.5,CultureInfo.InvariantCulture)=="12.5"&&Convert.ToString((object?)null)=="");
        Check(42.ToString("D3",CultureInfo.InvariantCulture)=="042"&&12.5.ToString(CultureInfo.InvariantCulture)=="12.5");
        Check(string.Format(CultureInfo.InvariantCulture,"{0:N1}",1234.5)=="1,234.5");
        Check(42.ToString("D0003")=="042"&&(-1).ToString("B")==new string('1',32));
        Check(42.ToString("D5")=="00042"&&(-42).ToString("D5")=="-00042");
        Check(int.MinValue.ToString()=="-2147483648"&&ulong.MaxValue.ToString()=="18446744073709551615");
        Check(((sbyte)-1).ToString("X")=="FF"&&(-1).ToString("x")=="ffffffff"&&long.MinValue.ToString("X")=="8000000000000000");
        Check(ulong.MaxValue.ToString("N0")=="18,446,744,073,709,551,615");
        Check(12345.ToString("G3")=="1.23E+04"&&12345.ToString("E2")=="1.23E+004");
        Check(12.5.ToString()=="12.5"&&12.5f.ToString()=="12.5"&&(-0.0).ToString()=="-0");
        Check(1.25.ToString("F2")=="1.25"&&1234.5.ToString("N2")=="1,234.50");
        Check(2.5.ToString("F0")=="2"&&3.5.ToString("F0")=="4"&&0.125.ToString("E2")=="1.25E-001");
        Check(double.NaN.ToString()=="NaN"&&double.PositiveInfinity.ToString()=="Infinity");
        double original=1.2345678901234567;Check(double.Parse(original.ToString("R"))==original);
        float single=1.2345678f;Check(float.Parse(single.ToString("R"))==single);
        try{42.ToString("Q");Check(false);}catch(FormatException){}
        phase=8;GoldenFormats();
        phase=5;
        var b=new StringBuilder(2);b.Append('a').Append("bc").Append(42).Append(true);Check(b.ToString()=="abc42True"&&b.Capacity>=b.Length);
        Check(new StringBuilder(1).Append("a").Equals(new StringBuilder(100).Append("a")));
        b.Clear().Append('x',3).Insert(1,"ab").Replace('x','y').Remove(0,1);Check(b.ToString()=="abyy");
        b.Length=6;Check(b[4]=='\0'&&b[5]=='\0');b[4]='z';Check(b.ToString(0,5)=="abyyz");
        b.Clear().Append("aaaaa").Replace("aa","b");Check(b.ToString()=="bba");
        b.Append(b);Check(b.ToString()=="bbabba");b.Clear().Append(new[]{'a','b','c'},1,2);Check(b.ToString()=="bc");
        chars=new char[2];b.CopyTo(0,chars,0,2);Check(new string(chars)=="bc");
        try{new StringBuilder(1,3).Append('x',4);Check(false);}catch(ArgumentOutOfRangeException){}
        phase=6;
        Check(string.Format("{{{0:D3}}} {1,-4}!",7,"x")=="{007} x   !");
        Check(string.Format("{1:X4}/{0:N1}/{2}",12.5,42,null)=="002A/12.5/");
        Check(string.Format("{0}/{1}/{2}/{3}",new object?[]{true,'Z',ulong.MaxValue,null})=="True/Z/18446744073709551615/");
        b.Clear().AppendFormat("<{0,5:F1}>",1.25);Check(b.ToString()=="<  1.2>");
        try{string.Format("{1}",0);Check(false);}catch(FormatException){}
        try{string.Format("{0",0);Check(false);}catch(FormatException){}
        try{string.Format("}",0);Check(false);}catch(FormatException){}
        phase=7;
        int number=7;double price=1.25;string name="fox";
        Check($"{name}: {number:D3} / {price,6:F2}"=="fox: 007 /   1.25");
        Check($"{{{number}}} {true} {'Z'}"=="{7} True Z");
        Check(string.Create(CultureInfo.InvariantCulture,$"{number:D3}:{price:F2}")=="007:1.25");
        b.Clear().Append($"{name}:{number:D3}/{price,5:F2}");Check(b.ToString()=="fox:007/ 1.25");
        b.Clear().Append(CultureInfo.InvariantCulture,$"{price:N2}");Check(b.ToString()=="1.25");
        Check(double.Parse(double.Epsilon.ToString("R")).ToString("R")=="5E-324"&&float.Parse(float.Epsilon.ToString("R")).ToString("R")=="1E-45");
        phase=9;Check(System.Threading.Tasks.Task.Run(()=>float.Epsilon.ToString("R")).Result=="1E-45");
        for(int i=0;i<100;i++){b.Clear().AppendFormat("{0:D3}-{1}",i,name);Check(b.Length==7);}
    }
}
