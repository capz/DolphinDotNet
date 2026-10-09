using System;
using System.Collections.Generic;
namespace Dolphin.Collections;
public sealed class StringComparer : IComparer<string>,IEqualityComparer<string>
{
    private readonly bool ignoreCase;
    private StringComparer(bool ignoreCase){this.ignoreCase=ignoreCase;}
    public static StringComparer Ordinal=>new StringComparer(false);
    public static StringComparer OrdinalIgnoreCase=>new StringComparer(true);
    public int Compare(string? x,string? y)=>Dolphin.Text.NativeText.TextCompare(x,y,ignoreCase);
    public bool Equals(string? x,string? y)=>Compare(x,y)==0;
    public int GetHashCode(string obj)=>Dolphin.Text.NativeText.TextHash(obj,ignoreCase);
}
