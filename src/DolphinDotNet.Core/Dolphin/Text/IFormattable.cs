using System;
namespace Dolphin.Text;
public interface IFormattable
{
    string ToString(string? format,IFormatProvider? formatProvider);
}
