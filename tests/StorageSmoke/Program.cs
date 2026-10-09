using System;
using System.IO;
using System.Collections.Generic;
using Dolphin.Storage;
internal static class Program
{
    private static int Main()
    {
        if(!Storage.Mount(StorageDevice.SdSerialPort2) || !Storage.Mount(StorageDevice.DvdDrive))return 1;
        var list=new Dolphin.Collections.List<long>();ICollection<long> values=list;
        for(var i=0;i<40;i++)values.Add(0x100000000L+i);
        var total=0L;foreach(var value in list)total+=value;if(total!=40*0x100000000L+780)return 2;
        var initial=list.GetEnumerator();try{if(initial.Current!=0)return 21;var unused=((System.Collections.IEnumerator)initial).Current;return 21;}catch(InvalidOperationException){}finally{initial.Dispose();}
        var adapted=new Dolphin.Collections.List<long>(new Dolphin.Collections.ArrayEnumerable<long>(list.ToArray()));if(adapted.Count!=40 || adapted[39]!=0x100000027L)return 22;
        System.Collections.IEnumerable boxedValues=adapted;var boxedTotal=0L;
        foreach(var value in boxedValues)boxedTotal+=(long)value;if(boxedTotal!=total)return 44;
        list.Insert(0,5);list.RemoveAt(0);var destination=new long[41];list.CopyTo(destination,1);if(destination[40]!=0x100000027L)return 23;
        var versioned=list.GetEnumerator();list.Add(7);
        try { versioned.MoveNext();return 3; }catch(InvalidOperationException){}finally{versioned.Dispose();}
        var strings=new Dolphin.Collections.List<string>();strings.Add(string.Concat("same"," value"));if(!strings.Contains(string.Concat("same ","value")))return 4;
        var root=Storage.GetPath(StorageDevice.SdSerialPort2,"test");Directory.CreateDirectory(root);
        var path=Path.Combine(root,"data.bin");var bytes=new byte[3];bytes[0]=9;bytes[1]=8;bytes[2]=7;
        var textPath=Path.Combine(root,"text.txt");
        File.WriteAllText(textPath,"first\r\nsecond\n\u20ac");File.AppendAllText(textPath," tail");
        if(File.ReadAllLines(textPath).Length!=3||File.ReadAllText(textPath)!="first\r\nsecond\n\u20ac tail")return 45;
        foreach(var line in File.ReadLines(textPath)){if(line!="first")return 46;break;}
        var nested=Path.Combine(root,"nested");Directory.CreateDirectory(nested);File.WriteAllText(Path.Combine(nested,"match.txt"),"nested");
        if(Directory.GetFiles(root,"*.txt",SearchOption.AllDirectories).Length!=2||Directory.GetFiles(root,"match.txt").Length!=0)return 47;
        var large=new byte[9000];large[8999]=123;var asyncPath=Path.Combine(root,"async.bin");
        var pendingWrite=File.WriteAllBytesAsync(asyncPath,large);if(pendingWrite.IsCompleted||File.Exists(asyncPath))return 48;pendingWrite.GetAwaiter().GetResult();
        var pendingRead=File.ReadAllBytesAsync(asyncPath);if(pendingRead.IsCompleted)return 49;var asyncBytes=pendingRead.GetAwaiter().GetResult();if(asyncBytes.Length!=9000||asyncBytes[8999]!=123)return 50;
        var cancellation=new System.Threading.CancellationTokenSource();var cancelPath=Path.Combine(root,"cancel.bin");var canceledWrite=File.WriteAllBytesAsync(cancelPath,large,cancellation.Token);cancellation.Cancel();
        try{canceledWrite.GetAwaiter().GetResult();return 51;}catch(OperationCanceledException){}if(File.Exists(cancelPath)||!canceledWrite.IsCanceled)return 52;
        File.WriteAllTextAsync(textPath,"async \ud83d\ude00").GetAwaiter().GetResult();if(File.ReadAllTextAsync(textPath).GetAwaiter().GetResult()!="async \ud83d\ude00")return 53;
        File.Delete(textPath);File.Delete(asyncPath);Directory.Delete(nested,true);
        File.WriteAllBytes(path,bytes);var read=File.ReadAllBytes(path);if(read.Length!=3 || read[1]!=8)return 5;
        if(!File.Exists(path) || File.Exists(root) || !Directory.Exists(root) || File.Exists(null!))return 6;
        using(var stream=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            if(stream.Length!=3)return 7;stream.Seek(1,SeekOrigin.Begin);stream.Write(bytes,2,1);stream.Position=0;
            if(stream.Read(read,0,3)!=3 || read[1]!=7)return 8;
            try { File.OpenRead(path);return 9; }catch(IOException){}
            try { Storage.Unmount(StorageDevice.SdSerialPort2);return 10; }catch(IOException){}
            stream.SetLength(2);stream.Flush();if(stream.Length!=2 || stream.Position!=2)return 11;
            try{stream.Read(bytes,-1,1);return 40;}catch(ArgumentOutOfRangeException){}
            try{stream.Read(bytes,0,4);return 41;}catch(ArgumentException){}
        }
        using(var append=File.Open(path,FileMode.Append,FileAccess.Write))
        {
            append.Write(bytes,0,1);try {append.Seek(0,SeekOrigin.Begin);return 12;}catch(ArgumentException){}
        }
        try{File.ReadAllBytes(Path.Combine(root,"missing.bin"));return 24;}catch(FileNotFoundException){}
        try{File.OpenRead(Path.Combine(root,"missing/missing.bin"));return 25;}catch(DirectoryNotFoundException){}
        try{File.WriteAllBytes(path,null!);return 26;}catch(ArgumentNullException){}if(File.ReadAllBytes(path).Length!=3)return 27;
        try{using(var existing=File.Open(path,FileMode.CreateNew)){}return 28;}catch(IOException){}
        var disposed=File.OpenRead(path);disposed.Dispose();disposed.Dispose();try{disposed.Read(bytes,0,1);return 29;}catch(ObjectDisposedException){}
        if(Path.GetDirectoryName("sd:/file")!="sd:/" || Path.GetFullPath("sd:/test/../test")!=root)return 30;
        try{Path.GetFullPath("sd:/../escape");return 31;}catch(ArgumentException){}
        try{Directory.Delete("sd:/",true);return 32;}catch(UnauthorizedAccessException){}
        var active=Directory.EnumerateFiles(root).GetEnumerator();try{Storage.Unmount(StorageDevice.SdSerialPort2);return 33;}catch(IOException){}finally{active.Dispose();}
        File.WriteAllBytes(Path.Combine(root,"empty.bin"),new byte[0]);if(File.ReadAllBytes(Path.Combine(root,"empty.bin")).Length!=0)return 34;File.Delete(Path.Combine(root,"empty.bin"));
        File.Copy(path,Path.Combine(root,"copy.bin"));File.Move(Path.Combine(root,"copy.bin"),Path.Combine(root,"moved.bin"));
        Directory.CreateDirectory(Path.Combine(root,"nested"));
        if(Directory.GetFiles(root).Length!=2 || Directory.GetDirectories(root).Length!=1)return 13;
        foreach(var entry in Directory.EnumerateFiles(root)){if(entry.Length==0)return 14;break;}
        try {foreach(var entry in Directory.EnumerateFiles(root)){throw new InvalidOperationException(entry);}}catch(InvalidOperationException){}
        if(Storage.ReadAllBytes(StorageDevice.DvdDrive,"asset.bin")[0]!=42)return 15;
        try {Storage.WriteAllBytes(StorageDevice.DvdDrive,"asset.bin",bytes);return 16;}catch(UnauthorizedAccessException){}
        using(var card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("DDNT","01")))
        {
            var options=new MemoryCardSaveOptions();options.Title="DolphinDotNet";options.Comment="Slot one";options.Icon=new byte[2048];
            card.WriteSave("Save01",bytes,options);var save=card.ReadAllBytes("Save01");if(save.Length!=3 || save[2]!=7)return 17;
            var entries=card.GetEntries();if(entries.Length!=1 || entries[0].Name!="Save01" || entries[0].Length!=3)return 18;
            using(var shared=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("DDNT","01"))){if(shared.ReadAllBytes("Save01")[0]!=9)return 35;}
            try{card.WriteAllBytes("bad/name",bytes);return 36;}catch(ArgumentException){}
            try{using(var conflicting=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("OTHR","01"))){}return 37;}catch(IOException){}
            options.Icon=new byte[1];try{card.WriteSave("Save01",bytes,options);return 38;}catch(ArgumentException){}if(card.ReadAllBytes("Save01").Length!=3)return 39;
            card.Delete("Save01");if(card.GetEntries().Length!=0)return 19;
        }
        Directory.SetCurrentDirectory(root);if(Directory.GetCurrentDirectory()!=root || Path.GetFullPath("data.bin")!=path)return 42;
        Directory.SetCurrentDirectory("sd:/");if(Path.GetDirectoryName("sd:/")!=null || Path.GetExtension("file.")!="")return 43;
        Directory.Delete(root,true);if(Directory.Exists(root))return 20;
        Storage.Unmount(StorageDevice.DvdDrive);Storage.Unmount(StorageDevice.SdSerialPort2);return 0;
    }
}
