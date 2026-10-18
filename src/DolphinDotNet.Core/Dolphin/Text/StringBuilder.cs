using System;
namespace Dolphin.Text;

public sealed class StringBuilder
{
    private char[] buffer;
    private int length;
    private readonly int maxCapacity;
    public struct AppendInterpolatedStringHandler
    {
        private StringBuilder builder;
        private IFormatProvider? provider;
        public AppendInterpolatedStringHandler(int literalLength,int formattedCount,StringBuilder builder):this(literalLength,formattedCount,builder,null){}
        public AppendInterpolatedStringHandler(int literalLength,int formattedCount,StringBuilder builder,IFormatProvider? provider){Formatter.Provider(provider);this.builder=builder;this.provider=provider;}
        public void AppendLiteral(string value)=>builder.Append(value);
        public void AppendFormatted<T>(T value)=>Formatter.Append(builder,value,0,null,provider);
        public void AppendFormatted<T>(T value,string? format)=>Formatter.Append(builder,value,0,format,provider);
        public void AppendFormatted<T>(T value,int alignment)=>Formatter.Append(builder,value,alignment,null,provider);
        public void AppendFormatted<T>(T value,int alignment,string? format)=>Formatter.Append(builder,value,alignment,format,provider);
        public void AppendFormatted(string? value)=>builder.Append(value);
        public void AppendFormatted(string? value,int alignment=0,string? format=null)=>Formatter.Append(builder,value,alignment,format,provider);
        public void AppendFormatted(object? value,int alignment=0,string? format=null)=>Formatter.Append(builder,value,alignment,format,provider);
    }
    public StringBuilder Append(ref AppendInterpolatedStringHandler handler)=>this;
    public StringBuilder Append(IFormatProvider? provider,ref AppendInterpolatedStringHandler handler){Formatter.Provider(provider);return this;}
    public StringBuilder AppendLine(ref AppendInterpolatedStringHandler handler)=>AppendLine();
    public StringBuilder AppendLine(IFormatProvider? provider,ref AppendInterpolatedStringHandler handler){Formatter.Provider(provider);return AppendLine();}
    public StringBuilder():this(16,int.MaxValue){}
    public StringBuilder(int capacity):this(capacity,int.MaxValue){}
    public StringBuilder(int capacity,int maxCapacity){if(capacity<0||maxCapacity<1||capacity>maxCapacity)throw new ArgumentOutOfRangeException();this.maxCapacity=maxCapacity;buffer=new char[capacity==0?(maxCapacity<16?maxCapacity:16):capacity];}
    public StringBuilder(string? value):this(value,16){}
    public StringBuilder(string? value,int capacity):this(capacity){Append(value);}
    public StringBuilder(string? value,int startIndex,int length,int capacity):this(capacity){Append(value,startIndex,length);}
    public int MaxCapacity=>maxCapacity;
    public int Capacity {get=>buffer.Length;set{if(value<length||value>maxCapacity)throw new ArgumentOutOfRangeException("value");if(value!=buffer.Length){var next=new char[value];for(int i=0;i<length;i++)next[i]=buffer[i];buffer=next;}}}
    public int Length {get=>length;set{if(value<0||value>maxCapacity)throw new ArgumentOutOfRangeException("value");EnsureCapacity(value);for(int i=length;i<value;i++)buffer[i]='\0';length=value;}}
    public char this[int index] {get{if(index<0||index>=length)throw new IndexOutOfRangeException();return buffer[index];}set{if(index<0||index>=length)throw new ArgumentOutOfRangeException("index");buffer[index]=value;}}
    public int EnsureCapacity(int capacity){if(capacity<0||capacity>maxCapacity)throw new ArgumentOutOfRangeException("capacity");if(capacity>buffer.Length){int next=buffer.Length<=maxCapacity/2?buffer.Length*2:maxCapacity;if(next<capacity)next=capacity;Capacity=next;}return buffer.Length;}
    private void Grow(int count){if(count<0||count>maxCapacity-length)throw new ArgumentOutOfRangeException("count");EnsureCapacity(length+count);}
    public StringBuilder Clear(){length=0;return this;}
    public override string ToString()=>new string(buffer,0,length);
    public string ToString(int startIndex,int length){if(startIndex<0||length<0||startIndex>this.length||length>this.length-startIndex)throw new ArgumentOutOfRangeException();return new string(buffer,startIndex,length);}
    public StringBuilder Append(char value){Grow(1);buffer[length++]=value;return this;}
    public StringBuilder Append(char value,int repeatCount){Grow(repeatCount);for(int i=0;i<repeatCount;i++)buffer[length++]=value;return this;}
    public StringBuilder Append(string? value){if(value!=null)Append(value,0,value.Length);return this;}
    public StringBuilder Append(string? value,int startIndex,int count){if(value==null){if(startIndex==0&&count==0)return this;throw new ArgumentNullException("value");}StringOps.Range(value,startIndex,count);Grow(count);for(int i=0;i<count;i++)buffer[length++]=value[startIndex+i];return this;}
    public StringBuilder Append(char[]? value){if(value!=null)Append(value,0,value.Length);return this;}
    public StringBuilder Append(char[]? value,int startIndex,int count){if(value==null){if(startIndex==0&&count==0)return this;throw new ArgumentNullException("value");}if(startIndex<0||count<0||startIndex>value.Length||count>value.Length-startIndex)throw new ArgumentOutOfRangeException();Grow(count);for(int i=0;i<count;i++)buffer[length++]=value[startIndex+i];return this;}
    public StringBuilder Append(StringBuilder? value){if(value!=null)Append(value.ToString());return this;}
    public StringBuilder Append(object? value)=>Append(Formatter.ObjectValue(value));
    public StringBuilder AppendLine()=>Append('\n');
    public StringBuilder AppendLine(string? value)=>Append(value).AppendLine();
    public StringBuilder AppendFormat(string format,object? a)=>Append(Formatter.Composite(format,new[]{a}));
    public StringBuilder AppendFormat(string format,object? a,object? b)=>Append(Formatter.Composite(format,new[]{a,b}));
    public StringBuilder AppendFormat(string format,object? a,object? b,object? c)=>Append(Formatter.Composite(format,new[]{a,b,c}));
    public StringBuilder AppendFormat(string format,params object?[] args)=>Append(Formatter.Composite(format,args));
    public StringBuilder Insert(int index,string? value)=>Insert(index,value,1);
    public StringBuilder Insert(int index,string? value,int count){if(index<0||index>length||count<0)throw new ArgumentOutOfRangeException();if(value==null||value.Length==0||count==0)return this;if(count>(maxCapacity-length)/value.Length)throw new ArgumentOutOfRangeException();int size=value.Length*count;Grow(size);for(int i=length-1;i>=index;i--)buffer[i+size]=buffer[i];for(int i=0;i<size;i++)buffer[index+i]=value[i%value.Length];length+=size;return this;}
    public StringBuilder Insert(int index,char value)=>Insert(index,Formatter.ObjectValue(value));
    public StringBuilder Insert(int index,object? value)=>Insert(index,Formatter.ObjectValue(value));
    public StringBuilder Insert(int index,char[]? value)=>Insert(index,value==null?null:new string(value));
    public StringBuilder Insert(int index,char[]? value,int startIndex,int charCount){if(value==null){if(startIndex!=0||charCount!=0)throw new ArgumentNullException("value");return Insert(index,(string?)null);}return Insert(index,new string(value,startIndex,charCount));}
    public StringBuilder Remove(int startIndex,int length){if(startIndex<0||length<0||startIndex>this.length||length>this.length-startIndex)throw new ArgumentOutOfRangeException();for(int i=startIndex+length;i<this.length;i++)buffer[i-length]=buffer[i];this.length-=length;return this;}
    public StringBuilder Replace(char oldChar,char newChar)=>Replace(oldChar,newChar,0,length);
    public StringBuilder Replace(char oldChar,char newChar,int startIndex,int count){if(startIndex<0||count<0||startIndex>length||count>length-startIndex)throw new ArgumentOutOfRangeException();for(int i=startIndex;i<startIndex+count;i++)if(buffer[i]==oldChar)buffer[i]=newChar;return this;}
    public StringBuilder Replace(string oldValue,string? newValue)=>Replace(oldValue,newValue,0,length);
    public StringBuilder Replace(string oldValue,string? newValue,int startIndex,int count){if(oldValue==null)throw new ArgumentNullException("oldValue");if(oldValue.Length==0)throw new ArgumentException("Empty oldValue.");string replaced=StringOps.Replace(ToString(startIndex,count),oldValue,newValue);long next=(long)length-count+replaced.Length;if(next>maxCapacity)throw new ArgumentOutOfRangeException();Remove(startIndex,count);return Insert(startIndex,replaced);}
    public void CopyTo(int sourceIndex,char[] destination,int destinationIndex,int count){if(destination==null)throw new ArgumentNullException("destination");if(sourceIndex<0||count<0||sourceIndex>length||count>length-sourceIndex||destinationIndex<0||destinationIndex>destination.Length||count>destination.Length-destinationIndex)throw new ArgumentOutOfRangeException();for(int i=0;i<count;i++)destination[destinationIndex+i]=buffer[sourceIndex+i];}
    public bool Equals(StringBuilder? other){if(other==null||other.length!=length)return false;for(int i=0;i<length;i++)if(buffer[i]!=other.buffer[i])return false;return true;}
    public StringBuilder Append(bool value)=>Append(value.ToString());
    public StringBuilder Insert(int index,bool value)=>Insert(index,value.ToString());
    public StringBuilder Append(sbyte value)=>Append(value.ToString());
    public StringBuilder Insert(int index,sbyte value)=>Insert(index,value.ToString());
    public StringBuilder Append(byte value)=>Append(value.ToString());
    public StringBuilder Insert(int index,byte value)=>Insert(index,value.ToString());
    public StringBuilder Append(short value)=>Append(value.ToString());
    public StringBuilder Insert(int index,short value)=>Insert(index,value.ToString());
    public StringBuilder Append(ushort value)=>Append(value.ToString());
    public StringBuilder Insert(int index,ushort value)=>Insert(index,value.ToString());
    public StringBuilder Append(int value)=>Append(value.ToString());
    public StringBuilder Insert(int index,int value)=>Insert(index,value.ToString());
    public StringBuilder Append(uint value)=>Append(value.ToString());
    public StringBuilder Insert(int index,uint value)=>Insert(index,value.ToString());
    public StringBuilder Append(long value)=>Append(value.ToString());
    public StringBuilder Insert(int index,long value)=>Insert(index,value.ToString());
    public StringBuilder Append(ulong value)=>Append(value.ToString());
    public StringBuilder Insert(int index,ulong value)=>Insert(index,value.ToString());
    public StringBuilder Append(float value)=>Append(value.ToString());
    public StringBuilder Insert(int index,float value)=>Insert(index,value.ToString());
    public StringBuilder Append(double value)=>Append(value.ToString());
    public StringBuilder Insert(int index,double value)=>Insert(index,value.ToString());
    public StringBuilder AppendFormat(IFormatProvider? provider,string format,object? a)=>Append(Formatter.Composite(format,new[]{a},provider));
    public StringBuilder AppendFormat(IFormatProvider? provider,string format,object? a,object? b)=>Append(Formatter.Composite(format,new[]{a,b},provider));
    public StringBuilder AppendFormat(IFormatProvider? provider,string format,object? a,object? b,object? c)=>Append(Formatter.Composite(format,new[]{a,b,c},provider));
    public StringBuilder AppendFormat(IFormatProvider? provider,string format,params object?[] args)=>Append(Formatter.Composite(format,args,provider));
    public StringBuilder Append(StringBuilder? value,int startIndex,int count){if(value==null){if(startIndex==0&&count==0)return this;throw new ArgumentNullException("value");}return Append(value.ToString(startIndex,count));}
    public StringBuilder AppendJoin(string? separator,params string?[] values)=>Append(StringOps.Join(separator,values));
    public StringBuilder AppendJoin(string? separator,params object?[] values)=>Append(StringOps.Join(separator,values));
    public StringBuilder AppendJoin(char separator,params string?[] values)=>Append(StringOps.Join(separator,values));
    public StringBuilder AppendJoin(char separator,params object?[] values)=>Append(StringOps.Join(separator,values));
}
