using System;
namespace Dolphin.Text;
public abstract class FormattableString : IFormattable
{
    public abstract string Format {get;}
    public abstract object?[] GetArguments();
    public abstract int ArgumentCount {get;}
    public abstract object? GetArgument(int index);
    public abstract string ToString(IFormatProvider? formatProvider);
    string IFormattable.ToString(string? format,IFormatProvider? formatProvider)=>ToString(formatProvider);
    public override string ToString()=>ToString(null);
    public static string Invariant(FormattableString formattable){if(formattable==null)throw new ArgumentNullException("formattable");return formattable.ToString(Dolphin.Globalization.CultureInfo.InvariantCulture);}
    public static string CurrentCulture(FormattableString formattable)=>Invariant(formattable);
}
public static class FormattableStringFactory
{
    public static FormattableString Create(string format,params object?[] arguments)
    {if(format==null)throw new ArgumentNullException("format");if(arguments==null)throw new ArgumentNullException("arguments");return new Composite(format,arguments);}
    private sealed class Composite : FormattableString
    {
        private readonly string format;private readonly object?[] arguments;
        internal Composite(string format,object?[] arguments){this.format=format;this.arguments=arguments;}
        public override string Format=>format;
        public override int ArgumentCount=>arguments.Length;
        public override object?[] GetArguments()=>arguments;
        public override object? GetArgument(int index)=>arguments[index];
        public override string ToString(IFormatProvider? provider)=>Formatter.Composite(format,arguments,provider);
    }
}
