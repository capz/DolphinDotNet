using System;
using System.IO;
namespace Dolphin.Network;
/// <summary>Process-wide HTTPS trust, entropy and certificate-validation clock.
/// Initialize before requests, with no active TLS connections. Configuration is
/// copied natively. The entropy array is cleared, including on failure.</summary>
public static class TlsConfiguration
{
    /// <param name="trustedRootsPem">Explicit PEM trust anchors, maximum 256 KiB.</param>
    /// <param name="freshEntropy">48–64 cryptographically random secret bytes, fresh
    /// for every initialization and process boot. Never use Random, ticks, public
    /// constants or an unchanged seed embedded in a distributable executable.</param>
    /// <param name="unixTimeSeconds">Trusted UTC time; the runtime advances it using
    /// its monotonic clock. The current PowerPC time ABI supports 2024–2038.</param>
    public static void Initialize(byte[] trustedRootsPem,byte[] freshEntropy,long unixTimeSeconds)
    {
        if(freshEntropy==null)throw new ArgumentNullException("freshEntropy");
        try
        {
            if(trustedRootsPem==null)throw new ArgumentNullException("trustedRootsPem");
            if(NativeNetwork.TlsConfigure(trustedRootsPem,freshEntropy,unixTimeSeconds)<0)
                throw new IOException("TLS configuration rejected: require valid PEM roots, fresh 48–64 byte cryptographic entropy, trusted UTC time and no active TLS connections.");
        }
        finally {Array.Clear(freshEntropy,0,freshEntropy.Length);}
    }
    /// <summary>Clear native trust and DRBG state. Active TLS connections must be disposed first.</summary>
    public static void Reset()
    {if(NativeNetwork.TlsReset()<0)throw new InvalidOperationException("TLS connections are still active.");}
}
