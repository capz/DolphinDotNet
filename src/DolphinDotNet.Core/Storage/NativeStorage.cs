using System;
namespace Dolphin.Storage;
// Only this bridge is replaced by compiler bindings. Application methods remain managed C#.
internal static class NativeStorage
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static bool Mount(int device)=>throw Unsupported();
    internal static void Unmount(int device)=>throw Unsupported();
    internal static bool IsMounted(int device)=>throw Unsupported();
    internal static string GetPath(int device,string path)=>throw Unsupported();
    internal static string FullPath(string path)=>throw Unsupported();
    internal static bool Exists(string path,bool directory)=>throw Unsupported();
    internal static byte[] ReadAllBytes(string path)=>throw Unsupported();
    internal static void WriteAllBytes(string path,byte[] data)=>throw Unsupported();
    internal static void Delete(string path,bool directory)=>throw Unsupported();
    internal static void Move(string source,string destination)=>throw Unsupported();
    internal static void CreateDirectory(string path)=>throw Unsupported();
    internal static int Open(string path,int mode,int access,int share)=>throw Unsupported();
    internal static int Read(int handle,byte[] data,int offset,int count)=>throw Unsupported();
    internal static void Write(int handle,byte[] data,int offset,int count)=>throw Unsupported();
    internal static long Seek(int handle,long offset,int origin)=>throw Unsupported();
    internal static long Length(int handle)=>throw Unsupported();
    internal static void SetLength(int handle,long length)=>throw Unsupported();
    internal static void Flush(int handle)=>throw Unsupported();
    internal static void Close(int handle)=>throw Unsupported();
    internal static int DirectoryOpen(string path)=>throw Unsupported();
    internal static string? DirectoryNext(int handle,int kind)=>throw Unsupported();
    internal static void DirectoryClose(int handle)=>throw Unsupported();
    internal static string GetCurrentDirectory()=>throw Unsupported();
    internal static void SetCurrentDirectory(string path)=>throw Unsupported();
    internal static bool CardMount(int device,string gameCode,string companyCode)=>throw Unsupported();
    internal static byte[] CardRead(int device,string name)=>throw Unsupported();
    internal static void CardWrite(int device,string name,byte[] data)=>throw Unsupported();
    internal static int CardLength(int device,string name)=>throw Unsupported();
    internal static void CardWriteSave(int device,string name,byte[] data,string title,string comment,byte[]? banner,byte[]? icon)=>throw Unsupported();
    internal static void CardDelete(int device,string name)=>throw Unsupported();
    internal static string[] CardEntries(int device)=>throw Unsupported();
}
