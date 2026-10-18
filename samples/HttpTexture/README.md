# HTTP texture

Downloads <http://dlptest.com/dlp-test-logo.png> with standard `System.Net.Http.HttpClient`,
decodes the PNG entirely in memory with `Texture2D.FromPng`, and displays it with
`Draw2D`. No preconverted texture or SD card is required. The complete logo is
centered and scaled to fit without changing its aspect ratio; alpha is preserved.

Open `HttpTexture.sln` in Rider and choose **HttpTexture in Dolphin (play without
debugger)**. Configure Dolphin's GameCube Serial Port 1 as a Broadband Adapter
with an Internet-capable backend (for example HLE); a working BBA network is
required. The runtime uses DHCP and DNS 8.8.8.8 by default. Configure
`NetworkRuntime.StartAsync(new NetworkOptions { ... }, default)` for static
addresses or another DNS server. See [networking](../../docs/network-runtime.md).

```sh
dotnet build samples/HttpTexture/HttpTexture.sln -c Debug
```

Use **B** to exit and **Z** to toggle statistics. The usual wait-for-Rider launch
profile is included. A failed request or PNG decode prints the error on the result
screen; Start leaves that screen. The native `.dol` build requires devkitPro and
libogc2, as with the other samples. The CLI profile is a build profile; platform
APIs require the GameCube runtime.

The response buffer is bounded to 64 KiB (the logo is currently about 4 KiB).
`FromPng` accepts up to 1 MiB of encoded PNG, dimensions 1–1024 on each axis,
and at most 8 MiB of temporary decoder allocations. It creates tiled GX_RGBA8
storage and must run between graphics frames. PNG decode is synchronous; decode
assets during loading, rather than every frame. Texture disposal frees native
storage. No decoded managed pixel copy is retained.

HTTP is supported directly. If the remote site later redirects to HTTPS, the
runtime reports an unsupported-TLS error; it never disables TLS verification.
Change `ImageUrl` to a reachable HTTP endpoint or a local relay in that case.
