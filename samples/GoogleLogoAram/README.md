# Google logo download through ARAM

Streams an HTTP PNG into a 256 KiB `Dolphin.IO.AramStream` using one 4 KiB
buffer, then seeks and reads the completed PNG into a bounded MEM1 byte array
for `Texture2D.FromPng`. The image is displayed with its aspect ratio preserved.
ARAM reduces the download's MEM1 residency; PNG decoding still needs MEM1 and
native GX texture storage. This is not a zero-copy decoder.

The shared [download implementation](../Common/SampleDownload.cs) uses standard
`HttpClient`, response-header streaming, cancellation and awaited reads/writes.
The application keeps drawing while I/O runs. A retries; B cancels and exits;
Z toggles statistics. Errors remain visible and retryable. Responses, streams,
ARAM staging, replaced textures and final textures are disposed.

GameCube HTTP does not support TLS. The Google image is served over a local
HTTP relay which downloads it over verified HTTPS on the development PC:

1. Run `python3 samples/GoogleLogoAram/logo_proxy.py` on that PC.
2. Change the URL in `Program.cs` to the PC's LAN IPv4 address and port 18081.
3. Configure Dolphin's Serial Port 1 Broadband Adapter with a working LAN backend.
4. Open `GoogleLogoAram.sln` and run the Dolphin launch profile.

DHCP and DNS 8.8.8.8 are the defaults; developers can supply `NetworkOptions`
for static addresses and custom DNS. The PC-to-console relay is unencrypted;
use it on a trusted LAN. Desktop CLR execution does not provide native ARAM.
See [networking](../../docs/network-runtime.md) and
[async runtime](../../docs/async-runtime.md).

The production download flow has loopback HTTP tests for streaming, cancellation,
size limits, retry and resource ownership. Actual ARAM DMA and BBA behavior require
Dolphin or hardware; the host ownership tests substitute the graphics/ARAM backend.
