using System;
using System.IO;
using Dolphin.Storage;
using Dolphin.Timing;

namespace Dolphin.IO;

/// <summary>Physical extent statistics for a FAT32 or exFAT file.</summary>
public sealed class DefragAnalysis
{
    public long Length { get; }
    public int ExtentCount { get; }
    public bool IsContiguous => ExtentCount <= 1;
    internal DefragAnalysis(long length,int extents)
    { Length=length; ExtentCount=extents; }
}
public enum DefragOutcome
{
    AlreadyContiguous=0,
    Optimized=1,
    NoContiguousSpace=2,
    Unsupported=3
}
public sealed class DefragResult
{
    public DefragOutcome Outcome { get; }
    public DefragAnalysis Before { get; }
    public DefragAnalysis After { get; }
    public long BytesCopied { get; }
    public long ElapsedMilliseconds { get; }
    internal DefragResult(DefragOutcome outcome,DefragAnalysis before,
        DefragAnalysis after,long bytes,long elapsed)
    { Outcome=outcome;Before=before;After=after;BytesCopied=bytes;ElapsedMilliseconds=elapsed; }
}
/// <summary>Opt-in FAT32/exFAT allocation policy and manual inspection.</summary>
public static class Defrag
{
    /// <summary>Automatic writes are not optimized until explicitly enabled.</summary>
    public static bool IsEnabled => NativeStorage.AutoDefragEnabled();
    public static void OptIn(bool enabled) => NativeStorage.SetAutoDefrag(enabled);
    /// <summary>Minimum length for automatic preallocation/repair, default 65536.</summary>
    public static int MinimumFileSize
    {
        get => NativeStorage.DefragMinFileSize();
        set {if(value<0)throw new ArgumentOutOfRangeException(nameof(value));NativeStorage.SetDefragMinFileSize(value);}
    }
    /// <summary>Process-global temporary policy; do not overlap scopes across threads.</summary>
    public static IDisposable BeginScope(bool enabled,int? minimumFileSize=null)
        =>new Scope(enabled,minimumFileSize);
    private sealed class Scope : IDisposable
    {
        private readonly bool oldEnabled;
        private readonly int oldMinimum;
        private bool disposed;
        internal Scope(bool enabled,int? minimum)
        {
            if(minimum.HasValue && minimum.Value<0)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            oldEnabled=IsEnabled;oldMinimum=MinimumFileSize;
            if(minimum.HasValue)MinimumFileSize=minimum.Value;
            OptIn(enabled);
        }
        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            MinimumFileSize=oldMinimum;
            OptIn(oldEnabled);
        }
    }
    /// <summary>Read the physical cluster-run count. Does not change the file.</summary>
    public static DefragAnalysis Analyze(string path)
    {
        if(path==null)throw new ArgumentNullException(nameof(path));
        int extents=NativeStorage.DefragAnalyze(path);
        using(var file=File.OpenRead(path))
            return new DefragAnalysis(file.Length,extents);
    }
    /// <summary>Explicitly repair a file even with automatic optimization off.</summary>
    public static DefragResult Optimize(string path)
    {
        if(path==null)throw new ArgumentNullException(nameof(path));
        var before=Analyze(path);
        var watch=Stopwatch.StartNew();
        var outcome=(DefragOutcome)NativeStorage.DefragOptimize(path);
        watch.Stop();
        var after=Analyze(path);
        return new DefragResult(outcome,before,after,
            outcome==DefragOutcome.Optimized?before.Length:0,watch.ElapsedMilliseconds);
    }
    /// <summary>
    /// Copy exactly knownLength bytes into a verified create-new contiguous
    /// allocation, using a bounded 16 KiB buffer; never loads the file into RAM.
    /// Throws rather than silently publishing a fragmented file.
    /// </summary>
    public static void WriteContiguousFrom(Stream source,string destination,long knownLength)
    {
        if(source==null)throw new ArgumentNullException(nameof(source));
        if(destination==null)throw new ArgumentNullException(nameof(destination));
        if(knownLength<0)throw new ArgumentOutOfRangeException(nameof(knownLength));
        using(var writer=Storage.CreateContiguousFile(destination,knownLength))
        {
            var buffer=new byte[16384];
            long remaining=knownLength;
            while(remaining>0)
            {
                int n=source.Read(buffer,0,(int)Math.Min((long)buffer.Length,remaining));
                if(n<=0)throw new EndOfStreamException("Source ended before knownLength.");
                writer.Write(buffer,0,n);
                remaining-=n;
            }
            writer.Complete();
        }
    }
}
