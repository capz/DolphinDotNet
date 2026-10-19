using System;
namespace Dolphin.Numerics;

/// <summary>Exact 96-bit decimal coefficient, sign and scale 0–28.</summary>
public readonly struct Decimal : IEquatable<Decimal>, IComparable<Decimal>, IComparable, Dolphin.Text.IFormattable
{
    private readonly uint lo,mid,hi;
    private readonly int flags;
    internal int Scale=>(flags>>16)&255;
    private bool Negative=>flags<0;
    internal BigInteger Coefficient=>new BigInteger(1,new[]{lo,mid,hi});
    private static BigInteger Limit=>new BigInteger(1,new[]{uint.MaxValue,uint.MaxValue,uint.MaxValue});
    public static readonly Decimal Zero=new Decimal(0);
    public static readonly Decimal One=new Decimal(1);
    public static readonly Decimal MinusOne=new Decimal(-1);
    public static readonly Decimal MaxValue=new Decimal(-1,-1,-1,false,0);
    public static readonly Decimal MinValue=new Decimal(-1,-1,-1,true,0);
    public Decimal(int value):this((long)value){}
    public Decimal(uint value):this((ulong)value){}
    public Decimal(long value){ulong n=value<0?0UL-(ulong)value:(ulong)value;lo=(uint)n;mid=(uint)(n>>32);hi=0;flags=value<0?int.MinValue:0;}
    public Decimal(ulong value){lo=(uint)value;mid=(uint)(value>>32);hi=0;flags=0;}
    public Decimal(int lo,int mid,int hi,bool isNegative,byte scale){if(scale>28)throw new ArgumentOutOfRangeException("scale");this.lo=(uint)lo;this.mid=(uint)mid;this.hi=(uint)hi;flags=(scale<<16)|(isNegative?int.MinValue:0);}
    public Decimal(int[] bits){if(bits==null)throw new ArgumentNullException("bits");if(bits.Length!=4||(bits[3]&0x7f00ffff)!=0||((bits[3]>>16)&255)>28)throw new ArgumentException("Invalid decimal bits.");lo=(uint)bits[0];mid=(uint)bits[1];hi=(uint)bits[2];flags=bits[3];}
    public Decimal(double value){if(double.IsNaN(value)||double.IsInfinity(value))throw new OverflowException();var parsed=ParseCore(value.ToString("G15"),true);lo=parsed.lo;mid=parsed.mid;hi=parsed.hi;flags=parsed.flags;}
    public Decimal(float value){if(float.IsNaN(value)||float.IsInfinity(value))throw new OverflowException();var parsed=ParseCore(value.ToString("G7"),true);lo=parsed.lo;mid=parsed.mid;hi=parsed.hi;flags=parsed.flags;}
    public static int[] GetBits(Decimal value)=>new[]{(int)value.lo,(int)value.mid,(int)value.hi,value.flags};
    private static BigInteger Power(int scale)=>BigInteger.Pow(10,scale);
    private static BigInteger Rounded(BigInteger value,BigInteger divisor,MidpointRounding mode,bool negative)
    {
        BigInteger q=BigInteger.DivRem(value,divisor,out var rem);bool up=false;
        if(!rem.IsZero){if(mode==MidpointRounding.AwayFromZero)up=rem*2>=divisor;else if(mode==MidpointRounding.ToEven)up=rem*2>divisor||rem*2==divisor&&!q.IsEven;else if(mode==MidpointRounding.ToNegativeInfinity)up=negative;else if(mode==MidpointRounding.ToPositiveInfinity)up=!negative;}
        return up?q+1:q;
    }
    private static Decimal Pack(BigInteger coefficient,int scale,bool negative)
    {
        if(coefficient.IsZero){if(scale<0)scale=0;if(scale>28)scale=28;return new Decimal(0,0,0,negative,(byte)scale);}
        if(scale<0){if(scale< -30)throw new OverflowException();coefficient*=Power(-scale);scale=0;}
        int drop=scale>28?scale-28:0;
        while(true){BigInteger q=drop==0?coefficient:Rounded(coefficient,Power(drop),MidpointRounding.ToEven,negative);if(q<=Limit)return new Decimal((int)q.Word(0),(int)q.Word(1),(int)q.Word(2),negative,(byte)(scale-drop));if(drop>=scale)throw new OverflowException();drop++;}
    }
    internal BigInteger Integer(){BigInteger n=Coefficient/Power(Scale);return Negative?-n:n;}
    public static Decimal operator +(Decimal a,Decimal b)
    {
        int scale=a.Scale>b.Scale?a.Scale:b.Scale;BigInteger x=a.Coefficient*Power(scale-a.Scale),y=b.Coefficient*Power(scale-b.Scale);
        if(a.Negative)x=-x;if(b.Negative)y=-y;BigInteger sum=x+y;return Pack(BigInteger.Abs(sum),scale,sum.Sign<0);
    }
    public static Decimal operator -(Decimal a,Decimal b)=>a+-b;
    public static Decimal operator -(Decimal value)=>new Decimal((int)value.lo,(int)value.mid,(int)value.hi,!value.Negative,(byte)value.Scale);
    public static Decimal operator +(Decimal value)=>value;
    public static Decimal operator *(Decimal a,Decimal b)=>Pack(a.Coefficient*b.Coefficient,a.Scale+b.Scale,a.Negative!=b.Negative);
    public static Decimal operator /(Decimal a,Decimal b)
    {
        if(b.Coefficient.IsZero)throw new DivideByZeroException();bool negative=a.Negative!=b.Negative;int preferred=a.Scale-b.Scale;if(preferred<0)preferred=0;
        for(int scale=28;scale>=0;scale--){int factor=scale+b.Scale-a.Scale;BigInteger numerator=a.Coefficient,denominator=b.Coefficient;if(factor>=0)numerator*=Power(factor);else denominator*=Power(-factor);
            BigInteger q=Rounded(numerator,denominator,MidpointRounding.ToEven,negative);if(q>Limit)continue;
            while(scale>preferred){BigInteger n=BigInteger.DivRem(q,10,out var r);if(!r.IsZero)break;q=n;scale--;}
            return Pack(q,scale,negative);
        }
        throw new OverflowException();
    }
    public static Decimal operator %(Decimal a,Decimal b){int scale=a.Scale>b.Scale?a.Scale:b.Scale;return Pack((a.Coefficient*Power(scale-a.Scale))%(b.Coefficient*Power(scale-b.Scale)),scale,a.Negative);}
    public static Decimal operator ++(Decimal value)=>value+One;
    public static Decimal operator --(Decimal value)=>value-One;
    public int CompareTo(Decimal other){int scale=Scale>other.Scale?Scale:other.Scale;BigInteger a=Coefficient*Power(scale-Scale),b=other.Coefficient*Power(scale-other.Scale);if(Negative)a=-a;if(other.Negative)b=-b;return a.CompareTo(b);}
    public int CompareTo(object? value){if(value==null)return 1;if(!(value is Decimal))throw new ArgumentException("Expected Decimal.");return CompareTo((Decimal)value);}
    public bool Equals(Decimal other)=>CompareTo(other)==0;
    public override bool Equals(object? other)=>other is Decimal&&Equals((Decimal)other);
    public override int GetHashCode(){BigInteger n=Coefficient;int scale=Scale;while(scale>0){BigInteger q=BigInteger.DivRem(n,10,out var r);if(!r.IsZero)break;n=q;scale--;}return n.IsZero?0:n.GetHashCode()^(Negative?int.MinValue:0)^scale;}
    public static bool operator ==(Decimal a,Decimal b)=>a.Equals(b);
    public static bool operator !=(Decimal a,Decimal b)=>!a.Equals(b);
    public static bool operator <(Decimal a,Decimal b)=>a.CompareTo(b)<0;
    public static bool operator >(Decimal a,Decimal b)=>a.CompareTo(b)>0;
    public static bool operator <=(Decimal a,Decimal b)=>a.CompareTo(b)<=0;
    public static bool operator >=(Decimal a,Decimal b)=>a.CompareTo(b)>=0;
    public static int Compare(Decimal a,Decimal b)=>a.CompareTo(b);
    public static bool Equals(Decimal a,Decimal b)=>a.Equals(b);
    public static Decimal Add(Decimal a,Decimal b)=>a+b;
    public static Decimal Subtract(Decimal a,Decimal b)=>a-b;
    public static Decimal Multiply(Decimal a,Decimal b)=>a*b;
    public static Decimal Divide(Decimal a,Decimal b)=>a/b;
    public static Decimal Remainder(Decimal a,Decimal b)=>a%b;
    public static Decimal Negate(Decimal value)=>-value;
    public static Decimal Abs(Decimal value)=>value.Negative?-value:value;
    public static Decimal Min(Decimal a,Decimal b)=>a<b?a:b;
    public static Decimal Max(Decimal a,Decimal b)=>a>b?a:b;
    public static int Sign(Decimal value)=>value.Coefficient.IsZero?0:value.Negative?-1:1;
    public static Decimal Round(Decimal value)=>Round(value,0,MidpointRounding.ToEven);
    public static Decimal Round(Decimal value,int decimals)=>Round(value,decimals,MidpointRounding.ToEven);
    public static Decimal Round(Decimal value,MidpointRounding mode)=>Round(value,0,mode);
    public static Decimal Round(Decimal value,int decimals,MidpointRounding mode)
    {
        if(decimals<0||decimals>28)throw new ArgumentOutOfRangeException("decimals");if(mode<0||(int)mode>4)throw new ArgumentException("Invalid midpoint rounding.");if(value.Scale<=decimals)return value;
        return Pack(Rounded(value.Coefficient,Power(value.Scale-decimals),mode,value.Negative),decimals,value.Negative);
    }
    public static Decimal Truncate(Decimal value)=>Round(value,0,MidpointRounding.ToZero);
    public static Decimal Floor(Decimal value)=>Round(value,0,MidpointRounding.ToNegativeInfinity);
    public static Decimal Ceiling(Decimal value)=>Round(value,0,MidpointRounding.ToPositiveInfinity);
    public override string ToString()=>ToString(null,null);
    public string ToString(IFormatProvider? provider)=>ToString(null,provider);
    public string ToString(string? format)=>ToString(format,null);
    public string ToString(string? format,IFormatProvider? provider)
    {
        Dolphin.Text.Formatter.Provider(provider);char code=format==null||format.Length==0?'G':format[0];if(code>='a'&&code<='z')code=(char)(code-32);int precision=-1;
        if(format!=null&&format.Length>1){precision=0;for(int i=1;i<format.Length;i++){if(format[i]<'0'||format[i]>'9'||precision>9)throw new FormatException();precision=precision*10+format[i]-'0';}if(precision>99)throw new FormatException();}
        if(code!='G'&&code!='F'&&code!='N'&&code!='C'&&code!='P')throw new FormatException("Decimal supports G/F/N/C/P formats.");
        int scale=Scale;BigInteger coefficient=Coefficient;bool negative=Negative&&!coefficient.IsZero;
        if(code=='P'){if(scale>=2)scale-=2;else{coefficient*=Power(2-scale);scale=0;}}
        if(code!='G'){
            if(precision<0)precision=2;
            if(scale>precision)coefficient=Rounded(coefficient,Power(scale-precision),MidpointRounding.AwayFromZero,negative);
            else if(scale<precision)coefficient*=Power(precision-scale);scale=precision;
        }else if(precision>0)throw new FormatException("Decimal general precision is not implemented.");
        string digits=coefficient.ToString();int zeros=scale-digits.Length+1;var b=new Dolphin.Text.StringBuilder();
        if(negative&&code!='C')b.Append('-');if(code=='C'){if(negative)b.Append('(');b.Append('¤');}
        int width=digits.Length>scale?digits.Length-scale:1;
        for(int i=0;i<width+scale;i++){
            if(i==width&&scale!=0)b.Append('.');int at=i-zeros;char c=zeros>0?(at<0?'0':digits[at]):digits[i];b.Append(c);
            if((code=='N'||code=='C'||code=='P')&&i<width-1&&(width-i-1)%3==0)b.Append(',');
        }
        if(code=='C'&&negative)b.Append(')');if(code=='P')b.Append(" %");return b.ToString();
    }
    public static Decimal Parse(string value)=>Parse(value,null);
    public static Decimal Parse(string value,IFormatProvider? provider){Dolphin.Text.Formatter.Provider(provider);return ParseCore(value,false);}
    public static bool TryParse(string? value,out Decimal result){try{result=Parse(value!);return true;}catch(ArgumentNullException){}catch(FormatException){}catch(OverflowException){}result=Zero;return false;}
    private static Decimal ParseCore(string value,bool exponentAllowed)
    {
        if(value==null)throw new ArgumentNullException("value");int start=0,end=value.Length;while(start<end&&Space(value[start]))start++;while(end>start&&Space(value[end-1]))end--;
        bool negative=false;if(start<end&&(value[start]=='+'||value[start]=='-'))negative=value[start++]=='-';
        if(start<end&&(value[end-1]=='+'||value[end-1]=='-')){if(start>0&&(value[start-1]=='+'||value[start-1]=='-'))throw new FormatException();negative=value[--end]=='-';}
        bool point=false,seen=false;int scale=0,at=start;BigInteger coefficient=BigInteger.Zero;
        for(;at<end;at++){char c=value[at];if(c>='0'&&c<='9'){coefficient=coefficient*10+(c-'0');seen=true;if(point)scale++;}else if(c=='.'&&!point)point=true;else if(c==','&&!point&&seen){}else break;}
        if(!seen)throw new FormatException();
        if(at<end&&exponentAllowed&&(value[at]=='e'||value[at]=='E')){at++;bool minus=at<end&&value[at]=='-';if(at<end&&(value[at]=='+'||value[at]=='-'))at++;if(at==end)throw new FormatException();int exp=0;for(;at<end;at++){if(value[at]<'0'||value[at]>'9'||exp>1000)throw new OverflowException();exp=exp*10+value[at]-'0';}scale+=minus?exp:-exp;}
        if(at!=end)throw new FormatException();return Pack(coefficient,scale,negative);
    }
    private static bool Space(char c)=>c==' '||c>=9&&c<=13;
    public static implicit operator Decimal(int value)=>new Decimal(value);
    public static implicit operator Decimal(uint value)=>new Decimal(value);
    public static implicit operator Decimal(long value)=>new Decimal(value);
    public static implicit operator Decimal(ulong value)=>new Decimal(value);
    public static implicit operator Decimal(short value)=>new Decimal((int)value);
    public static implicit operator Decimal(ushort value)=>new Decimal((uint)value);
    public static implicit operator Decimal(byte value)=>new Decimal((uint)value);
    public static implicit operator Decimal(sbyte value)=>new Decimal((int)value);
    public static explicit operator Decimal(double value)=>new Decimal(value);
    public static explicit operator Decimal(float value)=>new Decimal(value);
    public static explicit operator long(Decimal value)=>(long)value.Integer();
    public static explicit operator ulong(Decimal value)=>(ulong)value.Integer();
    public static explicit operator int(Decimal value)=>(int)value.Integer();
    public static explicit operator uint(Decimal value)=>(uint)value.Integer();
    public static explicit operator short(Decimal value)=>(short)value.Integer();
    public static explicit operator ushort(Decimal value)=>(ushort)value.Integer();
    public static explicit operator sbyte(Decimal value)=>(sbyte)value.Integer();
    public static explicit operator byte(Decimal value)=>(byte)value.Integer();
    public static explicit operator char(Decimal value)=>(char)(ushort)value;
    public static explicit operator double(Decimal value)=>Double.Parse(value.ToString());
    public static explicit operator float(Decimal value)=>Single.Parse(value.ToString());
    public static byte ToByte(Decimal value)=>(byte)value;
    public static sbyte ToSByte(Decimal value)=>(sbyte)value;
    public static short ToInt16(Decimal value)=>(short)value;
    public static ushort ToUInt16(Decimal value)=>(ushort)value;
    public static int ToInt32(Decimal value)=>(int)value;
    public static uint ToUInt32(Decimal value)=>(uint)value;
    public static long ToInt64(Decimal value)=>(long)value;
    public static ulong ToUInt64(Decimal value)=>(ulong)value;
    public static float ToSingle(Decimal value)=>(float)value;
    public static double ToDouble(Decimal value)=>(double)value;
}
