using System;
namespace Dolphin.Numerics;

/// <summary>Immutable signed arbitrary-precision integer; little-endian 32-bit limbs.</summary>
public readonly struct BigInteger : IEquatable<BigInteger>, IComparable<BigInteger>, IComparable, Dolphin.Text.IFormattable
{
    private readonly uint[]? words;
    private readonly int sign;
    private int Length=>words==null?0:words.Length;
    internal BigInteger(int sign,uint[] words)
    {
        int n=words.Length;while(n>0&&words[n-1]==0)n--;
        if(n==0){this.words=null;this.sign=0;return;}
        if(n!=words.Length){var trim=new uint[n];Array.Copy(words,trim,n);words=trim;}
        this.words=words;this.sign=sign<0?-1:1;
    }
    public BigInteger(int value):this((long)value){}
    public BigInteger(uint value):this((ulong)value){}
    public BigInteger(long value){ulong magnitude=value<0?0UL-(ulong)value:(ulong)value;words=magnitude==0?null:magnitude<=uint.MaxValue?new[]{(uint)magnitude}:new[]{(uint)magnitude,(uint)(magnitude>>32)};sign=value<0?-1:value>0?1:0;}
    public BigInteger(ulong value){words=value==0?null:value<=uint.MaxValue?new[]{(uint)value}:new[]{(uint)value,(uint)(value>>32)};sign=value==0?0:1;}
    public BigInteger(byte[] value):this(value,false,false){}
    public BigInteger(byte[] value,bool isUnsigned=false,bool isBigEndian=false)
    {
        if(value==null)throw new ArgumentNullException("value");
        var data=new uint[(value.Length+3)/4];
        bool negative=!isUnsigned&&value.Length>0&&(value[isBigEndian?0:value.Length-1]&128)!=0;
        if(negative)for(int i=0;i<data.Length;i++)data[i]=uint.MaxValue;
        for(int i=0;i<value.Length;i++){int at=isBigEndian?value.Length-i-1:i,shift=(i%4)*8;data[i/4]=(data[i/4]&~(255U<<shift))|((uint)value[at]<<shift);}
        if(negative){ulong carry=1;for(int i=0;i<data.Length;i++){ulong n=(ulong)(~data[i])+carry;data[i]=(uint)n;carry=n>>32;}}
        var normalized=new BigInteger(negative?-1:1,data);words=normalized.words;sign=normalized.sign;
    }
    public BigInteger(double value)
    {
        if(double.IsNaN(value)||double.IsInfinity(value))throw new OverflowException();
        int s=value<0?-1:1;value=Math.Floor(Math.Abs(value));
        if(value<1){words=null;sign=0;return;}
        int n=(int)(Math.ILogB(value)/32)+1;var data=new uint[n];
        for(int i=n-1;i>=0;i--){double factor=Math.ScaleB(1,i*32);uint digit=(uint)(value/factor);data[i]=digit;value-=digit*factor;}
        var normalized=new BigInteger(s,data);words=normalized.words;sign=normalized.sign;
    }
    public BigInteger(Decimal value){var n=value.Integer();words=n.words;sign=n.sign;}
    public static explicit operator BigInteger(Decimal value)=>new BigInteger(value);
    public static explicit operator Decimal(BigInteger value){if(value.Length>3)throw new OverflowException();return new Decimal((int)value.Word(0),(int)value.Word(1),(int)value.Word(2),value.sign<0,0);}
    public BigInteger(float value):this((double)value){}
    public static BigInteger Zero=>default;
    public static BigInteger One=>new BigInteger(1);
    public static BigInteger MinusOne=>new BigInteger(-1);
    public int Sign=>sign;
    public bool IsZero=>sign==0;
    public bool IsOne=>sign==1&&Length==1&&words![0]==1;
    public bool IsEven=>sign==0||(words![0]&1)==0;
    public bool IsPowerOfTwo {get{if(sign<=0)return false;bool seen=false;for(int i=0;i<Length;i++){uint n=words![i];if(n!=0){if(seen||(n&(n-1))!=0)return false;seen=true;}}return true;}}
    internal uint Word(int index)=>index<Length?words![index]:0;
    private static int Magnitude(BigInteger a,BigInteger b){if(a.Length!=b.Length)return a.Length<b.Length?-1:1;for(int i=a.Length-1;i>=0;i--)if(a.words![i]!=b.words![i])return a.words[i]<b.words[i]?-1:1;return 0;}
    public int CompareTo(BigInteger other)=>sign!=other.sign?sign<other.sign?-1:1:sign*Magnitude(this,other);
    public int CompareTo(object? other){if(other==null)return 1;if(!(other is BigInteger))throw new ArgumentException("Expected BigInteger.");return CompareTo((BigInteger)other);}
    public bool Equals(BigInteger other)=>CompareTo(other)==0;
    public override bool Equals(object? other)=>other is BigInteger&&Equals((BigInteger)other);
    public override int GetHashCode(){int hash=sign;for(int i=0;i<Length;i++)hash=(hash*31)^(int)words![i];return hash;}
    public static BigInteger Abs(BigInteger value)=>value.sign>=0?value:new BigInteger(1,value.words!);
    public static BigInteger Negate(BigInteger value)=>-value;
    public static BigInteger operator -(BigInteger value)=>value.sign==0?value:new BigInteger(-value.sign,value.words!);
    public static BigInteger operator +(BigInteger value)=>value;
    public static BigInteger operator +(BigInteger a,BigInteger b)
    {
        if(a.sign==0)return b;if(b.sign==0)return a;
        if(a.sign!=b.sign){int c=Magnitude(a,b);return c==0?Zero:c>0?SubtractMagnitude(a,b,a.sign):SubtractMagnitude(b,a,b.sign);}
        int n=a.Length>b.Length?a.Length:b.Length;var result=new uint[n+1];ulong carry=0;
        for(int i=0;i<n;i++){ulong v=(ulong)a.Word(i)+b.Word(i)+carry;result[i]=(uint)v;carry=v>>32;}result[n]=(uint)carry;return new BigInteger(a.sign,result);
    }
    private static BigInteger SubtractMagnitude(BigInteger a,BigInteger b,int sign)
    {
        var result=new uint[a.Length];ulong borrow=0;
        for(int i=0;i<a.Length;i++){ulong n=(ulong)a.words![i]-b.Word(i)-borrow;result[i]=(uint)n;borrow=n>>63;}return new BigInteger(sign,result);
    }
    public static BigInteger operator -(BigInteger a,BigInteger b)=>a+-b;
    public static BigInteger operator *(BigInteger a,BigInteger b)
    {
        if(a.sign==0||b.sign==0)return Zero;var result=new uint[a.Length+b.Length];
        for(int i=0;i<a.Length;i++){ulong carry=0;for(int j=0;j<b.Length;j++){ulong n=(ulong)a.words![i]*b.words![j]+result[i+j]+carry;result[i+j]=(uint)n;carry=n>>32;}result[i+b.Length]=(uint)carry;}return new BigInteger(a.sign*b.sign,result);
    }
    public static BigInteger DivRem(BigInteger dividend,BigInteger divisor,out BigInteger remainder)
    {
        if(divisor.sign==0)throw new DivideByZeroException();
        if(Magnitude(dividend,divisor)<0){remainder=dividend;return Zero;}
        var quotient=new uint[dividend.Length];var rem=new uint[divisor.Length+1];int rn=0;
        for(int bit=dividend.Length*32-1;bit>=0;bit--){
            uint carry=(dividend.words![bit/32]>>(bit%32))&1;
            for(int i=0;i<rn;i++){uint next=rem[i]>>31;rem[i]=(rem[i]<<1)|carry;carry=next;}if(carry!=0)rem[rn++]=carry;
            int cmp=rn<divisor.Length?-1:rn>divisor.Length?1:0;if(cmp==0)for(int i=rn-1;i>=0;i--)if(rem[i]!=divisor.words![i]){cmp=rem[i]<divisor.words[i]?-1:1;break;}
            if(cmp>=0){ulong borrow=0;for(int i=0;i<rn;i++){ulong n=(ulong)rem[i]-divisor.Word(i)-borrow;rem[i]=(uint)n;borrow=n>>63;}while(rn>0&&rem[rn-1]==0)rn--;quotient[bit/32]|=1U<<(bit%32);}
        }
        remainder=new BigInteger(dividend.sign,rem);return new BigInteger(dividend.sign*divisor.sign,quotient);
    }
    public static BigInteger operator /(BigInteger a,BigInteger b)=>DivRem(a,b,out _);
    public static BigInteger operator %(BigInteger a,BigInteger b){DivRem(a,b,out var remainder);return remainder;}
    public static BigInteger operator ++(BigInteger value)=>value+One;
    public static BigInteger operator --(BigInteger value)=>value-One;
    public static bool operator ==(BigInteger a,BigInteger b)=>a.Equals(b);
    public static bool operator !=(BigInteger a,BigInteger b)=>!a.Equals(b);
    public static bool operator <(BigInteger a,BigInteger b)=>a.CompareTo(b)<0;
    public static bool operator >(BigInteger a,BigInteger b)=>a.CompareTo(b)>0;
    public static bool operator <=(BigInteger a,BigInteger b)=>a.CompareTo(b)<=0;
    public static bool operator >=(BigInteger a,BigInteger b)=>a.CompareTo(b)>=0;
    public static int Compare(BigInteger a,BigInteger b)=>a.CompareTo(b);
    public static BigInteger Min(BigInteger a,BigInteger b)=>a<b?a:b;
    public static BigInteger Max(BigInteger a,BigInteger b)=>a>b?a:b;
    public static BigInteger Add(BigInteger a,BigInteger b)=>a+b;
    public static BigInteger Subtract(BigInteger a,BigInteger b)=>a-b;
    public static BigInteger Multiply(BigInteger a,BigInteger b)=>a*b;
    public static BigInteger Divide(BigInteger a,BigInteger b)=>a/b;
    public static BigInteger Remainder(BigInteger a,BigInteger b)=>a%b;
    public static BigInteger Pow(BigInteger value,int exponent){if(exponent<0)throw new ArgumentOutOfRangeException("exponent");BigInteger result=One;while(exponent!=0){if((exponent&1)!=0)result*=value;exponent>>=1;if(exponent!=0)value*=value;}return result;}
    public static BigInteger GreatestCommonDivisor(BigInteger a,BigInteger b){a=Abs(a);b=Abs(b);while(!b.IsZero){BigInteger n=a%b;a=b;b=n;}return a;}
    public static BigInteger ModPow(BigInteger value,BigInteger exponent,BigInteger modulus){if(exponent.sign<0)throw new ArgumentOutOfRangeException("exponent");BigInteger result=One%modulus;value%=modulus;while(!exponent.IsZero){if(!exponent.IsEven)result=result*value%modulus;exponent>>=1;if(!exponent.IsZero)value=value*value%modulus;}return result;}
    public static BigInteger operator <<(BigInteger value,int shift)
    {
        if(shift==int.MinValue)throw new ArgumentOutOfRangeException("shift");if(shift<0)return value>>-shift;if(value.sign==0)return value;
        int extra=shift/32,bits=shift%32;if(extra>int.MaxValue-value.Length-1)throw new OutOfMemoryException();var data=new uint[value.Length+extra+1];ulong carry=0;
        for(int i=0;i<value.Length;i++){ulong n=((ulong)value.words![i]<<bits)|carry;data[i+extra]=(uint)n;carry=n>>32;}data[value.Length+extra]=(uint)carry;return new BigInteger(value.sign,data);
    }
    public static BigInteger operator >>(BigInteger value,int shift)
    {
        if(shift==int.MinValue)throw new ArgumentOutOfRangeException("shift");if(shift<0)return value<<-shift;if(value.sign==0)return value;
        int skip=shift/32,bits=shift%32;if(skip>=value.Length)return value.sign<0?MinusOne:Zero;var data=new uint[value.Length-skip];
        for(int i=0;i<data.Length;i++)data[i]=bits==0?value.words![i+skip]:(value.words![i+skip]>>bits)|(value.Word(i+skip+1)<<(32-bits));
        bool lost=false;for(int i=0;i<skip;i++)lost|=value.words![i]!=0;if(bits!=0)lost|=(value.words![skip]&((1U<<bits)-1))!=0;
        var result=new BigInteger(value.sign,data);return value.sign<0&&lost?result-One:result;
    }
    private static uint[] Twos(BigInteger value,int length){var result=new uint[length];for(int i=0;i<value.Length;i++)result[i]=value.words![i];if(value.sign<0){ulong carry=1;for(int i=0;i<length;i++){ulong n=(ulong)(~result[i])+carry;result[i]=(uint)n;carry=n>>32;}}return result;}
    private static BigInteger FromTwos(uint[] data){if((data[data.Length-1]&0x80000000)==0)return new BigInteger(1,data);ulong carry=1;for(int i=0;i<data.Length;i++){ulong n=(ulong)(~data[i])+carry;data[i]=(uint)n;carry=n>>32;}return new BigInteger(-1,data);}
    private static BigInteger Bits(BigInteger a,BigInteger b,int op){int n=(a.Length>b.Length?a.Length:b.Length)+1;var x=Twos(a,n);var y=Twos(b,n);for(int i=0;i<n;i++)x[i]=op==0?x[i]&y[i]:op==1?x[i]|y[i]:x[i]^y[i];return FromTwos(x);}
    public static BigInteger operator &(BigInteger a,BigInteger b)=>Bits(a,b,0);
    public static BigInteger operator |(BigInteger a,BigInteger b)=>Bits(a,b,1);
    public static BigInteger operator ^(BigInteger a,BigInteger b)=>Bits(a,b,2);
    public static BigInteger operator ~(BigInteger value)=>-value-One;
    public byte[] ToByteArray()=>ToByteArray(false,false);
    public byte[] ToByteArray(bool isUnsigned=false,bool isBigEndian=false)
    {
        if(isUnsigned&&sign<0)throw new OverflowException();if(sign==0)return new byte[]{0};var data=Twos(this,Length+1);int n=data.Length*4;
        byte fill=sign<0?(byte)255:(byte)0;while(n>1){byte top=(byte)(data[(n-1)/4]>>(((n-1)%4)*8)),below=(byte)(data[(n-2)/4]>>(((n-2)%4)*8));if(top!=fill||!isUnsigned&&((below&128)!=0)!=(sign<0))break;n--;}
        var result=new byte[n];for(int i=0;i<n;i++)result[isBigEndian?n-i-1:i]=(byte)(data[i/4]>>((i%4)*8));return result;
    }
    public int GetByteCount(bool isUnsigned=false)=>ToByteArray(isUnsigned).Length;
    public override string ToString()=>ToString(null,null);
    public string ToString(IFormatProvider? provider)=>ToString(null,provider);
    public string ToString(string? format)=>ToString(format,null);
    public string ToString(string? format,IFormatProvider? provider)
    {
        Dolphin.Text.Formatter.Provider(provider);char code=format==null||format.Length==0?'G':format[0];int precision=0;
        if(code!='G'&&code!='g'&&code!='D'&&code!='d')throw new FormatException("BigInteger supports decimal G/D formats.");
        if(format!=null)for(int i=1;i<format.Length;i++){if(format[i]<'0'||format[i]>'9'||precision>9999)throw new FormatException();precision=precision*10+format[i]-'0';}
        var copy=new uint[Length];if(words!=null)Array.Copy(words,copy,Length);int n=Length;var chars=new char[Length*10+1];int used=0;
        while(n>0){ulong rem=0;for(int i=n-1;i>=0;i--){ulong value=(rem<<32)|copy[i];copy[i]=(uint)(value/10);rem=value%10;}chars[used++]=(char)('0'+rem);while(n>0&&copy[n-1]==0)n--;}
        if(used==0)chars[used++]='0';int digits=precision>used?precision:used;var output=new char[digits+(sign<0?1:0)];int at=0;if(sign<0)output[at++]='-';for(int i=used;i<digits;i++)output[at++]='0';while(used>0)output[at++]=chars[--used];return new string(output);
    }
    public static BigInteger Parse(string value)=>Parse(value,null);
    public static BigInteger Parse(string value,IFormatProvider? provider){Dolphin.Text.Formatter.Provider(provider);if(value==null)throw new ArgumentNullException("value");if(!TryParse(value,out BigInteger result))throw new FormatException();return result;}
    public static bool TryParse(string? value,out BigInteger result)
    {
        result=Zero;if(value==null)return false;int start=0,end=value.Length;while(start<end&&Space(value[start]))start++;while(end>start&&Space(value[end-1]))end--;bool negative=start<end&&value[start]=='-';if(start<end&&(value[start]=='-'||value[start]=='+'))start++;if(start==end)return false;
        BigInteger number=Zero;for(int i=start;i<end;i++){char digit=value[i];if(digit<'0'||digit>'9')return false;number=number*10+(digit-'0');}result=negative?-number:number;return true;
    }
    private static bool Space(char c)=>c==' '||c>=9&&c<=13;
    public static implicit operator BigInteger(int value)=>new BigInteger(value);
    public static implicit operator BigInteger(uint value)=>new BigInteger(value);
    public static implicit operator BigInteger(long value)=>new BigInteger(value);
    public static implicit operator BigInteger(ulong value)=>new BigInteger(value);
    public static implicit operator BigInteger(byte value)=>new BigInteger((uint)value);
    public static implicit operator BigInteger(sbyte value)=>new BigInteger((int)value);
    public static implicit operator BigInteger(short value)=>new BigInteger((int)value);
    public static implicit operator BigInteger(ushort value)=>new BigInteger((uint)value);
    public static explicit operator BigInteger(double value)=>new BigInteger(value);
    public static explicit operator BigInteger(float value)=>new BigInteger(value);
    private ulong Unsigned(){if(sign<0||Length>2)throw new OverflowException();return Word(0)|((ulong)Word(1)<<32);}
    private long Signed(){if(Length>2)throw new OverflowException();ulong magnitude=Word(0)|((ulong)Word(1)<<32);if(sign>=0){if(magnitude>long.MaxValue)throw new OverflowException();return (long)magnitude;}if(magnitude>0x8000000000000000UL)throw new OverflowException();return (long)(0UL-magnitude);}
    public static explicit operator long(BigInteger value)=>value.Signed();
    public static explicit operator ulong(BigInteger value)=>value.Unsigned();
    public static explicit operator int(BigInteger value){long n=value.Signed();if(n<int.MinValue||n>int.MaxValue)throw new OverflowException();return (int)n;}
    public static explicit operator uint(BigInteger value){ulong n=value.Unsigned();if(n>uint.MaxValue)throw new OverflowException();return (uint)n;}
    public static explicit operator short(BigInteger value){int n=(int)value;if(n<short.MinValue||n>short.MaxValue)throw new OverflowException();return (short)n;}
    public static explicit operator ushort(BigInteger value){uint n=(uint)value;if(n>ushort.MaxValue)throw new OverflowException();return (ushort)n;}
    public static explicit operator byte(BigInteger value){uint n=(uint)value;if(n>byte.MaxValue)throw new OverflowException();return (byte)n;}
    public static explicit operator sbyte(BigInteger value){int n=(int)value;if(n<sbyte.MinValue||n>sbyte.MaxValue)throw new OverflowException();return (sbyte)n;}
    private double Floating(int precision)
    {
        if(sign==0)return 0;uint leading=words![Length-1];int topBits=0;while(leading!=0){leading>>=1;topBits++;}
        int bits=(Length-1)*32+topBits;if(bits>(precision==24?128:1024))return sign<0?double.NegativeInfinity:double.PositiveInfinity;
        int shift=bits>precision?bits-precision:0;ulong top=0;for(int i=bits-1;i>=shift;i--)top=(top<<1)|((Word(i/32)>>(i%32))&1);
        // .NET 8's BigInteger-to-double contract discards low mantissa bits;
        // Single conversion then narrows that double result.
        double n=Math.ScaleB((double)top,shift);return sign<0?-n:n;
    }
    public static explicit operator double(BigInteger value)=>value.Floating(53);
    public static explicit operator float(BigInteger value)=>(float)value.Floating(53);
    public static double Log(BigInteger value)=>Log(value,2.718281828459045);
    public static double Log10(BigInteger value)=>Log(value,10);
    public static double Log(BigInteger value,double baseValue)
    {
        if(value.sign<0)return double.NaN;if(value.IsZero)return Math.Log(0,baseValue);
        int n=value.Length;double top=value.words![n-1];if(n>1)top+=value.words[n-2]/4294967296.0;
        return (Math.Log(top)+(n-1)*32*Math.Log(2))/Math.Log(baseValue);
    }
    public bool Equals(long value)=>Equals(new BigInteger(value));
    public int CompareTo(long value)=>CompareTo(new BigInteger(value));
    public static BigInteger operator +(BigInteger a,long b)=>a+new BigInteger(b);
    public static BigInteger operator +(long a,BigInteger b)=>new BigInteger(a)+b;
    public static BigInteger operator -(BigInteger a,long b)=>a-new BigInteger(b);
    public static BigInteger operator -(long a,BigInteger b)=>new BigInteger(a)-b;
    public static BigInteger operator *(BigInteger a,long b)=>a*new BigInteger(b);
    public static BigInteger operator *(long a,BigInteger b)=>new BigInteger(a)*b;
    public static BigInteger operator /(BigInteger a,long b)=>a/new BigInteger(b);
    public static BigInteger operator /(long a,BigInteger b)=>new BigInteger(a)/b;
    public static BigInteger operator %(BigInteger a,long b)=>a%new BigInteger(b);
    public static BigInteger operator %(long a,BigInteger b)=>new BigInteger(a)%b;
    public static BigInteger operator &(BigInteger a,long b)=>a&new BigInteger(b);
    public static BigInteger operator &(long a,BigInteger b)=>new BigInteger(a)&b;
    public static BigInteger operator |(BigInteger a,long b)=>a|new BigInteger(b);
    public static BigInteger operator |(long a,BigInteger b)=>new BigInteger(a)|b;
    public static BigInteger operator ^(BigInteger a,long b)=>a^new BigInteger(b);
    public static BigInteger operator ^(long a,BigInteger b)=>new BigInteger(a)^b;
    public static bool operator ==(BigInteger a,long b)=>a==new BigInteger(b);
    public static bool operator ==(long a,BigInteger b)=>new BigInteger(a)==b;
    public static bool operator !=(BigInteger a,long b)=>a!=new BigInteger(b);
    public static bool operator !=(long a,BigInteger b)=>new BigInteger(a)!=b;
    public static bool operator <(BigInteger a,long b)=>a<new BigInteger(b);
    public static bool operator <(long a,BigInteger b)=>new BigInteger(a)<b;
    public static bool operator >(BigInteger a,long b)=>a>new BigInteger(b);
    public static bool operator >(long a,BigInteger b)=>new BigInteger(a)>b;
    public static bool operator <=(BigInteger a,long b)=>a<=new BigInteger(b);
    public static bool operator <=(long a,BigInteger b)=>new BigInteger(a)<=b;
    public static bool operator >=(BigInteger a,long b)=>a>=new BigInteger(b);
    public static bool operator >=(long a,BigInteger b)=>new BigInteger(a)>=b;
    public bool Equals(ulong value)=>Equals(new BigInteger(value));
    public int CompareTo(ulong value)=>CompareTo(new BigInteger(value));
    public static BigInteger operator +(BigInteger a,ulong b)=>a+new BigInteger(b);
    public static BigInteger operator +(ulong a,BigInteger b)=>new BigInteger(a)+b;
    public static BigInteger operator -(BigInteger a,ulong b)=>a-new BigInteger(b);
    public static BigInteger operator -(ulong a,BigInteger b)=>new BigInteger(a)-b;
    public static BigInteger operator *(BigInteger a,ulong b)=>a*new BigInteger(b);
    public static BigInteger operator *(ulong a,BigInteger b)=>new BigInteger(a)*b;
    public static BigInteger operator /(BigInteger a,ulong b)=>a/new BigInteger(b);
    public static BigInteger operator /(ulong a,BigInteger b)=>new BigInteger(a)/b;
    public static BigInteger operator %(BigInteger a,ulong b)=>a%new BigInteger(b);
    public static BigInteger operator %(ulong a,BigInteger b)=>new BigInteger(a)%b;
    public static BigInteger operator &(BigInteger a,ulong b)=>a&new BigInteger(b);
    public static BigInteger operator &(ulong a,BigInteger b)=>new BigInteger(a)&b;
    public static BigInteger operator |(BigInteger a,ulong b)=>a|new BigInteger(b);
    public static BigInteger operator |(ulong a,BigInteger b)=>new BigInteger(a)|b;
    public static BigInteger operator ^(BigInteger a,ulong b)=>a^new BigInteger(b);
    public static BigInteger operator ^(ulong a,BigInteger b)=>new BigInteger(a)^b;
    public static bool operator ==(BigInteger a,ulong b)=>a==new BigInteger(b);
    public static bool operator ==(ulong a,BigInteger b)=>new BigInteger(a)==b;
    public static bool operator !=(BigInteger a,ulong b)=>a!=new BigInteger(b);
    public static bool operator !=(ulong a,BigInteger b)=>new BigInteger(a)!=b;
    public static bool operator <(BigInteger a,ulong b)=>a<new BigInteger(b);
    public static bool operator <(ulong a,BigInteger b)=>new BigInteger(a)<b;
    public static bool operator >(BigInteger a,ulong b)=>a>new BigInteger(b);
    public static bool operator >(ulong a,BigInteger b)=>new BigInteger(a)>b;
    public static bool operator <=(BigInteger a,ulong b)=>a<=new BigInteger(b);
    public static bool operator <=(ulong a,BigInteger b)=>new BigInteger(a)<=b;
    public static bool operator >=(BigInteger a,ulong b)=>a>=new BigInteger(b);
    public static bool operator >=(ulong a,BigInteger b)=>new BigInteger(a)>=b;
}
