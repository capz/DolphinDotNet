# HTTPS in the SDK

The existing HttpClient accepts `https://` and defaults to port 443. HTTPS uses
mbedTLS 3.6.5 LTS pinned to `e185d7fd85499c8ce5ca2a54f5cf8fe7dbe3f8df`.
Networking GameCube/Wii bundles build and link the provider automatically; plain
HTTP remains available. The build machine needs Python 3, its configured C/AR
toolchain and HTTPS access to GitHub for the initial dependency download. The
library source/license is retained under `build/tls/source`. The SDK includes
`build_tls.py`; its `--source` argument supports a separately audited offline
checkout. mbedTLS is Apache-2.0 OR GPL-2.0-or-later licensed; retain its license
when redistributing its source or binary in accordance with the selected license.

## Required initialization

Before requesting HTTPS, call:

```csharp
Dolphin.Network.TlsConfiguration.Initialize(
    trustedRootsPem, freshEntropy, trustedUnixTimeSeconds);
using var client = new System.Net.Http.HttpClient();
string content = await client.GetStringAsync("https://example.com/config.json");
```

- `trustedRootsPem` is an explicit PEM CA trust bundle, at most 256 KiB. There is
  no implicit console trust store. Both hostname and certificate-chain/date
  validation are mandatory. No trust-all callback or certificate bypass exists.
- `freshEntropy` contains 48–64 cryptographically random **secret** bytes, fresh
  for each initialization and process boot. The SDK consumes/copies this input
  and clears the caller's array even when initialization fails. Provision it
  through a trusted host/device mechanism. Never use System.Random, ticks,
  public build constants, an unchanged distributed seed file, or unauthenticated
  network input. A securely seeded DRBG generates subsequent session randomness.
  After 4096 generation requests its reseed fails closed; close active HTTPS
  connections and initialize again with fresh entropy. It never invents entropy.
- `trustedUnixTimeSeconds` supplies credible UTC, currently bounded to 2024–2038
  for the PowerPC time ABI. The SDK advances this using its monotonic clock.
  Unauthenticated HTTP Date or unauthenticated NTP does not establish trusted UTC.

These are platform configuration inputs, not Hypercube-specific services. The
console currently has no built-in verified entropy provisioner. An application
without these inputs receives a clear HTTPS failure; it does not silently use
plaintext or disable validation. Source certificates can be public; entropy must
not be shipped in source, debug metadata or publicly distributed executables.

Configuration is process-wide, serialized by the managed runtime execution gate.
It cannot be changed/reset with active TLS sessions. Dispose responses/streams
and clients before calling `TlsConfiguration.Reset()` or reinitializing. At most
four TLS sessions exist globally, in addition to existing network/HTTP limits.
An invalid replacement configuration clears the old configuration when no
sessions are active. Busy replacements fail without disturbing active sessions.

## Protocol and lifetime

This increment supports TLS 1.2, SNI, HTTP/1.1 ALPN and certificate-authenticated
ECDHE with AES-128-GCM or ChaCha20-Poly1305. TLS 1.3, client certificates, session
resumption, renegotiation, arbitrary SslStream APIs, HTTP/2 and HTTP/3 are outside
this increment. TLS cryptography can consume significant CPU on GameCube; no
hardware latency/throughput claim is made.

The handshake progresses one library state-machine step per network operation.
Reads/writes use the existing nonblocking socket operations and cooperative
scheduler, retaining request timeouts and cancellation. The TLS provider owns
native contexts/buffers, never retained managed pointers. The existing response
parser handles verified decrypted bytes for both buffered and streamed bodies.
EOF without TLS close-notify is a transport error, not a successful TLS EOF;
fixed-length/chunked HTTP responses can complete once their authenticated framing
has been fully received. Dispose closes the connection without waiting for a
peer close-notify, so cancellation never blocks on TLS shutdown.

Relative and scheme-relative redirects retain HTTPS. Cross-origin redirects
(including scheme or effective-port changes) drop authorization/cookie headers.
HTTPS-to-HTTP redirects fail. No application request bytes are sent until the
TLS handshake and server verification succeed. Existing Connection: close,
response buffering and body/header limits remain in force.

## Validation

`python scripts/ci_https.py` builds the pinned provider, runs native lifecycle
and certificate checks, then builds Debug and Release C# requests through the
actual AOT compiler and executes them against local DNS/TLS servers, normally
and with GC stress. It covers fragmented encrypted replies, trusted/untrusted
roots, wrong hostname, expired/not-yet-valid certificates, time/configuration
bounds, session capacity, entropy exhaustion, buffered/streamed/chunked bodies,
relative redirects, credential isolation, downgrade rejection, truncation,
cancellation and absent configuration. The network CI area runs this gate.

The TLS-enabled GameCube ELF/DOL was compiled and linked with the SDK's pinned
libogc2/devkitPPC image, including the provider configuration entry point under
LTO, and passed DOL section validation.

Physical GameCube/BBA interoperability, provisioning and full-scene performance
remain separate hardware acceptance checks. Host TLS success does not prove the
quality of an application's entropy/time inputs or real-console compatibility.
