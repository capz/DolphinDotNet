using System;
namespace Dolphin.Globalization;
// Only the invariant provider is materialized; culture databases are unavailable.
public sealed class CultureInfo : IFormatProvider
{
    private CultureInfo(){}
    public static CultureInfo InvariantCulture=>new CultureInfo();
    public static CultureInfo CurrentCulture {get=>InvariantCulture;set{if(value==null)throw new ArgumentNullException("value");}}
    public object? GetFormat(Type? formatType)=>null;
}
