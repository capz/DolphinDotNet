using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dolphin.Security;

/// <summary>Collects fresh seed material on the native service executor, with
/// startup and continuous source health checks. No persistent or embedded seed
/// is used. GameCube source qualification on physical hardware remains required;
/// passing health checks or running in Dolphin does not prove an entropy rate.</summary>
public static class PlatformEntropy
{
    /// <summary>Collect 48–64 fresh secret bytes for TLS initialization.
    /// The bounded native operation releases the managed execution gate so the
    /// application can render. Cancellation is observed before and after native
    /// collection, which has a maximum 30-second deadline. Failure or cancellation
    /// clears the temporary output. Callers must consume and clear successful
    /// output; TlsConfiguration.Initialize clears its entropy argument.</summary>
    public static Task<byte[]> GetBytesAsync(int count=64,CancellationToken cancellationToken=default)
    {
        if(count<48||count>64)throw new ArgumentOutOfRangeException("count");
        return Task.Run(()=>Collect(count,cancellationToken),cancellationToken);
    }
    private static byte[] Collect(int count,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] output=new byte[count];
        try {
            int result=NativeEntropy.Fill(output,30000);
            token.ThrowIfCancellationRequested();
            if(result==-5)throw new TimeoutException("The entropy source did not become healthy within 30 seconds.");
            if(result==-3)throw new OutOfMemoryException("The entropy provider exceeded its bounded allocation budget.");
            if(result==-2)throw new InvalidOperationException("The entropy collector is already in use.");
            if(result!=0)throw new IOException("The entropy source failed its startup or continuous health checks. TLS was not initialized.");
            return output;
        } catch {
            Array.Clear(output,0,output.Length);throw;
        }
    }
}

internal static class NativeEntropy
{
    internal static int Fill(byte[] output,int timeoutMilliseconds)
        =>throw new PlatformNotSupportedException("Requires DolphinDotNet GameCube AOT.");
}
