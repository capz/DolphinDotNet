# Google logo download to GameCube ARAM

This sample demonstrates downloading an image without keeping the entire download in GameCube **main RAM (MEM1)**.

The HTTP response is consumed as a stream (`httpResponseStream`). The standard `Stream.CopyToAsync(aramStream, CopyBufferBytes)` API receives the image in small segments through a single reusable **4 KiB buffer** and awaits each write to `aramStream`, an `AramStream` backed by **ARAM (Auxiliary RAM)** — the GameCube's additional memory that is separate from CPU-accessible MEM1. This avoids allocating a full image-sized byte array in MEM1, leaving more main RAM available for gameplay, rendering and other work. Network reception and ARAM DMA still use small MEM1 staging buffers; data does not bypass main RAM entirely.

When the image is needed, seek within `aramStream` and read the necessary bytes back into MEM1 with `ReadAsync`. The sample demonstrates this by retrieving the first eight bytes and checking the PNG signature. It does not decode or draw the PNG. **Keep the ARAM stream alive** if you need to retrieve the image later; this short demonstration disposes it when the method returns.

## Modern API surface

Application async methods use `System.Threading.Tasks.Task`, and HTTP uses `System.Net.Http.HttpClient`. `Dolphin.IO.AramStream` derives from `System.IO.Stream` and exposes standard Task/cancellation signatures, so the HTTP response can be copied directly with `CopyToAsync` instead of a hand-written read/write loop. The sample uses aliases for this platform-specific stream and the network runtime.

DolphinDotNet AOT maps the supported standard calls to its runtime-owned async service thread. Managed execution is serialized between the application and background executors; this is not a general thread pool. PNG validation uses the supported byte-array `ReadAsync` overload, repeating reads until all eight signature bytes arrive. Running this sample with the desktop CLR does not provide the native GameCube backend.

## Understanding `Main()`

`Main()` is an ordinary `async Task<int>` entry point. It awaits the download and PNG verification, returns 0 on success or 1 on failure, and stops networking in `finally`. The generated native bootstrap starts and joins the runtime service thread. Networking, ARAM DMA and continuations advance automatically; no application pump loop is needed.

Default awaits resume on the application thread. Background tasks and I/O service execute on the service thread under a shared managed execution gate. See [automatic async runtime](../../docs/async-runtime.md) for executor capture, safe points and supported APIs.

## Run the example

GameCube HTTP support currently **does not include TLS/HTTPS**. Because the official Google logo is hosted over HTTPS, the accompanying `logo_proxy.py` downloads the PNG securely on a development PC and serves it locally over HTTP to the GameCube. The PC-to-GameCube connection itself is unencrypted; use only on a trusted LAN.

1. On a development PC with internet access, run `python3 samples/GoogleLogoAram/logo_proxy.py`.
2. Change `LogoUrl` in `Program.cs` to the PC's LAN IPv4 address (port 18081).
3. Open [GoogleLogoAram.sln](GoogleLogoAram.sln) and use the Dolphin launch profile,
   or run `dotnet run --project samples/GoogleLogoAram`. See [IDE setup](../../docs/ide-workflow.md).
4. Run it in Dolphin or on the GameCube with networking enabled. Exit code 0 means the streamed image's PNG signature was validated. Exit code 1 indicates a failure.

The example allocates **256 KiB of ARAM capacity** as an upper bound, not as a claim that the image occupies that much data. The 4 KiB staging buffer is reused for every segment. CI checks compilation and AOT generation; live network/ARAM hardware operation still needs testing.
