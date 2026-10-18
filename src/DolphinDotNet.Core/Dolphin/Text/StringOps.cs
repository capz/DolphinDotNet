using System;
using System.Collections.Generic;
namespace Dolphin.Text;

public enum StringComparison { CurrentCulture, CurrentCultureIgnoreCase, InvariantCulture, InvariantCultureIgnoreCase, Ordinal, OrdinalIgnoreCase }
[Flags] public enum StringSplitOptions { None=0, RemoveEmptyEntries=1, TrimEntries=2 }

// Instance System.String calls are lowered to static methods with a receiver.
public static class StringOps
{
    public static readonly string Empty="";
    public static string Create(IFormatProvider? provider,ref DefaultInterpolatedStringHandler handler){Formatter.Provider(provider);return handler.ToStringAndClear();}
    internal static void Receiver(string value){if(value==null)throw new NullReferenceException();}
    internal static void Range(string value,int start,int count){Receiver(value);if(start<0||count<0||start>value.Length||count>value.Length-start)throw new ArgumentOutOfRangeException();}
    internal static bool Fold(StringComparison comparison){if(comparison==StringComparison.Ordinal)return false;if(comparison==StringComparison.OrdinalIgnoreCase)return true;if((int)comparison<0||(int)comparison>5)throw new ArgumentException("Invalid comparison.");throw new NotSupportedException("Culture-sensitive comparison is unavailable; use Ordinal or OrdinalIgnoreCase.");}
    internal static bool White(char c)=>c==' '||c>='\t'&&c<='\r'||c=='\u0085'||c=='\u00a0'||c=='\u1680'||c>='\u2000'&&c<='\u200a'||c=='\u2028'||c=='\u2029'||c=='\u202f'||c=='\u205f'||c=='\u3000';
    public static bool IsNullOrEmpty(string? value)=>value==null||value.Length==0;
    public static bool IsNullOrWhiteSpace(string? value){if(value==null)return true;for(int i=0;i<value.Length;i++)if(!White(value[i]))return false;return true;}
    public static string Identity(string value){Receiver(value);return value;}
    public static bool InstanceEquals(string value,string? other){Receiver(value);return value==other;}
    public static bool InstanceEquals(string value,object? other){Receiver(value);return other is string&&value==(string)other;}
    public static bool InstanceEquals(string value,string? other,StringComparison comparison){Receiver(value);return Equals(value,other,comparison);}
    public static bool Equals(string? a,string? b)=>a==b;
    public static bool Equals(string? a,string? b,StringComparison comparison){bool fold=Fold(comparison);return NativeText.TextCompare(a,b,fold)==0;}
    public static int CompareOrdinal(string? a,string? b)=>NativeText.TextCompare(a,b,false);
    public static int Compare(string? a,string? b,StringComparison comparison)=>NativeText.TextCompare(a,b,Fold(comparison));
    private static bool Match(string a,int start,string b,bool fold)=>NativeText.TextRangeEqual(a,start,b,0,b.Length,fold);
    public static bool StartsWith(string value,string prefix)=>StartsWith(value,prefix,StringComparison.Ordinal);
    public static bool StartsWith(string value,string prefix,StringComparison comparison){Receiver(value);if(prefix==null)throw new ArgumentNullException("prefix");bool fold=Fold(comparison);return prefix.Length<=value.Length&&Match(value,0,prefix,fold);}
    public static bool StartsWith(string value,char prefix){Receiver(value);return value.Length>0&&value[0]==prefix;}
    public static bool EndsWith(string value,string suffix)=>EndsWith(value,suffix,StringComparison.Ordinal);
    public static bool EndsWith(string value,string suffix,StringComparison comparison){Receiver(value);if(suffix==null)throw new ArgumentNullException("suffix");bool fold=Fold(comparison);return suffix.Length<=value.Length&&Match(value,value.Length-suffix.Length,suffix,fold);}
    public static bool EndsWith(string value,char suffix){Receiver(value);return value.Length>0&&value[value.Length-1]==suffix;}
    public static bool Contains(string value,string needle)=>IndexOf(value,needle)>=0;
    public static bool Contains(string value,string needle,StringComparison comparison)=>IndexOf(value,needle,comparison)>=0;
    public static bool Contains(string value,char needle)=>IndexOf(value,needle)>=0;
    public static int IndexOf(string value,string needle)=>IndexOf(value,needle,0,value.Length,StringComparison.Ordinal);
    public static int IndexOf(string value,string needle,StringComparison comparison)=>IndexOf(value,needle,0,value.Length,comparison);
    public static int IndexOf(string value,string needle,int start)=>IndexOf(value,needle,start,value.Length-start,StringComparison.Ordinal);
    public static int IndexOf(string value,string needle,int start,StringComparison comparison)=>IndexOf(value,needle,start,value.Length-start,comparison);
    public static int IndexOf(string value,string needle,int start,int count)=>IndexOf(value,needle,start,count,StringComparison.Ordinal);
    public static int IndexOf(string value,string needle,int start,int count,StringComparison comparison){Range(value,start,count);if(needle==null)throw new ArgumentNullException("needle");bool fold=Fold(comparison);for(int i=start;i<=start+count-needle.Length;i++)if(Match(value,i,needle,fold))return i;return -1;}
    public static int IndexOf(string value,char needle)=>IndexOf(value,needle,0,value.Length);
    public static int IndexOf(string value,char needle,int start)=>IndexOf(value,needle,start,value.Length-start);
    public static int IndexOf(string value,char needle,int start,int count){Range(value,start,count);for(int i=start;i<start+count;i++)if(value[i]==needle)return i;return -1;}
    public static int LastIndexOf(string value,char needle)=>LastIndexOf(value,needle,value.Length-1,value.Length);
    public static int LastIndexOf(string value,char needle,int start)=>LastIndexOf(value,needle,start,start+1);
    public static int LastIndexOf(string value,char needle,int start,int count){Receiver(value);if(value.Length==0){if(start!=-1&&start!=0||count<0)throw new ArgumentOutOfRangeException();return -1;}if(start<0||start>=value.Length||count<0||count>start+1)throw new ArgumentOutOfRangeException();for(int i=start;i>start-count;i--)if(value[i]==needle)return i;return -1;}
    public static int LastIndexOf(string value,string needle)=>LastIndexOf(value,needle,value.Length-1,value.Length,StringComparison.Ordinal);
    public static int LastIndexOf(string value,string needle,StringComparison comparison)=>LastIndexOf(value,needle,value.Length-1,value.Length,comparison);
    public static int LastIndexOf(string value,string needle,int start)=>LastIndexOf(value,needle,start,start+1,StringComparison.Ordinal);
    public static int LastIndexOf(string value,string needle,int start,int count)=>LastIndexOf(value,needle,start,count,StringComparison.Ordinal);
    public static int LastIndexOf(string value,string needle,int start,int count,StringComparison comparison){Receiver(value);if(needle==null)throw new ArgumentNullException("needle");bool fold=Fold(comparison);if(value.Length==0){if(start!=-1&&start!=0||count<0)throw new ArgumentOutOfRangeException();return needle.Length==0?0:-1;}if(start<0||start>value.Length||count<0||count>start+1)throw new ArgumentOutOfRangeException();if(start==value.Length){start--;if(count>0)count--;}if(needle.Length==0)return start+1;for(int i=start-needle.Length+1;i>start-count;i--)if(Match(value,i,needle,fold))return i;return -1;}
    public static int IndexOfAny(string value,char[] chars)=>IndexOfAny(value,chars,0,value.Length);
    public static int IndexOfAny(string value,char[] chars,int start)=>IndexOfAny(value,chars,start,value.Length-start);
    public static int IndexOfAny(string value,char[] chars,int start,int count){Range(value,start,count);if(chars==null)throw new ArgumentNullException("chars");for(int i=start;i<start+count;i++)for(int j=0;j<chars.Length;j++)if(value[i]==chars[j])return i;return -1;}
    public static char[] ToCharArray(string value)=>ToCharArray(value,0,value.Length);
    public static char[] ToCharArray(string value,int start,int count){Range(value,start,count);var result=new char[count];for(int i=0;i<count;i++)result[i]=value[start+i];return result;}
    public static void CopyTo(string value,int sourceIndex,char[] destination,int destinationIndex,int count){Range(value,sourceIndex,count);if(destination==null)throw new ArgumentNullException("destination");if(destinationIndex<0||destinationIndex>destination.Length||count>destination.Length-destinationIndex)throw new ArgumentOutOfRangeException();for(int i=0;i<count;i++)destination[destinationIndex+i]=value[sourceIndex+i];}
    public static string Insert(string value,int start,string inserted){Range(value,start,0);if(inserted==null)throw new ArgumentNullException("inserted");return Concat(value.Substring(0,start),inserted,value.Substring(start));}
    public static string Remove(string value,int start)=>Remove(value,start,value.Length-start);
    public static string Remove(string value,int start,int count){Range(value,start,count);return Concat(value.Substring(0,start),value.Substring(start+count));}
    public static string Replace(string value,char oldChar,char newChar){Receiver(value);char[] chars=ToCharArray(value);for(int i=0;i<chars.Length;i++)if(chars[i]==oldChar)chars[i]=newChar;return new string(chars);}
    public static string Replace(string value,string oldValue,string? newValue)=>Replace(value,oldValue,newValue,StringComparison.Ordinal);
    public static string Replace(string value,string oldValue,string? newValue,StringComparison comparison){Receiver(value);if(oldValue==null)throw new ArgumentNullException("oldValue");if(oldValue.Length==0)throw new ArgumentException("Empty oldValue.");Fold(comparison);var result=new StringBuilder();int at=0,next;while((next=IndexOf(value,oldValue,at,comparison))>=0){result.Append(value,at,next-at);result.Append(newValue);at=next+oldValue.Length;}result.Append(value,at,value.Length-at);return result.ToString();}
    private static bool TrimChar(char c,char[]? chars){if(chars==null||chars.Length==0)return White(c);for(int i=0;i<chars.Length;i++)if(chars[i]==c)return true;return false;}
    private static string TrimPart(string value,char[]? chars,bool first,bool last){Receiver(value);int start=0,end=value.Length;if(first)while(start<end&&TrimChar(value[start],chars))start++;if(last)while(end>start&&TrimChar(value[end-1],chars))end--;return value.Substring(start,end-start);}
    public static string Trim(string value)=>TrimPart(value,null,true,true);
    public static string Trim(string value,char[]? chars)=>TrimPart(value,chars,true,true);
    public static string Trim(string value,char c)=>Trim(value,new[]{c});
    public static string TrimStart(string value)=>TrimPart(value,null,true,false);
    public static string TrimStart(string value,char[]? chars)=>TrimPart(value,chars,true,false);
    public static string TrimStart(string value,char c)=>TrimStart(value,new[]{c});
    public static string TrimEnd(string value)=>TrimPart(value,null,false,true);
    public static string TrimEnd(string value,char[]? chars)=>TrimPart(value,chars,false,true);
    public static string TrimEnd(string value,char c)=>TrimEnd(value,new[]{c});
    public static string PadLeft(string value,int width)=>PadLeft(value,width,' ');
    public static string PadLeft(string value,int width,char c){Receiver(value);if(width<0)throw new ArgumentOutOfRangeException("width");return width<=value.Length?value:new StringBuilder(width).Append(c,width-value.Length).Append(value).ToString();}
    public static string PadRight(string value,int width)=>PadRight(value,width,' ');
    public static string PadRight(string value,int width,char c){Receiver(value);if(width<0)throw new ArgumentOutOfRangeException("width");return width<=value.Length?value:new StringBuilder(width).Append(value).Append(c,width-value.Length).ToString();}
    public static string Concat(string? a,string? b)=>string.Concat(a,b);
    public static string Concat(string? a,string? b,string? c)=>Concat(Concat(a,b),c);
    public static string Concat(string? a,string? b,string? c,string? d)=>Concat(Concat(a,b),Concat(c,d));
    public static string Concat(string?[] values){if(values==null)throw new ArgumentNullException("values");var b=new StringBuilder();for(int i=0;i<values.Length;i++)b.Append(values[i]);return b.ToString();}
    public static string Concat(object? value)=>Formatter.ObjectValue(value);
    public static string Concat(object? a,object? b)=>Concat(Formatter.ObjectValue(a),Formatter.ObjectValue(b));
    public static string Concat(object? a,object? b,object? c)=>Concat(Formatter.ObjectValue(a),Formatter.ObjectValue(b),Formatter.ObjectValue(c));
    public static string Concat(object?[] values){if(values==null)throw new ArgumentNullException("values");var b=new StringBuilder();for(int i=0;i<values.Length;i++)b.Append(values[i]);return b.ToString();}
    public static string Join(string? separator,string?[] values)=>Join(separator,values,0,values==null?0:values.Length);
    public static string Join(string? separator,string?[] values,int start,int count){if(values==null)throw new ArgumentNullException("values");if(start<0||count<0||start>values.Length||count>values.Length-start)throw new ArgumentOutOfRangeException();var b=new StringBuilder();for(int i=0;i<count;i++){if(i>0)b.Append(separator);b.Append(values[start+i]);}return b.ToString();}
    public static string Join(string? separator,object?[] values){if(values==null)throw new ArgumentNullException("values");var b=new StringBuilder();for(int i=0;i<values.Length;i++){if(i>0)b.Append(separator);b.Append(values[i]);}return b.ToString();}
    public static string[] Split(string value,char[]? separator)=>Split(value,separator,int.MaxValue,StringSplitOptions.None);
    public static string[] Split(string value,char[]? separator,StringSplitOptions options)=>Split(value,separator,int.MaxValue,options);
    public static string[] Split(string value,char[]? separator,int count)=>Split(value,separator,count,StringSplitOptions.None);
    public static string[] Split(string value,char[]? separator,int count,StringSplitOptions options)=>SplitCore(value,separator,null,count,options);
    public static string[] Split(string value,char separator,StringSplitOptions options=StringSplitOptions.None)=>Split(value,new[]{separator},int.MaxValue,options);
    public static string[] Split(string value,char separator,int count,StringSplitOptions options=StringSplitOptions.None)=>Split(value,new[]{separator},count,options);
    public static string[] Split(string value,string[]? separator,StringSplitOptions options)=>Split(value,separator,int.MaxValue,options);
    public static string[] Split(string value,string[]? separator,int count,StringSplitOptions options)=>SplitCore(value,null,separator,count,options);
    public static string[] Split(string value,string? separator,StringSplitOptions options=StringSplitOptions.None)=>Split(value,separator,int.MaxValue,options);
    public static string[] Split(string value,string? separator,int count,StringSplitOptions options=StringSplitOptions.None)=>SplitCore(value,null,new[]{separator!},count,options);
    private static string[] SplitCore(string value,char[]? chars,string[]? strings,int count,StringSplitOptions options){Receiver(value);if(count<0)throw new ArgumentOutOfRangeException("count");if(((int)options&~3)!=0)throw new ArgumentException("Invalid split options.");var parts=new List<string>();if(count==0)return parts.ToArray();bool remove=((int)options&1)!=0,trim=((int)options&2)!=0;if(count==1){string one=trim?Trim(value):value;if(!remove||one.Length>0)parts.Add(one);return parts.ToArray();}int start=0,i=0;while(i<value.Length){int width=0;if(strings!=null&&strings.Length>0){for(int j=0;j<strings.Length;j++){string? s=strings[j];if(!IsNullOrEmpty(s)&&s!.Length<=value.Length-i&&Match(value,i,s,false)){width=s.Length;break;}}}else if(TrimChar(value[i],chars))width=1;if(width==0){i++;continue;}string item=value.Substring(start,i-start);if(trim)item=Trim(item);if(!remove||item.Length>0){if(parts.Count==count-1)break;parts.Add(item);}i+=width;start=i;}string last=value.Substring(start);if(trim)last=Trim(last);if(!remove||last.Length>0)parts.Add(last);return parts.ToArray();}
    public static string Format(string format,object? a)=>Formatter.Composite(format,new[]{a});
    public static string Format(string format,object? a,object? b)=>Formatter.Composite(format,new[]{a,b});
    public static string Format(string format,object? a,object? b,object? c)=>Formatter.Composite(format,new[]{a,b,c});
    public static string Format(string format,params object?[] args)=>Formatter.Composite(format,args);
    public static string Format(IFormatProvider? provider,string format,object? a)=>Formatter.Composite(format,new[]{a},provider);
    public static string Format(IFormatProvider? provider,string format,object? a,object? b)=>Formatter.Composite(format,new[]{a,b},provider);
    public static string Format(IFormatProvider? provider,string format,object? a,object? b,object? c)=>Formatter.Composite(format,new[]{a,b,c},provider);
    public static string Format(IFormatProvider? provider,string format,params object?[] args)=>Formatter.Composite(format,args,provider);

