using System;
using System.IO;
using Dolphin.Text;
namespace Dolphin.IO;
public abstract class TextReader : IDisposable
{
    public virtual int Read()=>-1;
    public virtual int Peek()=>-1;
    public virtual int Read(char[] buffer,int index,int count){if(buffer==null)throw new ArgumentNullException("buffer");if(index<0||count<0)throw new ArgumentOutOfRangeException();if(index>buffer.Length-count)throw new ArgumentException("Invalid range.");var n=0;while(n<count){var c=Read();if(c<0)break;buffer[index+n++]=(char)c;}return n;}
    public int ReadBlock(char[] buffer,int index,int count){var total=0;while(total<count){var n=Read(buffer,index+total,count-total);if(n==0)break;total+=n;}return total;}
    public virtual string ReadToEnd(){var chars=new char[1024];var count=0;int c;while((c=Read())>=0){if(count==chars.Length){var next=new char[chars.Length*2];Array.Copy(chars,0,next,0,count);chars=next;}chars[count++]=(char)c;}return NativeText.FromChars(chars,0,count);}
    public virtual string? ReadLine(){if(Peek()<0)return null;var chars=new char[128];var count=0;int c;while((c=Read())>=0){if(c=='\n')break;if(c=='\r'){if(Peek()=='\n')Read();break;}if(count==chars.Length){var next=new char[chars.Length*2];Array.Copy(chars,0,next,0,count);chars=next;}chars[count++]=(char)c;}return NativeText.FromChars(chars,0,count);}
    public virtual Dolphin.Threading.Tasks.Task<int> ReadAsync(char[] buffer,int index,int count){if(buffer==null)throw new ArgumentNullException("buffer");if(index<0||count<0||index>buffer.Length-count)throw new ArgumentOutOfRangeException();return Dolphin.Threading.Tasks.Task.Run(()=>Read(buffer,index,count>1024?1024:count));}
    public virtual Dolphin.Threading.Tasks.Task<string?> ReadLineAsync(){var work=new ReadWork(this,true);Dolphin.Threading.Tasks.Scheduler.Post(work.Step);return work.Completion.Task;}
    public virtual Dolphin.Threading.Tasks.Task<string> ReadToEndAsync(){var work=new ReadWork(this,false);Dolphin.Threading.Tasks.Scheduler.Post(work.Step);return work.Completion.Task!;}
    private sealed class ReadWork
    {
        private readonly TextReader reader;private readonly bool line;private string text="";private bool any;
        internal readonly Dolphin.Threading.Tasks.TaskCompletionSource<string?> Completion=new Dolphin.Threading.Tasks.TaskCompletionSource<string?>();
        internal ReadWork(TextReader reader,bool line){this.reader=reader;this.line=line;}
        internal void Step(){try{var chars=new char[1024];var n=0;var done=false;while(n<chars.Length){var c=reader.Read();if(c<0){done=true;break;}any=true;if(line&&(c==10||c==13)){if(c==13&&reader.Peek()==10)reader.Read();done=true;break;}chars[n++]=(char)c;}text=string.Concat(text,NativeText.FromChars(chars,0,n));if(done)Completion.SetResult(line&&!any?null:text);else Dolphin.Threading.Tasks.Scheduler.Post(Step);}catch(Exception error){Completion.SetException(error);}}
    }
    public virtual void Close()=>Dispose();
    public virtual void Dispose(){}
}
public abstract class TextWriter : IDisposable
{
    public virtual string NewLine {get;set;}="\n";
    public abstract Encoding Encoding {get;}
    public virtual void Write(char value){}
    public virtual void Write(string? value){if(value!=null)for(var i=0;i<value.Length;i++)Write(value[i]);}
    public virtual void Write(char[] buffer){if(buffer!=null)Write(buffer,0,buffer.Length);}
    public virtual void Write(char[] buffer,int index,int count){if(buffer==null)throw new ArgumentNullException("buffer");if(index<0||count<0)throw new ArgumentOutOfRangeException();if(index>buffer.Length-count)throw new ArgumentException("Invalid range.");for(var i=0;i<count;i++)Write(buffer[index+i]);}
    public virtual void WriteLine()=>Write(NewLine);
    public virtual void WriteLine(string? value){Write(value);WriteLine();}
    public virtual void WriteLine(char value){Write(value);WriteLine();}
    public virtual void Flush(){}
    public virtual Dolphin.Threading.Tasks.Task WriteAsync(char value)=>WriteAsync(NativeText.FromChars(new[]{value},0,1));
    public virtual Dolphin.Threading.Tasks.Task WriteAsync(string? value){var work=new WriteWork(this,value??"",false);Dolphin.Threading.Tasks.Scheduler.Post(work.Step);return work.Completion.Task;}
    public virtual Dolphin.Threading.Tasks.Task WriteAsync(char[] buffer,int index,int count)=>WriteAsync(NativeText.FromChars(buffer,index,count));
    public virtual Dolphin.Threading.Tasks.Task WriteLineAsync()=>WriteLineAsync("");
    public virtual Dolphin.Threading.Tasks.Task WriteLineAsync(char value)=>WriteLineAsync(NativeText.FromChars(new[]{value},0,1));
    public virtual Dolphin.Threading.Tasks.Task WriteLineAsync(string? value){var work=new WriteWork(this,value??"",true);Dolphin.Threading.Tasks.Scheduler.Post(work.Step);return work.Completion.Task;}
    public virtual Dolphin.Threading.Tasks.Task WriteLineAsync(char[] buffer,int index,int count)=>WriteLineAsync(NativeText.FromChars(buffer,index,count));
    public virtual Dolphin.Threading.Tasks.Task FlushAsync()=>Dolphin.Threading.Tasks.Task.Run(Flush);
    private sealed class WriteWork
    {
        private readonly TextWriter writer;private readonly string text;private readonly bool line;private int offset;
        internal readonly Dolphin.Threading.Tasks.TaskCompletionSource<int> Completion=new Dolphin.Threading.Tasks.TaskCompletionSource<int>();
        internal WriteWork(TextWriter writer,string text,bool line){this.writer=writer;this.text=text;this.line=line;}
        internal void Step(){try{var n=text.Length-offset;if(n>1024)n=1024;writer.Write(text.Substring(offset,n));offset+=n;if(offset<text.Length){Dolphin.Threading.Tasks.Scheduler.Post(Step);return;}if(line)writer.WriteLine();Completion.SetResult(0);}catch(Exception error){Completion.SetException(error);}}
    }
    public virtual void Close()=>Dispose();
    public virtual void Dispose(){}
}
public sealed class StreamWriter : TextWriter
{
    private readonly Stream stream;private readonly Encoding encoding;private readonly bool leaveOpen;
    private readonly char[] chars;private int count;private bool disposed;
    public StreamWriter(string path):this(path,false){}
    public StreamWriter(string path,bool append):this(path,append,new UTF8Encoding(false)){}
    public StreamWriter(string path,bool append,Encoding encoding):this(path,append,encoding,1024){}
    public StreamWriter(string path,bool append,Encoding encoding,int bufferSize):this(Open(path,append,encoding,bufferSize),encoding,bufferSize,false){}
    private static Stream Open(string path,bool append,Encoding encoding,int size){if(encoding==null)throw new ArgumentNullException("encoding");if(size<=0)throw new ArgumentOutOfRangeException("bufferSize");return new FileStream(path,append?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.Read);}
    public StreamWriter(Stream stream):this(stream,new UTF8Encoding(false),1024,false){}
    public StreamWriter(Stream stream,Encoding encoding):this(stream,encoding,1024,false){}
    public StreamWriter(Stream stream,Encoding encoding,int bufferSize):this(stream,encoding,bufferSize,false){}
    public StreamWriter(Stream stream,Encoding encoding,int bufferSize,bool leaveOpen){if(stream==null)throw new ArgumentNullException("stream");if(encoding==null)throw new ArgumentNullException("encoding");if(bufferSize<=0)throw new ArgumentOutOfRangeException("bufferSize");if(!stream.CanWrite)throw new ArgumentException("Stream is not writable.");this.stream=stream;this.encoding=encoding;this.leaveOpen=leaveOpen;chars=new char[bufferSize<2?2:bufferSize];if(!stream.CanSeek||stream.Position==0){var preamble=encoding.GetPreamble();stream.Write(preamble,0,preamble.Length);}}
    public Stream BaseStream=>stream;
    public override Encoding Encoding=>encoding;
    public bool AutoFlush {get;set;}
    private void Check(){if(disposed)throw new ObjectDisposedException("StreamWriter");}
    private void FlushBuffer(bool final){var n=count;if(!final&&n>0&&chars[n-1]>=0xd800&&chars[n-1]<=0xdbff)n--;if(n>0){var bytes=encoding.GetBytes(chars,0,n);stream.Write(bytes,0,bytes.Length);}if(n<count)chars[0]=chars[n];count-=n;}
    public override void Write(char value){Check();if(count==chars.Length)FlushBuffer(false);chars[count++]=value;if(AutoFlush){FlushBuffer(false);stream.Flush();}}
    public override void Write(string? value){Check();if(value!=null)for(var i=0;i<value.Length;i++)Write(value[i]);}
    public override void Flush(){Check();FlushBuffer(true);stream.Flush();}
    public override void Dispose(){if(disposed)return;try{Flush();}finally{disposed=true;if(!leaveOpen)stream.Dispose();}}
}
public sealed class StreamReader : TextReader
{
    private readonly Stream stream;private Encoding encoding;private readonly bool leaveOpen,detectBom;
    private readonly byte[] bytes;private int byteIndex,byteCount;private bool started,disposed;
    private readonly int[] prefix=new int[4];private int prefixIndex,prefixCount;
    private int cached=-2,low=-1,pushed=-1,pendingUnit=-1;
    public StreamReader(string path):this(path,Encoding.UTF8,true,1024){}
    public StreamReader(string path,Encoding encoding):this(path,encoding,true,1024){}
    public StreamReader(string path,Encoding encoding,bool detectEncodingFromByteOrderMarks):this(path,encoding,detectEncodingFromByteOrderMarks,1024){}
    public StreamReader(string path,Encoding encoding,bool detectEncodingFromByteOrderMarks,int bufferSize):this(Open(path,encoding,bufferSize),encoding,detectEncodingFromByteOrderMarks,bufferSize,false){}
    private static Stream Open(string path,Encoding encoding,int size){if(encoding==null)throw new ArgumentNullException("encoding");if(size<=0)throw new ArgumentOutOfRangeException("bufferSize");return File.OpenRead(path);}
    public StreamReader(Stream stream):this(stream,Encoding.UTF8,true,1024,false){}
    public StreamReader(Stream stream,Encoding encoding):this(stream,encoding,true,1024,false){}
    public StreamReader(Stream stream,Encoding encoding,bool detectEncodingFromByteOrderMarks):this(stream,encoding,detectEncodingFromByteOrderMarks,1024,false){}
    public StreamReader(Stream stream,Encoding encoding,bool detectEncodingFromByteOrderMarks,int bufferSize):this(stream,encoding,detectEncodingFromByteOrderMarks,bufferSize,false){}
    public StreamReader(Stream stream,Encoding encoding,bool detectEncodingFromByteOrderMarks,int bufferSize,bool leaveOpen){if(stream==null)throw new ArgumentNullException("stream");if(encoding==null)throw new ArgumentNullException("encoding");if(bufferSize<=0)throw new ArgumentOutOfRangeException("bufferSize");if(!stream.CanRead)throw new ArgumentException("Stream is not readable.");this.stream=stream;this.encoding=encoding;this.leaveOpen=leaveOpen;detectBom=detectEncodingFromByteOrderMarks;bytes=new byte[bufferSize];}
    public Stream BaseStream=>stream;
    public Encoding CurrentEncoding=>encoding;
    public bool EndOfStream=>Peek()<0;
    private void Check(){if(disposed)throw new ObjectDisposedException("StreamReader");}
    private int RawByte(){if(byteIndex==byteCount){byteCount=stream.Read(bytes,0,bytes.Length);byteIndex=0;if(byteCount==0)return -1;}return bytes[byteIndex++];}
    private void Start(){if(started)return;started=true;while(prefixCount<4){var b=RawByte();if(b<0)break;prefix[prefixCount++]=b;}if((detectBom||encoding.CodePage==65001)&&prefixCount>=3&&prefix[0]==239&&prefix[1]==187&&prefix[2]==191){encoding=new UTF8Encoding(false,encoding.Strict);prefixIndex=3;}else if((detectBom||encoding.CodePage==1200)&&prefixCount>=2&&prefix[0]==255&&prefix[1]==254){if(prefixCount==4&&prefix[2]==0&&prefix[3]==0)throw new NotSupportedException("UTF-32 BOM is not supported.");encoding=new UnicodeEncoding(false,false,encoding.Strict);prefixIndex=2;}else if((detectBom||encoding.CodePage==1201)&&prefixCount>=2&&prefix[0]==254&&prefix[1]==255){encoding=new UnicodeEncoding(true,false,encoding.Strict);prefixIndex=2;}else if(detectBom&&prefixCount==4&&prefix[0]==0&&prefix[1]==0&&prefix[2]==254&&prefix[3]==255)throw new NotSupportedException("UTF-32 BOM is not supported.");}
    private int NextByte(){if(pushed>=0){var b=pushed;pushed=-1;return b;}return prefixIndex<prefixCount?prefix[prefixIndex++]:RawByte();}
    private int Invalid(){if(encoding.Strict)throw new System.Text.DecoderFallbackException("Invalid text sequence.");return 0xfffd;}
    private int Unit(){var a=NextByte();if(a<0)return -1;var b=NextByte();if(b<0)return Invalid();return encoding.CodePage==1200?a|(b<<8):(a<<8)|b;}
    private int Decode()
    {
        Start();if(low>=0){var c=low;low=-1;return c;}
        var code=encoding.CodePage;
        if(code==1200||code==1201){var c=pendingUnit>=0?pendingUnit:Unit();pendingUnit=-1;if(c>=0xd800&&c<=0xdbff){var d=Unit();if(d<0xdc00||d>0xdfff){if(d>=0)pendingUnit=d;return Invalid();}low=d;return c;}return c>=0xdc00&&c<=0xdfff?Invalid():c;}
        var first=NextByte();if(first<0)return -1;if(code==20127)return first<=127?first:encoding.Strict?Invalid():'?';if(code==28591)return first;if(code!=65001)throw new NotSupportedException("Encoding is unavailable.");if(first<128)return first;
        var need=first>=0xc2&&first<=0xdf?1:first>=0xe0&&first<=0xef?2:first>=0xf0&&first<=0xf4?3:0;if(need==0)return Invalid();var cp=first&(need==1?31:need==2?15:7);
        for(var i=0;i<need;i++){var b=NextByte();if(b<128||b>191||i==0&&(first==0xe0&&b<160||first==0xed&&b>159||first==0xf0&&b<144||first==0xf4&&b>143)){if(b>=0)pushed=b;return Invalid();}cp=(cp<<6)|(b&63);}
        if(cp>0xffff){cp-=0x10000;low=0xdc00+(cp&1023);return 0xd800+(cp>>10);}return cp;
    }
    public override int Read(){Check();if(cached!=-2){var c=cached;cached=-2;return c;}return Decode();}
    public override int Peek(){Check();if(cached==-2)cached=Decode();return cached;}
    public void DiscardBufferedData(){Check();byteIndex=byteCount=prefixIndex=prefixCount=0;cached=-2;low=pushed=pendingUnit=-1;}
    public override void Dispose(){if(disposed)return;disposed=true;if(!leaveOpen)stream.Dispose();}
}
