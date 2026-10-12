# Google logo download to GameCube ARAM

This sample demonstrates downloading an image without keeping the entire download in GameCube **main RAM (MEM1)**.

The HTTP response is consumed as a stream (`httpResponseStream`). A single reusable **4 KiB buffer** (`segmentBuffer`) receives the image in small segments. Each segment is written to `aramStream`, an `AramStream` backed by **ARAM (Auxiliary RAM)** — the GameCube's additional memory that is separate from CPU-accessible MEM1. This avoids allocating a full image-sized byte array in MEM1, leaving more main RAM available for gameplay, rendering and other work. Network reception and ARAM DMA still use small MEM1 staging buffers; data does not bypass main RAM entirely.

When the image is needed, seek within `aramStream` and read the necessary bytes back into MEM1 with `ReadAsync`. The sample demonstrates this by retrieving the first eight bytes and checking the PNG signature. It does not decode or draw the PNG. **Keep the ARAM stream alive** if you need to retrieve the image later; this short demonstration disposes it when the method returns.

## Understanding `Main()`

`Main()` starts `DownloadLogoToAramAsync()` and receives a `Task<int>` representing its eventual completion. DolphinDotNet is **cooperatively scheduled** on GameCube; async operations do not automatically progress on background threads.

While the task is pending, the sample explicitly runs both pumps:

- `NetworkRuntime.Pump(16, 1)` progresses network initialization, DNS, sockets and HTTP requests. The arguments cap the operations and approximate milliseconds of work in a pass.
- `Scheduler.Pump(16)` executes up to 16 queued task continuations and advances ARAM DMA transfer completion.

Without these calls, the HTTP download or ARAM writes may remain pending. This headless example pumps in a loop until completion. **A real game should place both pumps in its existing per-frame loop** alongside input, updates and rendering; the call limits are not a guarantee of frame timing.

After completion, `GetAwaiter().GetResult()` returns 0 on success or throws an exception; `Main()` returns 1 on failure. The `finally` block shuts down networking.

## Run the example

GameCube HTTP support currently **does not include TLS/HTTPS**. Because the official Google logo is hosted over HTTPS, the accompanying `logo_proxy.py` downloads the PNG securely on a development PC and serves it locally over HTTP to the GameCube. The PC-to-GameCube connection itself is unencrypted; use only on a trusted LAN.

1. On a development PC with internet access, run `python3 samples/GoogleLogoAram/logo_proxy.py`.
2. Change `LogoUrl` in `Program.cs` to the PC's LAN IPv4 address (port 18081).
3. Build with `dotnet build samples/GoogleLogoAram/GoogleLogoAram.csproj -c Release`, then compile the assembly through DolphinDotNet AOT and your GameCube DOL build process.
4. Run it on the GameCube with networking enabled. Exit code 0 means the streamed image's PNG signature was validated. Exit code 1 indicates a failure.

The example allocates **256 KiB of ARAM capacity** as an upper bound, not as a claim that the image occupies that much data. The 4 KiB staging buffer is reused for every segment. CI checks compilation and AOT generation; live network/ARAM hardware operation still needs testing.
