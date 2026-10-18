namespace Dolphin.Text;

public struct DefaultInterpolatedStringHandler
{
    private StringBuilder builder;
    private System.IFormatProvider? provider;
    public DefaultInterpolatedStringHandler(int literalLength,int formattedCount):this(literalLength,formattedCount,null){}
    public DefaultInterpolatedStringHandler(int literalLength,int formattedCount,System.IFormatProvider? provider){Formatter.Provider(provider);builder=new StringBuilder(literalLength);this.provider=provider;}
    public void AppendLiteral(string value)=>builder.Append(value);
    public void AppendFormatted<T>(T value)=>builder.Append(Formatter.Value(value,null,provider));
    public void AppendFormatted<T>(T value,string? format)=>builder.Append(Formatter.Value(value,format,provider));
    public void AppendFormatted<T>(T value,int alignment)=>AppendFormatted(value,alignment,null);
    public void AppendFormatted<T>(T value,int alignment,string? format)=>Formatter.Append(builder,value,alignment,format,provider);
    public void AppendFormatted(string? value)=>builder.Append(value);
    public void AppendFormatted(string? value,int alignment=0,string? format=null)=>AppendFormatted<string?>(value,alignment,format);
    public void AppendFormatted(object? value,int alignment=0,string? format=null)=>Formatter.Append(builder,value,alignment,format,provider);
    public override string ToString()=>builder.ToString();
    public string ToStringAndClear(){string result=builder.ToString();builder=null!;return result;}
}
