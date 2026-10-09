using System;
using System.Collections.Generic;
using Dolphin.Text;
namespace Dolphin.IO;
public static partial class File
{
    public static string ReadAllText(string path)=>ReadAllText(path,Encoding.UTF8);
    public static string ReadAllText(string path,Encoding encoding){using(var reader=new StreamReader(path,encoding))return reader.ReadToEnd();}
    public static void WriteAllText(string path,string? contents)=>WriteAllText(path,contents,new UTF8Encoding(false));
    public static void WriteAllText(string path,string? contents,Encoding encoding){using(var writer=new StreamWriter(path,false,encoding))writer.Write(contents);}
    public static void AppendAllText(string path,string? contents)=>AppendAllText(path,contents,new UTF8Encoding(false));
    public static void AppendAllText(string path,string? contents,Encoding encoding){using(var writer=new StreamWriter(path,true,encoding))writer.Write(contents);}
    public static string[] ReadAllLines(string path)=>ReadAllLines(path,Encoding.UTF8);
    public static string[] ReadAllLines(string path,Encoding encoding){var lines=new Dolphin.Collections.List<string>();using(var reader=new StreamReader(path,encoding)){string? line;while((line=reader.ReadLine())!=null)lines.Add(line);}return lines.ToArray();}
    public static void WriteAllLines(string path,string[] contents)=>WriteAllLines(path,(IEnumerable<string>)contents);
    public static void WriteAllLines(string path,string[] contents,Encoding encoding)=>WriteAllLines(path,(IEnumerable<string>)contents,encoding);
    public static void WriteAllLines(string path,IEnumerable<string> contents)=>WriteAllLines(path,contents,new UTF8Encoding(false));
    public static void WriteAllLines(string path,IEnumerable<string> contents,Encoding encoding){if(contents==null)throw new ArgumentNullException("contents");using(var writer=new StreamWriter(path,false,encoding))foreach(var line in contents)writer.WriteLine(line);}
    public static void AppendAllLines(string path,IEnumerable<string> contents)=>AppendAllLines(path,contents,new UTF8Encoding(false));
    public static void AppendAllLines(string path,IEnumerable<string> contents,Encoding encoding){if(contents==null)throw new ArgumentNullException("contents");using(var writer=new StreamWriter(path,true,encoding))foreach(var line in contents)writer.WriteLine(line);}
    public static IEnumerable<string> ReadLines(string path)=>ReadLines(path,Encoding.UTF8);
    public static IEnumerable<string> ReadLines(string path,Encoding encoding)=>new Lines(path,encoding);
    private sealed class Lines : IEnumerable<string>
    {
        private readonly string path;private readonly Encoding encoding;
        public Lines(string path,Encoding encoding){if(path==null)throw new ArgumentNullException("path");if(encoding==null)throw new ArgumentNullException("encoding");this.path=path;this.encoding=encoding;}
        public IEnumerator<string> GetEnumerator()=>new LineEnumerator(path,encoding);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    private sealed class LineEnumerator : IEnumerator<string>
    {
        private StreamReader? reader;private string? current;
        public LineEnumerator(string path,Encoding encoding){reader=new StreamReader(path,encoding);}
        public string Current=>current??throw new InvalidOperationException();
        object System.Collections.IEnumerator.Current=>Current;
        public bool MoveNext(){if(reader==null)return false;try{current=reader.ReadLine();if(current==null)Dispose();return current!=null;}catch{Dispose();throw;}}
        public void Reset()=>throw new NotSupportedException();
        public void Dispose(){if(reader!=null){var r=reader;reader=null;current=null;r.Dispose();}}
    }
}