    public static string ToUpperInvariant(string value)=>NativeText.TextCase(value,false);
    public static string ToLowerInvariant(string value)=>NativeText.TextCase(value,true);
    public static string Copy(string value){if(value==null)throw new ArgumentNullException("value");return value.Substring(0);}
    public static object Clone(string value)=>Identity(value);
    public static int Compare(string? a,string? b)=>CompareOrdinal(a,b);
    public static int CompareTo(string value,string? other){Receiver(value);return CompareOrdinal(value,other);}
    public static int CompareTo(string value,object? other){Receiver(value);if(other==null)return 1;if(!(other is string))throw new ArgumentException("Expected string.");return CompareOrdinal(value,(string)other);}
    public static bool Contains(string value,char needle,StringComparison comparison)=>IndexOf(value,needle.ToString(),comparison)>=0;
    public static int IndexOf(string value,char needle,StringComparison comparison)=>IndexOf(value,needle.ToString(),comparison);
    public static int LastIndexOfAny(string value,char[] chars)=>LastIndexOfAny(value,chars,value.Length-1,value.Length);
    public static int LastIndexOfAny(string value,char[] chars,int start)=>LastIndexOfAny(value,chars,start,start+1);
    public static int LastIndexOfAny(string value,char[] chars,int start,int count){Receiver(value);if(chars==null)throw new ArgumentNullException("chars");if(value.Length==0)return -1;if(start<0||start>=value.Length||count<0||count>start+1)throw new ArgumentOutOfRangeException();for(int i=start;i>start-count;i--)for(int j=0;j<chars.Length;j++)if(value[i]==chars[j])return i;return -1;}
    public static string Concat(IEnumerable<string?> values){if(values==null)throw new ArgumentNullException("values");string? array=ArrayJoin("",values);if(array!=null)return array;var b=new StringBuilder();foreach(string? value in values)b.Append(value);return b.ToString();}
    public static string Concat<T>(IEnumerable<T> values){if(values==null)throw new ArgumentNullException("values");string? array=ArrayJoin("",values);if(array!=null)return array;var b=new StringBuilder();foreach(T value in values)b.Append(Formatter.ObjectValue(value));return b.ToString();}
    public static string Join(string? separator,IEnumerable<string?> values){if(values==null)throw new ArgumentNullException("values");string? array=ArrayJoin(separator,values);if(array!=null)return array;var b=new StringBuilder();bool first=true;foreach(string? value in values){if(!first)b.Append(separator);b.Append(value);first=false;}return b.ToString();}
    public static string Join<T>(string? separator,IEnumerable<T> values){if(values==null)throw new ArgumentNullException("values");string? array=ArrayJoin(separator,values);if(array!=null)return array;var b=new StringBuilder();bool first=true;foreach(T value in values){if(!first)b.Append(separator);b.Append(Formatter.ObjectValue(value));first=false;}return b.ToString();}
    public static string Join(char separator,string?[] values)=>Join(separator.ToString(),values);
    public static string Join(char separator,object?[] values)=>Join(separator.ToString(),values);
    public static string Join<T>(char separator,IEnumerable<T> values)=>Join(separator.ToString(),values);
    private static string? ArrayJoin(string? separator,object values){int count=NativeText.TextArrayLength(values);if(count<0)return null;var b=new StringBuilder();for(int i=0;i<count;i++){if(i>0)b.Append(separator);b.Append(Formatter.ObjectValue(NativeText.TextArrayItem(values,i)));}return b.ToString();}
}
