using System;
namespace Dolphin.Numerics;
/// <summary>Checked typed conversions. Culture, object/IConvertible and formatting overloads are unsupported.</summary>
public static class Convert
{
    public static sbyte ToSByte(sbyte value){return (sbyte)value;}
    public static sbyte ToSByte(byte value){if(value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(short value){if(value<-128||value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(ushort value){if(value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(int value){if(value<-128||value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(uint value){if(value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(long value){if(value<-128||value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(ulong value){if(value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(char value){if(value>127)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(bool value)=>(sbyte)(value?1:0);
    public static sbyte ToSByte(double value){value=Math.Round(value);if(!(value>=-128d)||value>127d)throw new OverflowException();return (sbyte)value;}
    public static sbyte ToSByte(float value)=>ToSByte((double)value);
    public static sbyte ToSByte(string? value)=>value==null?(sbyte)0:ToSByte(Int32.Parse(value));
    public static byte ToByte(sbyte value){if(value<0)throw new OverflowException();return (byte)value;}
    public static byte ToByte(byte value){return (byte)value;}
    public static byte ToByte(short value){if(value<0||value>255)throw new OverflowException();return (byte)value;}
    public static byte ToByte(ushort value){if(value>255)throw new OverflowException();return (byte)value;}
    public static byte ToByte(int value){if(value<0||value>255)throw new OverflowException();return (byte)value;}
    public static byte ToByte(uint value){if(value>255)throw new OverflowException();return (byte)value;}
    public static byte ToByte(long value){if(value<0||value>255)throw new OverflowException();return (byte)value;}
    public static byte ToByte(ulong value){if(value>255)throw new OverflowException();return (byte)value;}
    public static byte ToByte(char value){if(value>255)throw new OverflowException();return (byte)value;}
    public static byte ToByte(bool value)=>(byte)(value?1:0);
    public static byte ToByte(double value){value=Math.Round(value);if(!(value>=0d)||value>255d)throw new OverflowException();return (byte)value;}
    public static byte ToByte(float value)=>ToByte((double)value);
    public static byte ToByte(string? value)=>value==null?(byte)0:ToByte(Int32.Parse(value));
    public static short ToInt16(sbyte value){return (short)value;}
    public static short ToInt16(byte value){return (short)value;}
    public static short ToInt16(short value){return (short)value;}
    public static short ToInt16(ushort value){if(value>32767)throw new OverflowException();return (short)value;}
    public static short ToInt16(int value){if(value<-32768||value>32767)throw new OverflowException();return (short)value;}
    public static short ToInt16(uint value){if(value>32767)throw new OverflowException();return (short)value;}
    public static short ToInt16(long value){if(value<-32768||value>32767)throw new OverflowException();return (short)value;}
    public static short ToInt16(ulong value){if(value>32767)throw new OverflowException();return (short)value;}
    public static short ToInt16(char value){if(value>32767)throw new OverflowException();return (short)value;}
    public static short ToInt16(bool value)=>(short)(value?1:0);
    public static short ToInt16(double value){value=Math.Round(value);if(!(value>=-32768d)||value>32767d)throw new OverflowException();return (short)value;}
    public static short ToInt16(float value)=>ToInt16((double)value);
    public static short ToInt16(string? value)=>value==null?(short)0:ToInt16(Int32.Parse(value));
    public static ushort ToUInt16(sbyte value){if(value<0)throw new OverflowException();return (ushort)value;}
    public static ushort ToUInt16(byte value){return (ushort)value;}
    public static ushort ToUInt16(short value){if(value<0)throw new OverflowException();return (ushort)value;}
    public static ushort ToUInt16(ushort value){return (ushort)value;}
    public static ushort ToUInt16(int value){if(value<0||value>65535)throw new OverflowException();return (ushort)value;}
    public static ushort ToUInt16(uint value){if(value>65535)throw new OverflowException();return (ushort)value;}
    public static ushort ToUInt16(long value){if(value<0||value>65535)throw new OverflowException();return (ushort)value;}
    public static ushort ToUInt16(ulong value){if(value>65535)throw new OverflowException();return (ushort)value;}
    public static ushort ToUInt16(char value){return (ushort)value;}
    public static ushort ToUInt16(bool value)=>(ushort)(value?1:0);
    public static ushort ToUInt16(double value){value=Math.Round(value);if(!(value>=0d)||value>65535d)throw new OverflowException();return (ushort)value;}
    public static ushort ToUInt16(float value)=>ToUInt16((double)value);
    public static ushort ToUInt16(string? value)=>value==null?(ushort)0:ToUInt16(Int32.Parse(value));
    public static int ToInt32(sbyte value){return (int)value;}
    public static int ToInt32(byte value){return (int)value;}
    public static int ToInt32(short value){return (int)value;}
    public static int ToInt32(ushort value){return (int)value;}
    public static int ToInt32(int value){return (int)value;}
    public static int ToInt32(uint value){if(value>2147483647)throw new OverflowException();return (int)value;}
    public static int ToInt32(long value){if(value<-2147483648||value>2147483647)throw new OverflowException();return (int)value;}
    public static int ToInt32(ulong value){if(value>2147483647)throw new OverflowException();return (int)value;}
    public static int ToInt32(char value){return (int)value;}
    public static int ToInt32(bool value)=>(int)(value?1:0);
    public static int ToInt32(double value){value=Math.Round(value);if(!(value>=-2147483648d)||value>2147483647d)throw new OverflowException();return (int)value;}
    public static int ToInt32(float value)=>ToInt32((double)value);
    public static int ToInt32(string? value)=>value==null?(int)0:ToInt32(Int32.Parse(value));
    public static uint ToUInt32(sbyte value){if(value<0)throw new OverflowException();return (uint)value;}
    public static uint ToUInt32(byte value){return (uint)value;}
    public static uint ToUInt32(short value){if(value<0)throw new OverflowException();return (uint)value;}
    public static uint ToUInt32(ushort value){return (uint)value;}
    public static uint ToUInt32(int value){if(value<0)throw new OverflowException();return (uint)value;}
    public static uint ToUInt32(uint value){return (uint)value;}
    public static uint ToUInt32(long value){if(value<0||value>4294967295L)throw new OverflowException();return (uint)value;}
    public static uint ToUInt32(ulong value){if(value>4294967295L)throw new OverflowException();return (uint)value;}
    public static uint ToUInt32(char value){return (uint)value;}
    public static uint ToUInt32(bool value)=>(uint)(value?1:0);
    public static uint ToUInt32(double value){value=Math.Round(value);if(!(value>=0d)||value>4294967295d)throw new OverflowException();return (uint)value;}
    public static uint ToUInt32(float value)=>ToUInt32((double)value);
    public static uint ToUInt32(string? value)=>value==null?(uint)0:ToUInt32(UInt32.Parse(value));
    public static long ToInt64(sbyte value){return (long)value;}
    public static long ToInt64(byte value){return (long)value;}
    public static long ToInt64(short value){return (long)value;}
    public static long ToInt64(ushort value){return (long)value;}
    public static long ToInt64(int value){return (long)value;}
    public static long ToInt64(uint value){return (long)value;}
    public static long ToInt64(long value){return (long)value;}
    public static long ToInt64(ulong value){if(value>9223372036854775807L)throw new OverflowException();return (long)value;}
    public static long ToInt64(char value){return (long)value;}
    public static long ToInt64(bool value)=>(long)(value?1:0);
    public static long ToInt64(double value){value=Math.Round(value);if(!(value>=-9223372036854775808d)||value>=9223372036854775808d)throw new OverflowException();return (long)value;}
    public static long ToInt64(float value)=>ToInt64((double)value);
    public static long ToInt64(string? value)=>value==null?(long)0:ToInt64(Int64.Parse(value));
    public static ulong ToUInt64(sbyte value){if(value<0)throw new OverflowException();return (ulong)value;}
    public static ulong ToUInt64(byte value){return (ulong)value;}
    public static ulong ToUInt64(short value){if(value<0)throw new OverflowException();return (ulong)value;}
    public static ulong ToUInt64(ushort value){return (ulong)value;}
    public static ulong ToUInt64(int value){if(value<0)throw new OverflowException();return (ulong)value;}
    public static ulong ToUInt64(uint value){return (ulong)value;}
    public static ulong ToUInt64(long value){if(value<0)throw new OverflowException();return (ulong)value;}
    public static ulong ToUInt64(ulong value){return (ulong)value;}
    public static ulong ToUInt64(char value){return (ulong)value;}
    public static ulong ToUInt64(bool value)=>(ulong)(value?1:0);
    public static ulong ToUInt64(double value){value=Math.Round(value);if(!(value>=0d)||value>=18446744073709551616d)throw new OverflowException();return (ulong)value;}
    public static ulong ToUInt64(float value)=>ToUInt64((double)value);
    public static ulong ToUInt64(string? value)=>value==null?(ulong)0:ToUInt64(UInt64.Parse(value));
    public static float ToSingle(sbyte value)=>(float)value;
    public static float ToSingle(byte value)=>(float)value;
    public static float ToSingle(short value)=>(float)value;
    public static float ToSingle(ushort value)=>(float)value;
    public static float ToSingle(int value)=>(float)value;
    public static float ToSingle(uint value)=>(float)value;
    public static float ToSingle(long value)=>(float)value;
    public static float ToSingle(ulong value)=>(float)value;
    public static float ToSingle(float value)=>(float)value;
    public static float ToSingle(double value)=>(float)value;
    public static float ToSingle(bool value)=>value?1:0;
    public static float ToSingle(string? value)=>value==null?0:Single.Parse(value);
    public static double ToDouble(sbyte value)=>(double)value;
    public static double ToDouble(byte value)=>(double)value;
    public static double ToDouble(short value)=>(double)value;
    public static double ToDouble(ushort value)=>(double)value;
    public static double ToDouble(int value)=>(double)value;
    public static double ToDouble(uint value)=>(double)value;
    public static double ToDouble(long value)=>(double)value;
    public static double ToDouble(ulong value)=>(double)value;
    public static double ToDouble(float value)=>(double)value;
    public static double ToDouble(double value)=>(double)value;
    public static double ToDouble(bool value)=>value?1:0;
    public static double ToDouble(string? value)=>value==null?0:Double.Parse(value);
    public static bool ToBoolean(sbyte value)=>value!=0;
    public static bool ToBoolean(byte value)=>value!=0;
    public static bool ToBoolean(short value)=>value!=0;
    public static bool ToBoolean(ushort value)=>value!=0;
    public static bool ToBoolean(int value)=>value!=0;
    public static bool ToBoolean(uint value)=>value!=0;
    public static bool ToBoolean(long value)=>value!=0;
    public static bool ToBoolean(ulong value)=>value!=0;
    public static bool ToBoolean(float value)=>value!=0;
    public static bool ToBoolean(double value)=>value!=0;
    public static bool ToBoolean(bool value)=>value;
    public static bool ToBoolean(string? value)=>value!=null&&Boolean.Parse(value);
    public static char ToChar(char value)=>value;
    public static char ToChar(string value){if(value==null)throw new ArgumentNullException("value");if(value.Length!=1)throw new FormatException();return value[0];}
    public static char ToChar(sbyte value){if(value<0)throw new OverflowException();return (char)value;}
    public static char ToChar(byte value){return (char)value;}
    public static char ToChar(short value){if(value<0)throw new OverflowException();return (char)value;}
    public static char ToChar(ushort value){return (char)value;}
    public static char ToChar(int value){if(value<0||value>65535)throw new OverflowException();return (char)value;}
    public static char ToChar(uint value){if(value>65535)throw new OverflowException();return (char)value;}
    public static char ToChar(long value){if(value<0||value>65535)throw new OverflowException();return (char)value;}
    public static char ToChar(ulong value){if(value>65535)throw new OverflowException();return (char)value;}
}
