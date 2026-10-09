using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Dolphin.Storage;
namespace Dolphin.IO;

public static class Path
{
    public static string GetFullPath(string path)=>NativeStorage.FullPath(path);
    public static bool IsPathRooted(string path)=>path!=null && path.Length>0 && (path[0]=='/' || path[0]=='\\' || path.IndexOf(":")>=0);
    public static string Combine(string first,string second)
    {
        if(first==null || second==null)throw new ArgumentNullException("path");
        if(IsPathRooted(second))return second;
        if(first.Length==0)return second;
        var last=first[first.Length-1];
        return string.Concat(first,string.Concat(last=='/' || last=='\\'?"":"/",second));
    }
    public static string GetFileName(string path)
    {
        if(path==null)return null!;
        for(var i=path.Length-1;i>=0;i--)if(path[i]=='/' || path[i]=='\\' || path[i]==':')return path.Substring(i+1);
        return path;
    }
    public static string? GetDirectoryName(string path)
    {
        if(path==null)return null;
        if(path.Length==0 || path=="/" || path=="\\" || path.Length>1 && path[path.Length-1]=='/' && path[path.Length-2]==':')return null;
        for(var i=path.Length-1;i>=0;i--)if(path[i]=='/' || path[i]=='\\')return i==0?"/":i>0 && path[i-1]==':'?path.Substring(0,i+1):path.Substring(0,i);
        return "";
    }
    public static string GetExtension(string path)
    {
        var name=GetFileName(path);if(name==null)return null!;
        for(var i=name.Length-1;i>=0;i--)if(name[i]=='.')return i==name.Length-1?"":name.Substring(i);
        return "";
    }
}
public sealed class FileStream : Stream
{
    private int handle;
    private readonly FileAccess access;
    public FileStream(string path,FileMode mode):this(path,mode,mode==FileMode.Append?FileAccess.Write:FileAccess.ReadWrite,FileShare.None){}
    public FileStream(string path,FileMode mode,FileAccess access):this(path,mode,access,FileShare.None){}
    public FileStream(string path,FileMode mode,FileAccess access,FileShare share){this.access=access;handle=NativeStorage.Open(path,(int)mode,(int)access,(int)share);}
    private void Check(){if(handle==0)throw new ObjectDisposedException("FileStream");}
    public override bool CanRead=>handle!=0 && access!=FileAccess.Write;
    public override bool CanWrite=>handle!=0 && access!=FileAccess.Read;
    public override bool CanSeek=>handle!=0;
    public override long Length {get {Check();return NativeStorage.Length(handle);}}
    public override long Position {get {Check();return NativeStorage.Seek(handle,0,1);}set {Seek(value,SeekOrigin.Begin);}}
    public override int Read(byte[] buffer,int offset,int count){Check();return NativeStorage.Read(handle,buffer,offset,count);}
    public override void Write(byte[] buffer,int offset,int count){Check();NativeStorage.Write(handle,buffer,offset,count);}
    public override long Seek(long offset,SeekOrigin origin){Check();return NativeStorage.Seek(handle,offset,(int)origin);}
    public override void SetLength(long length){Check();NativeStorage.SetLength(handle,length);}
    public override void Flush(){Check();NativeStorage.Flush(handle);}
    public override void Close()=>Dispose();
    public override void Dispose(){if(handle!=0){var h=handle;handle=0;NativeStorage.Close(h);}}
}
public static partial class File
{
    public static bool Exists(string path)=>NativeStorage.Exists(path,false);
    public static byte[] ReadAllBytes(string path)=>NativeStorage.ReadAllBytes(path);
    public static void WriteAllBytes(string path,byte[] data)=>NativeStorage.WriteAllBytes(path,data);
    public static FileStream Open(string path,FileMode mode)=>new FileStream(path,mode);
    public static FileStream Open(string path,FileMode mode,FileAccess access)=>new FileStream(path,mode,access);
    public static FileStream Open(string path,FileMode mode,FileAccess access,FileShare share)=>new FileStream(path,mode,access,share);
    public static FileStream OpenRead(string path)=>new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
    public static FileStream OpenWrite(string path)=>new FileStream(path,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
    public static FileStream Create(string path)=>new FileStream(path,FileMode.Create,FileAccess.ReadWrite,FileShare.None);
    public static void Delete(string path)=>NativeStorage.Delete(path,false);
    public static void Move(string source,string destination)
    {
        var from=Path.GetFullPath(source);var to=Path.GetFullPath(destination);
        if(from.Substring(0,from.IndexOf(":"))==to.Substring(0,to.IndexOf(":"))){NativeStorage.Move(from,to);return;}
        Copy(from,to,false);Delete(from);
    }
    public static void Copy(string source,string destination)=>Copy(source,destination,false);
    public static void Copy(string source,string destination,bool overwrite)
    {
        var created=false;
        try
        {
            using(var input=OpenRead(source))
            using(var output=new FileStream(destination,overwrite?FileMode.Create:FileMode.CreateNew,FileAccess.Write))
            {
                created=true;var buffer=new byte[4096];int read;
                while((read=input.Read(buffer,0,buffer.Length))!=0)output.Write(buffer,0,read);
            }
        }
        catch
        {
            if(created && !overwrite){try{Delete(destination);}catch{}}
            throw;
        }
    }
}
public sealed class DirectoryInfo
{
    public string FullName {get;}
    public string Name=>Path.GetFileName(FullName);
    public bool Exists=>Directory.Exists(FullName);
    public DirectoryInfo(string path){FullName=Path.GetFullPath(path);}
    public void Create()=>Directory.CreateDirectory(FullName);
    public void Delete()=>Directory.Delete(FullName);
    public void Delete(bool recursive)=>Directory.Delete(FullName,recursive);
}
public static class Directory
{
    public static bool Exists(string path)=>NativeStorage.Exists(path,true);
    public static DirectoryInfo CreateDirectory(string path){NativeStorage.CreateDirectory(path);return new DirectoryInfo(path);}
    public static void Delete(string path)=>NativeStorage.Delete(path,true);
    public static void Delete(string path,bool recursive)
    {
        if(recursive)DeleteTree(Path.GetFullPath(path),0);else Delete(path);
    }
    private static void DeleteTree(string path,int depth)
    {
        if(Path.GetFileName(path).Length==0)throw new UnauthorizedAccessException("Cannot delete a volume root.");
        if(depth>=64)throw new IOException("Directory nesting exceeds traversal limit.");
        foreach(var file in EnumerateFiles(path))File.Delete(file);
        foreach(var directory in EnumerateDirectories(path))DeleteTree(directory,depth+1);
        Delete(path);
    }
    public static void Move(string source,string destination)=>NativeStorage.Move(source,destination);
    public static string GetCurrentDirectory()=>NativeStorage.GetCurrentDirectory();
    public static void SetCurrentDirectory(string path)=>NativeStorage.SetCurrentDirectory(path);
    public static IEnumerable<string> EnumerateFiles(string path)=>new SearchEnumerable(path,"*",SearchOption.TopDirectoryOnly,1);
    public static IEnumerable<string> EnumerateDirectories(string path)=>new SearchEnumerable(path,"*",SearchOption.TopDirectoryOnly,2);
    public static IEnumerable<string> EnumerateFileSystemEntries(string path)=>new SearchEnumerable(path,"*",SearchOption.TopDirectoryOnly,0);
    public static string[] GetFiles(string path)=>Materialize(EnumerateFiles(path));
    public static string[] GetDirectories(string path)=>Materialize(EnumerateDirectories(path));
    public static string[] GetFileSystemEntries(string path)=>Materialize(EnumerateFileSystemEntries(path));
    public static IEnumerable<string> EnumerateFiles(string path,string pattern)=>EnumerateFiles(path,pattern,SearchOption.TopDirectoryOnly);
    public static IEnumerable<string> EnumerateFiles(string path,string pattern,SearchOption option)=>new SearchEnumerable(path,pattern,option,1);
    public static string[] GetFiles(string path,string pattern)=>Materialize(EnumerateFiles(path,pattern));
    public static string[] GetFiles(string path,string pattern,SearchOption option)=>Materialize(EnumerateFiles(path,pattern,option));
    public static IEnumerable<string> EnumerateDirectories(string path,string pattern)=>EnumerateDirectories(path,pattern,SearchOption.TopDirectoryOnly);
    public static IEnumerable<string> EnumerateDirectories(string path,string pattern,SearchOption option)=>new SearchEnumerable(path,pattern,option,2);
    public static string[] GetDirectories(string path,string pattern)=>Materialize(EnumerateDirectories(path,pattern));
    public static string[] GetDirectories(string path,string pattern,SearchOption option)=>Materialize(EnumerateDirectories(path,pattern,option));
    public static IEnumerable<string> EnumerateFileSystemEntries(string path,string pattern)=>EnumerateFileSystemEntries(path,pattern,SearchOption.TopDirectoryOnly);
    public static IEnumerable<string> EnumerateFileSystemEntries(string path,string pattern,SearchOption option)=>new SearchEnumerable(path,pattern,option,0);
    public static string[] GetFileSystemEntries(string path,string pattern)=>Materialize(EnumerateFileSystemEntries(path,pattern));
    public static string[] GetFileSystemEntries(string path,string pattern,SearchOption option)=>Materialize(EnumerateFileSystemEntries(path,pattern,option));
    private static string[] Materialize(IEnumerable<string> source)
    {
        var items=new Dolphin.Collections.List<string>();foreach(var item in source)items.Add(item);return items.ToArray();
    }
    private sealed class SearchEnumerable : IEnumerable<string>
    {
        private readonly string path,pattern;private readonly SearchOption option;private readonly int kind;
        public SearchEnumerable(string path,string pattern,SearchOption option,int kind)
        {
            if(pattern==null)throw new ArgumentNullException("searchPattern");
            if(pattern.IndexOf("/")>=0||pattern.IndexOf("\\")>=0||pattern.IndexOf(":")>=0||pattern.IndexOf("\0")>=0)throw new ArgumentException("Pattern must be a file name.");
            if(option!=SearchOption.TopDirectoryOnly&&option!=SearchOption.AllDirectories)throw new ArgumentOutOfRangeException("searchOption");
            this.path=Path.GetFullPath(path);this.pattern=pattern=="*.*"?"*":pattern;this.option=option;this.kind=kind;
        }
        public IEnumerator<string> GetEnumerator()=>new SearchEnumerator(path,pattern,option,kind);
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    private sealed class SearchEnumerator : IEnumerator<string>
    {
        private readonly string pattern;private readonly SearchOption option;private readonly int kind;
        private readonly Dolphin.Collections.List<string> pending=new Dolphin.Collections.List<string>();
        private IEnumerator<string>? cursor;private string? current;
        private bool disposed;
        public SearchEnumerator(string path,string pattern,SearchOption option,int kind){this.pattern=pattern;this.option=option;this.kind=kind;cursor=new DirectoryEnumerable(path,0).GetEnumerator();}
        public string Current=>current??throw new InvalidOperationException("Enumerator is not positioned.");
        object IEnumerator.Current=>Current;
        public bool MoveNext()
        {
            if(disposed)return false;
            try
            {
                while(true)
                {
                    while(cursor!=null&&cursor.MoveNext())
                    {
                        var path=cursor.Current;var directory=Directory.Exists(path);
                        if(directory&&option==SearchOption.AllDirectories)pending.Add(path);
                        if((kind==0||kind==1&&!directory||kind==2&&directory)&&Match(Path.GetFileName(path),pattern)){current=path;return true;}
                    }
                    if(cursor!=null){cursor.Dispose();cursor=null;}
                    if(pending.Count==0){Dispose();return false;}
                    var next=pending[pending.Count-1];pending.RemoveAt(pending.Count-1);cursor=new DirectoryEnumerable(next,0).GetEnumerator();
                }
            }
            catch{Dispose();throw;}
        }
        private static bool Match(string name,string pattern)
        {
            var n=0;var p=0;var star=-1;var retry=0;
            while(n<name.Length){if(p<pattern.Length&&(pattern[p]=='?'||pattern[p]==name[n])){n++;p++;}else if(p<pattern.Length&&pattern[p]=='*'){star=p++;retry=n;}else if(star>=0){p=star+1;n=++retry;}else return false;}
            while(p<pattern.Length&&pattern[p]=='*')p++;return p==pattern.Length;
        }
        public void Reset()=>throw new NotSupportedException();
        public void Dispose(){if(disposed)return;disposed=true;current=null;pending.Clear();if(cursor!=null){var c=cursor;cursor=null;c.Dispose();}}
    }
    private sealed class DirectoryEnumerable : IEnumerable<string>
    {
        private readonly string path;private readonly int kind;
        public DirectoryEnumerable(string path,int kind){if(path==null)throw new ArgumentNullException("path");if(path.Length==0)throw new ArgumentException("Empty path.");this.path=Path.GetFullPath(path);this.kind=kind;}
        public IEnumerator<string> GetEnumerator()=>new DirectoryEnumerator(path,kind);
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    private sealed class DirectoryEnumerator : IEnumerator<string>
    {
        private int handle;private readonly int kind;private string? current;
        public DirectoryEnumerator(string path,int kind){this.kind=kind;handle=NativeStorage.DirectoryOpen(path);}
        public string Current=>current??throw new InvalidOperationException("Enumerator is not positioned on an entry.");
        object IEnumerator.Current=>Current;
        public bool MoveNext()
        {
            if(handle==0)return false;
            try {current=NativeStorage.DirectoryNext(handle,kind);if(current==null)Dispose();return current!=null;}
            catch {Dispose();throw;}
        }
        public void Reset()=>throw new NotSupportedException();
        public void Dispose(){current=null;if(handle!=0){var h=handle;handle=0;NativeStorage.DirectoryClose(h);}}
    }
}
