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
    /// <summary>Initialize from a correctly set local calendar without knowing
    /// its UTC offset. Certificate validity must cover every UTC instant within
    /// 14 hours of the supplied local calendar; normal chain and hostname
    /// verification remain mandatory. Near certificate validity boundaries this
    /// stricter check can reject a connection that exact trusted UTC accepts.</summary>
    /// <param name="trustedRootsPem">Explicit PEM trust anchors, maximum 256 KiB.</param>
    /// <param name="freshEntropy">The same fresh secret entropy required by Initialize;
    /// the array is cleared on success and failure.</param>
    /// <param name="localUnixTimeSeconds">The console's correctly set local calendar,
    /// encoded as Unix seconds without applying a timezone. Both ends of its
    /// possible UTC interval must lie within the supported 2024–2038 time ABI.</param>
    public static void InitializeFromLocalClock(byte[] trustedRootsPem,byte[] freshEntropy,long localUnixTimeSeconds)
    {
        if(freshEntropy==null)throw new ArgumentNullException("freshEntropy");
        try
        {
            if(trustedRootsPem==null)throw new ArgumentNullException("trustedRootsPem");
            if(NativeNetwork.TlsConfigureLocalClock(trustedRootsPem,freshEntropy,localUnixTimeSeconds)<0)
                throw new IOException("TLS local-clock configuration rejected: set the console calendar correctly within 2024–2038 and supply valid roots and fresh entropy with no active TLS connections.");
        }
        finally {Array.Clear(freshEntropy,0,freshEntropy.Length);}
    }
    /// <summary>Clear native trust and DRBG state. Active TLS connections must be disposed first.</summary>
    public static void Reset()
    {if(NativeNetwork.TlsReset()<0)throw new InvalidOperationException("TLS connections are still active.");}
}
