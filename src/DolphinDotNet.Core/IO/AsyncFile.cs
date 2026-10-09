using System;
using System.Collections.Generic;
using Dolphin.Threading;
using Dolphin.Threading.Tasks;
using Dolphin.Text;
namespace Dolphin.IO;
public static partial class File
{
    public static Task<byte[]> ReadAllBytesAsync(string path)=>ReadAllBytesAsync(path,default);
    public static Task<byte[]> ReadAllBytesAsync(string path,CancellationToken token){if(path==null)throw new ArgumentNullException("path");var work=new ReadWork(path,token);TasksPost(work.Step);return work.Completion.Task;}
    public static Task WriteAllBytesAsync(string path,byte[] bytes)=>WriteAllBytesAsync(path,bytes,default);
    public static Task WriteAllBytesAsync(string path,byte[] bytes,CancellationToken token){if(path==null)throw new ArgumentNullException("path");if(bytes==null)throw new ArgumentNullException("bytes");var work=new WriteFileWork(path,bytes,false,token);TasksPost(work.Step);return work.Completion.Task;}
    public static Task<string> ReadAllTextAsync(string path)=>ReadAllTextAsync(path,Encoding.UTF8,default);
    public static Task<string> ReadAllTextAsync(string path,CancellationToken token)=>ReadAllTextAsync(path,Encoding.UTF8,token);
    public static Task<string> ReadAllTextAsync(string path,Encoding encoding)=>ReadAllTextAsync(path,encoding,default);
    public static Task<string> ReadAllTextAsync(string path,Encoding encoding,CancellationToken token){if(path==null)throw new ArgumentNullException("path");if(encoding==null)throw new ArgumentNullException("encoding");var work=new ReadTextWork(path,encoding,token);TasksPost(work.Step);return work.Completion.Task;}
    public static Task WriteAllTextAsync(string path,string? contents)=>WriteAllTextAsync(path,contents,new UTF8Encoding(false),default);
    public static Task WriteAllTextAsync(string path,string? contents,CancellationToken token)=>WriteAllTextAsync(path,contents,new UTF8Encoding(false),token);
    public static Task WriteAllTextAsync(string path,string? contents,Encoding encoding)=>WriteAllTextAsync(path,contents,encoding,default);
    public static Task WriteAllTextAsync(string path,string? contents,Encoding encoding,CancellationToken token)=>WriteText(path,contents,encoding,false,token);
    public static Task AppendAllTextAsync(string path,string? contents)=>WriteText(path,contents,new UTF8Encoding(false),true,default);
    public static Task AppendAllTextAsync(string path,string? contents,CancellationToken token)=>WriteText(path,contents,new UTF8Encoding(false),true,token);
    public static Task AppendAllTextAsync(string path,string? contents,Encoding encoding)=>WriteText(path,contents,encoding,true,default);
    public static Task AppendAllTextAsync(string path,string? contents,Encoding encoding,CancellationToken token)=>WriteText(path,contents,encoding,true,token);
    public static Task<string[]> ReadAllLinesAsync(string path)=>ReadAllLinesAsync(path,Encoding.UTF8,default);
    public static Task<string[]> ReadAllLinesAsync(string path,CancellationToken token)=>ReadAllLinesAsync(path,Encoding.UTF8,token);
    public static Task<string[]> ReadAllLinesAsync(string path,Encoding encoding)=>ReadAllLinesAsync(path,encoding,default);
    public static Task<string[]> ReadAllLinesAsync(string path,Encoding encoding,CancellationToken token){if(path==null)throw new ArgumentNullException("path");if(encoding==null)throw new ArgumentNullException("encoding");var work=new ReadLinesWork(path,encoding,token);TasksPost(work.Step);return work.Completion.Task;}
    public static Task WriteAllLinesAsync(string path,IEnumerable<string> contents)=>WriteLines(path,contents,new UTF8Encoding(false),false,default);
    public static Task WriteAllLinesAsync(string path,IEnumerable<string> contents,CancellationToken token)=>WriteLines(path,contents,new UTF8Encoding(false),false,token);
    public static Task WriteAllLinesAsync(string path,IEnumerable<string> contents,Encoding encoding)=>WriteLines(path,contents,encoding,false,default);
    public static Task WriteAllLinesAsync(string path,IEnumerable<string> contents,Encoding encoding,CancellationToken token)=>WriteLines(path,contents,encoding,false,token);
    public static Task AppendAllLinesAsync(string path,IEnumerable<string> contents)=>WriteLines(path,contents,new UTF8Encoding(false),true,default);
    public static Task AppendAllLinesAsync(string path,IEnumerable<string> contents,CancellationToken token)=>WriteLines(path,contents,new UTF8Encoding(false),true,token);
    public static Task AppendAllLinesAsync(string path,IEnumerable<string> contents,Encoding encoding)=>WriteLines(path,contents,encoding,true,default);
    public static Task AppendAllLinesAsync(string path,IEnumerable<string> contents,Encoding encoding,CancellationToken token)=>WriteLines(path,contents,encoding,true,token);
    private static Task WriteLines(string path,IEnumerable<string> contents,Encoding encoding,bool append,CancellationToken token){if(path==null)throw new ArgumentNullException("path");if(contents==null)throw new ArgumentNullException("contents");if(encoding==null)throw new ArgumentNullException("encoding");var work=new WriteLinesWork(path,contents,encoding,append,token);TasksPost(work.Step);return work.Completion.Task;}
    private sealed class ReadLinesWork
    {
        private readonly string path;private readonly Encoding encoding;private readonly CancellationToken token;private StreamReader? reader;private Task<string?>? pending;private readonly List<string> lines=new List<string>();
        internal readonly TaskCompletionSource<string[]> Completion=new TaskCompletionSource<string[]>();
        internal ReadLinesWork(string path,Encoding encoding,CancellationToken token){this.path=path;this.encoding=encoding;this.token=token;}
        internal void Step(){try{token.ThrowIfCancellationRequested();if(reader==null)reader=new StreamReader(path,encoding);pending=reader.ReadLineAsync();pending.Continue(Consume);}catch(Exception error){Fail(error);}}
        private void Consume(){try{token.ThrowIfCancellationRequested();var line=pending!.Result;if(line==null){reader!.Dispose();Completion.SetResult(lines.ToArray());}else{lines.Add(line);TasksPost(Step);}}catch(Exception error){Fail(error);}}
        private void Fail(Exception error){if(reader!=null)reader.Dispose();Completion.SetException(error);}
    }
    private sealed class WriteLinesWork
    {
        private readonly string path;private readonly IEnumerable<string> contents;private readonly Encoding encoding;private readonly bool append;private readonly CancellationToken token;private StreamWriter? writer;private IEnumerator<string>? iterator;private string? line;private int offset;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal WriteLinesWork(string path,IEnumerable<string> contents,Encoding encoding,bool append,CancellationToken token){this.path=path;this.contents=contents;this.encoding=encoding;this.append=append;this.token=token;}
        private void Close(){try{if(iterator!=null)iterator.Dispose();}finally{if(writer!=null)writer.Dispose();}}
        internal void Step(){try{token.ThrowIfCancellationRequested();if(writer==null){writer=new StreamWriter(path,append,encoding);iterator=contents.GetEnumerator();}if(line==null){if(!iterator!.MoveNext()){Close();Completion.SetResult(0);return;}line=iterator.Current??"";offset=0;}var n=line.Length-offset;if(n>1024)n=1024;writer.Write(line.Substring(offset,n));offset+=n;if(offset==line.Length){writer.WriteLine();line=null;}TasksPost(Step);}catch(Exception error){try{Close();}catch(Exception closeError){error=closeError;}Completion.SetException(error);}}
    }
    private static Task WriteText(string path,string? contents,Encoding encoding,bool append,CancellationToken token){if(path==null)throw new ArgumentNullException("path");if(encoding==null)throw new ArgumentNullException("encoding");var work=new WriteTextWork(path,contents??"",encoding,append,token);TasksPost(work.Step);return work.Completion.Task;}
    private static void TasksPost(Action action)=>Scheduler.Post(action);
    private sealed class ReadWork
    {
        private readonly string path;private readonly CancellationToken token;private FileStream? stream;private readonly MemoryStream output=new MemoryStream();private readonly byte[] buffer=new byte[4096];
        internal readonly TaskCompletionSource<byte[]> Completion=new TaskCompletionSource<byte[]>();
        internal ReadWork(string path,CancellationToken token){this.path=path;this.token=token;}
        internal void Step(){try{token.ThrowIfCancellationRequested();if(stream==null)stream=OpenRead(path);var n=stream.Read(buffer,0,buffer.Length);if(n!=0){output.Write(buffer,0,n);TasksPost(Step);return;}stream.Dispose();Completion.SetResult(output.ToArray());}catch(Exception error){if(stream!=null)stream.Dispose();Completion.SetException(error);}}
    }
    private sealed class WriteFileWork
    {
        private readonly string path;private readonly byte[] bytes;private readonly bool append;private readonly CancellationToken token;private FileStream? stream;private int offset;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal WriteFileWork(string path,byte[] bytes,bool append,CancellationToken token){this.path=path;this.bytes=bytes;this.append=append;this.token=token;}
        internal void Step(){try{token.ThrowIfCancellationRequested();if(stream==null)stream=Open(path,append?System.IO.FileMode.Append:System.IO.FileMode.Create,System.IO.FileAccess.Write,System.IO.FileShare.None);var n=bytes.Length-offset;if(n>4096)n=4096;stream.Write(bytes,offset,n);offset+=n;if(offset<bytes.Length){TasksPost(Step);return;}stream.Dispose();Completion.SetResult(0);}catch(Exception error){if(stream!=null)stream.Dispose();Completion.SetException(error);}}
    }
    private sealed class ReadTextWork
    {
        private readonly string path;private readonly Encoding encoding;private readonly CancellationToken token;private StreamReader? reader;private readonly char[] buffer=new char[1024];private string result="";
        internal readonly TaskCompletionSource<string> Completion=new TaskCompletionSource<string>();
        internal ReadTextWork(string path,Encoding encoding,CancellationToken token){this.path=path;this.encoding=encoding;this.token=token;}
        internal void Step(){try{token.ThrowIfCancellationRequested();if(reader==null)reader=new StreamReader(path,encoding);var n=reader.Read(buffer,0,buffer.Length);if(n>0){result=string.Concat(result,NativeText.FromChars(buffer,0,n));TasksPost(Step);return;}reader.Dispose();Completion.SetResult(result);}catch(Exception error){if(reader!=null)reader.Dispose();Completion.SetException(error);}}
    }
    private sealed class WriteTextWork
    {
        private readonly string path,contents;private readonly Encoding encoding;private readonly bool append;private readonly CancellationToken token;private StreamWriter? writer;private int offset;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal WriteTextWork(string path,string contents,Encoding encoding,bool append,CancellationToken token){this.path=path;this.contents=contents;this.encoding=encoding;this.append=append;this.token=token;}
        internal void Step(){try{token.ThrowIfCancellationRequested();if(writer==null)writer=new StreamWriter(path,append,encoding);var n=contents.Length-offset;if(n>1024)n=1024;writer.Write(contents.Substring(offset,n));offset+=n;if(offset<contents.Length){TasksPost(Step);return;}writer.Dispose();Completion.SetResult(0);}catch(Exception error){if(writer!=null)writer.Dispose();Completion.SetException(error);}}
    }
}
