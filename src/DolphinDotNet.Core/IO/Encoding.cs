using System;
namespace Dolphin.Text;
internal static class NativeText
{
    internal static byte[] Encode(string text,int codePage,bool strict)=>throw new NotSupportedException("AOT intrinsic");
    internal static string Decode(byte[] data,int offset,int count,int codePage,bool strict)=>throw new NotSupportedException("AOT intrinsic");
    internal static string FromChars(char[] chars,int offset,int count)=>throw new NotSupportedException("AOT intrinsic");
}
public class Encoding
{
    public int CodePage {get;}
    internal bool Strict {get;}
    private readonly bool bom;
    protected Encoding(int codePage,bool bom,bool strict){CodePage=codePage;this.bom=bom;Strict=strict;}
    public static Encoding UTF8=>new UTF8Encoding(true);
    public static Encoding Unicode=>new UnicodeEncoding(false,true);
    public static Encoding BigEndianUnicode=>new UnicodeEncoding(true,true);
    public static Encoding ASCII=>new Encoding(20127,false,false);
    public static Encoding GetEncoding(int codePage){if(codePage==65001)return UTF8;if(codePage==1200)return Unicode;if(codePage==1201)return BigEndianUnicode;if(codePage==20127)return ASCII;if(codePage==28591)return new Encoding(28591,false,false);throw new NotSupportedException("Encoding is not available.");}
    public static Encoding GetEncoding(string name){if(name==null)throw new ArgumentNullException("name");if(name=="utf-8"||name=="UTF-8")return UTF8;if(name=="utf-16"||name=="UTF-16")return Unicode;if(name=="ascii"||name=="ASCII")return ASCII;throw new NotSupportedException("Encoding is not available.");}
    public byte[] GetBytes(string text)=>NativeText.Encode(text,CodePage,Strict);
    public byte[] GetBytes(char[] chars)=>GetBytes(NativeText.FromChars(chars,0,chars==null?0:chars.Length));
    public byte[] GetBytes(char[] chars,int index,int count)=>GetBytes(NativeText.FromChars(chars,index,count));
    public int GetBytes(string text,int charIndex,int charCount,byte[] bytes,int byteIndex){var data=GetBytes(text.Substring(charIndex,charCount));if(bytes==null)throw new ArgumentNullException("bytes");if(byteIndex<0)throw new ArgumentOutOfRangeException("byteIndex");if(byteIndex>bytes.Length-data.Length)throw new ArgumentException("Destination is too small.");Array.Copy(data,0,bytes,byteIndex,data.Length);return data.Length;}
    public int GetByteCount(string text)=>GetBytes(text).Length;
    public int GetByteCount(char[] chars)=>GetBytes(chars).Length;
    public string GetString(byte[] bytes)=>GetString(bytes,0,bytes==null?0:bytes.Length);
    public string GetString(byte[] bytes,int index,int count)=>NativeText.Decode(bytes,index,count,CodePage,Strict);
    public char[] GetChars(byte[] bytes){var text=GetString(bytes);var chars=new char[text.Length];for(var i=0;i<chars.Length;i++)chars[i]=text[i];return chars;}
    public int GetCharCount(byte[] bytes)=>GetString(bytes).Length;
    public byte[] GetPreamble(){if(!bom)return new byte[0];if(CodePage==65001)return new byte[]{239,187,191};if(CodePage==1200)return new byte[]{255,254};if(CodePage==1201)return new byte[]{254,255};return new byte[0];}
}
public sealed class UTF8Encoding : Encoding
{
    public UTF8Encoding():this(false,false){}
    public UTF8Encoding(bool encoderShouldEmitUTF8Identifier):this(encoderShouldEmitUTF8Identifier,false){}
    public UTF8Encoding(bool encoderShouldEmitUTF8Identifier,bool throwOnInvalidBytes):base(65001,encoderShouldEmitUTF8Identifier,throwOnInvalidBytes){}
}
public sealed class UnicodeEncoding : Encoding
{
    public UnicodeEncoding():this(false,true,false){}
    public UnicodeEncoding(bool bigEndian,bool byteOrderMark):this(bigEndian,byteOrderMark,false){}
    public UnicodeEncoding(bool bigEndian,bool byteOrderMark,bool throwOnInvalidBytes):base(bigEndian?1201:1200,byteOrderMark,throwOnInvalidBytes){}
}
