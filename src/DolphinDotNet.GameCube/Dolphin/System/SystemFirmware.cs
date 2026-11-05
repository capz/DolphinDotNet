using System;
using System.IO;
using System.Threading;

namespace Dolphin.System;

internal static class NativeFirmware
{
    internal static int FirmwareRead(byte[] destination,int offset,int count)
        =>throw new PlatformNotSupportedException("Console firmware access requires DolphinDotNet AOT.");
}

/// <summary>Read-only access to GameCube IPL firmware through the console EXI
/// bus, with an optional explicit file fallback. No firmware copies are written.</summary>
public static class SystemFirmware
{
    /// <summary>Try console ROM first. If inaccessible, invalid or unsupported,
    /// use only the explicitly supplied fallback path. Cancellation never falls
    /// back. Source reports the selected path, not whether a console is emulated.
    /// Run on the background executor; dispose the image after catalog extraction.</summary>
    public static FirmwareImage Open(string? fallbackPath=null,CancellationToken cancellationToken=default)
    {
        try { return ReadConsole(cancellationToken); }
        catch(InvalidDataException) { if(fallbackPath==null)throw; }
        catch(IOException) { if(fallbackPath==null)throw; }
        catch(NotSupportedException) { if(fallbackPath==null)throw; }
        return FirmwareImage.Load(fallbackPath!,FirmwareEncoding.Auto,cancellationToken);
    }
    /// <summary>Read exactly 2 MiB with 4 KiB bounded EXI transactions and
    /// cancellation between transactions. A busy bus fails instead of retrying
    /// indefinitely. Console firmware may be raw or already descrambled.</summary>
    public static FirmwareImage ReadConsole(CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data=new byte[FirmwareImage.ImageBytes];
        var quantum=new byte[4096];
        try
        {
            for(int offset=0;offset<data.Length;offset+=quantum.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count=NativeFirmware.FirmwareRead(quantum,offset,quantum.Length);
                if(count!=quantum.Length)
                    throw new IOException(count==-2?"Console firmware EXI read timed out.":"Console firmware is inaccessible or the EXI bus is busy.");
                Array.Copy(quantum,0,data,offset,count);
            }
            return FirmwareImage.Take(data,FirmwareEncoding.Auto,cancellationToken,FirmwareSource.ConsoleRom);
        }
        catch { Array.Clear(data,0,data.Length);throw; }
        finally { Array.Clear(quantum,0,quantum.Length); }
    }
}
