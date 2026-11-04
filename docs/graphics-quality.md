# Antialiasing and texture sampling

Configure the display and global sampler between frames:

```csharp
var device = GraphicsDevice.Default;
device.ConfigureDisplay(new DisplayOptions {
    Resolution = GraphicsResolution.R640x480,
    Antialiasing = true
});
var smooth = new SamplerState(
    TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear,
    lodBias: -0.5f, anisotropy: TextureAnisotropy.Four,
    biasClamp: true);
device.ConfigureSampling(smooth);
```

`Display.Antialiasing` reports the resolved setting. GX multisample AA is a
framebuffer setting, so it applies to every object and drawing pass. It uses
RGB565 color and a 16-bit depth buffer. At 640x480, libogc's standard AA modes
render a 640x242 EFB and resolve to 640x480 output. `Display.RenderSize` reports
the EFB size. 320x240 keeps its render size and uses the standard AA sample
pattern. 576-line output with AA is rejected without changing the display.
Progressive 480-line output still requires a component cable. Reconfiguring
the display with `Antialiasing = false` restores the normal render mode.

Sampling can vary for each material/object or group of 2D draws:

```csharp
material.Sampler = smooth;
material.SecondarySampler = new SamplerState(
    TextureMinFilter.Nearest, TextureMagFilter.Nearest);
renderer.DrawMesh(mesh, material, transform);
// For an imported object's complete material override:
renderer.DrawWithMaterial(gameObject, material);

// Inside a Draw2D pass; flushes preceding draws before changing state:
draw.SetSampler(smooth);
// ...draw sprites...
draw.SetSampler(default); // return to the global fallback
```

An explicit material sampler replaces the global sampler for that texture slot.
A default sampler inherits the device fallback, including when a material only
sets a UV transform. Ordinary imported-model draws use the global fallback while
retaining authored UVs. With `device.ConfigureSampling(default)`, textures return
to their authored sampling (or the nearest/linear choice at creation).
Changing global sampling during a frame throws.

The advanced `SamplerState` constructor exposes all six GX minification filters,
independent nearest/linear magnification, minimum/maximum LOD, LOD bias,
bias clamping, edge LOD, and 1x/2x/4x anisotropy. LOD is clamped to the resident
chain; on a single-level texture, a mip filter uses its corresponding base
nearest/linear filter. Choosing `Nearest` or `Linear` as the minification filter
disables mip selection even when a chain exists.

Filtering does not generate mip images. Load resident mip chains with
`Texture2D.FromBti`, `Texture2D.FromPackage` (DNTX v2), or imported model assets
that contain mip levels. `Texture2D.MipLevels` reports the resident count.
`Texture2D.ConfigureSampler` remains available for texture-specific wrap,
minification, magnification and bias settings; explicit material/global sampling
takes precedence during managed draws. Wrap-only and UV-only material settings
preserve a texture's LOD configuration when no global sampler is configured.

The existing `Dolphin.Graphics.Raw.GX` APIs also expose `GX_InitTexObjLOD`,
`GX_SetPixelFmt`, and `GX_SetCopyFilter` for applications owning a raw GX pass.
Managed passes restore their configured display and sampling state after raw work.
