using System;
using System.IO;
using Dolphin.Collections;

namespace Dolphin.Storage;

/// <summary>Read-only GameCube FST exposed by a .iso or .gcm file on a mounted device.
/// Keeps the image's underlying file open; close all streams before disposing the image.</summary>
public sealed class GameCubeImage : IDisposable
{
    private int handle;
    private int openStreams;
    private GameCubeImage(int handle) { this.handle=handle; }

    /// <summary>Open and validate an original GameCube disc image, e.g. sd:/games/game.iso
    /// or gcloader:/games/game.iso. ISO9660 and compressed images are not supported.</summary>
    public static GameCubeImage Open(string path)
    {
        if(path==null)throw new ArgumentNullException("path");
        int token=NativeStorage.IsoOpen(path);
        if(token==0)throw new IOException("Cannot open or validate GameCube ISO FST.");
        return new GameCubeImage(token);
    }
    private void Check()
    {
        if(handle==0)throw new ObjectDisposedException("GameCubeImage");
    }
    private int Find(string path)
    {
        Check();
        if(path==null)throw new ArgumentNullException("path");
        return NativeStorage.IsoFind(handle,path);
    }
    public bool FileExists(string path)
    {
        int i=Find(path);
        return i>=0 && !NativeStorage.IsoDirectory(handle,i);
    }
    public bool DirectoryExists(string path)
    {
        int i=Find(path);
        return i>=0 && NativeStorage.IsoDirectory(handle,i);
    }
    public string[] GetFiles(string directory="/")=>GetEntries(directory,false);
    public string[] GetDirectories(string directory="/")=>GetEntries(directory,true);
    private string[] GetEntries(string path,bool folders)
    {
        int dir=Find(path);
        if(dir<0)throw new DirectoryNotFoundException("GameCube ISO directory not found.");
        if(!NativeStorage.IsoDirectory(handle,dir))throw new IOException("Not a directory.");
        int end=NativeStorage.IsoNext(handle,dir);
        var list=new List<string>();
        for(int i=dir+1;i<end;i=NativeStorage.IsoNext(handle,i))
            if(NativeStorage.IsoDirectory(handle,i)==folders)
                list.Add(NativeStorage.IsoName(handle,i));
        return list.ToArray(); // Immediate children: names relative to directory.
    }
    public Stream OpenRead(string path)
    {
        int entry=Find(path);
        if(entry<0)throw new FileNotFoundException("GameCube ISO file not found.",path);
        if(NativeStorage.IsoDirectory(handle,entry))throw new IOException("Cannot open directory as a file.");
        int size=NativeStorage.IsoLength(handle,entry);
        openStreams++;
        return new ImageStream(this,entry,size);
    }
    public byte[] ReadAllBytes(string path)
    {
        using var stream=OpenRead(path);
        if(stream.Length>int.MaxValue)throw new IOException("Image entry exceeds addressable array size.");
        var data=new byte[(int)stream.Length];
        int done=0;
        while(done<data.Length)
        {
            int n=stream.Read(data,done,data.Length-done);
            if(n<=0)throw new EndOfStreamException();
            done+=n;
        }
        return data;
    }
    public void Dispose()
    {
        if(handle==0)return;
        if(openStreams!=0)throw new InvalidOperationException("Dispose image streams first.");
        NativeStorage.IsoClose(handle);
        handle=0;
    }

    private sealed class ImageStream : Stream
    {
        private readonly GameCubeImage image;
        private readonly int entry,length;
        private int position;
        private bool disposed;
        internal ImageStream(GameCubeImage image,int entry,int size)
        {this.image=image;this.entry=entry;length=size;}
        private void Check()
        {
            if(disposed)throw new ObjectDisposedException("GameCubeImageStream");
            image.Check();
        }
        public override bool CanRead=>!disposed;
        public override bool CanWrite=>false;
        public override bool CanSeek=>!disposed;
        public override long Length{get{Check();return length;}}
        public override long Position
        {
            get{Check();return position;}
            set{Seek(value,SeekOrigin.Begin);}
        }
        public override int Read(byte[] buffer,int offset,int count)
        {
            Check();
            if(buffer==null)throw new ArgumentNullException("buffer");
            if(offset<0 || count<0 || offset>buffer.Length-count)
                throw new ArgumentOutOfRangeException("offset");
            int remaining=length-position;
            if(count>remaining)count=remaining;
            if(count==0)return 0;
            int n=NativeStorage.IsoRead(image.handle,entry,position,buffer,offset,count);
            if(n<0)throw new IOException("GameCube ISO read failed.");
            position+=n;
            return n;
        }
        public override long Seek(long offset,SeekOrigin origin)
        {
            Check();
            long start=origin==SeekOrigin.Begin ? 0
                        : origin==SeekOrigin.Current ? position
                        : origin==SeekOrigin.End ? length
                        : throw new ArgumentException("Invalid seek origin.","origin");
            if(offset< -start || offset>length-start)
                throw new IOException("Seek outside GameCube ISO file.");
            position=(int)(start+offset);
            return position;
        }
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException("GameCube images are read only.");
        public override void SetLength(long value)=>throw new NotSupportedException("GameCube images are read only.");
        public override void Flush(){Check();}
        public override void Dispose()
        {
            if(disposed)return;
            disposed=true;
            image.openStreams--;
        }
    }
}
