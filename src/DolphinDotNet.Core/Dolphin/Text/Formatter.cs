using System;
namespace Dolphin.Text;

internal static class Formatter
{
    internal static string ObjectValue(object? value)=>value==null?"":value.ToString()??"";
    internal static void Provider(IFormatProvider? provider){if(provider!=null&&!(provider is Dolphin.Globalization.CultureInfo))throw new NotSupportedException("Only invariant formatting is available.");}
    internal static void Append(StringBuilder builder,object? value,int alignment,string? format,IFormatProvider? provider){string text=Value(value,format,provider);long width=alignment<0?-(long)alignment:alignment;if(width>int.MaxValue)throw new ArgumentOutOfRangeException("alignment");int pad=(int)width-text.Length;if(alignment>0&&pad>0)builder.Append(' ',pad);builder.Append(text);if(alignment<0&&pad>0)builder.Append(' ',pad);}
    internal static string Value(object? value,string? format,IFormatProvider? provider=null)
    {
        if(value==null)return "";
        if(value is string)return (string)value;
        if(value is int)return ((int)value).ToString(format);
        if(value is uint)return ((uint)value).ToString(format);
        if(value is long)return ((long)value).ToString(format);
        if(value is ulong)return ((ulong)value).ToString(format);
        if(value is short)return ((short)value).ToString(format);
        if(value is ushort)return ((ushort)value).ToString(format);
        if(value is byte)return ((byte)value).ToString(format);
        if(value is sbyte)return ((sbyte)value).ToString(format);
        if(value is float)return ((float)value).ToString(format);
        if(value is double)return ((double)value).ToString(format);
        if(value is bool)return ((bool)value).ToString();
        if(value is char)return ((char)value).ToString();
        if(value is IFormattable)return ((IFormattable)value).ToString(format,provider)??"";
        return value.ToString()??"";
    }
    private static int Number(string text,ref int at)
    {
        int value=0,start=at;
        while(at<text.Length&&text[at]>='0'&&text[at]<='9'){int digit=text[at++]-'0';if(value>(999999-digit)/10)throw new FormatException();value=value*10+digit;}
        if(at==start)throw new FormatException();return value;
    }
    internal static string Composite(string format,object?[] args,IFormatProvider? provider=null)
    {
        if(format==null)throw new ArgumentNullException("format");if(args==null)throw new ArgumentNullException("args");
        Provider(provider);var result=new StringBuilder();int at=0;
        while(at<format.Length){char c=format[at++];if(c=='}'){if(at>=format.Length||format[at++]!='}')throw new FormatException();result.Append('}');continue;}if(c!='{'){result.Append(c);continue;}if(at<format.Length&&format[at]=='{'){at++;result.Append('{');continue;}
            int index=Number(format,ref at),alignment=0;if(index>=args.Length)throw new FormatException();while(at<format.Length&&format[at]==' ')at++;
            if(at<format.Length&&format[at]==','){at++;while(at<format.Length&&format[at]==' ')at++;bool negative=at<format.Length&&format[at]=='-';if(negative)at++;alignment=Number(format,ref at);if(negative)alignment=-alignment;while(at<format.Length&&format[at]==' ')at++;}
            string? spec=null;if(at<format.Length&&format[at]==':'){int start=++at;while(at<format.Length&&format[at]!='}'){if(format[at]=='{')throw new FormatException();at++;}spec=format.Substring(start,at-start);}
            if(at>=format.Length||format[at++]!='}')throw new FormatException();string value=Value(args[index],spec,provider);int pad=(alignment<0?-alignment:alignment)-value.Length;if(alignment>0&&pad>0)result.Append(' ',pad);result.Append(value);if(alignment<0&&pad>0)result.Append(' ',pad);
        }
        return result.ToString();
    }
}
