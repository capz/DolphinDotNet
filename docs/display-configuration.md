# Display configuration

The SDK assumes a 4:3 television. Set `Widescreen` explicitly for a television
configured to stretch the GameCube signal to 16:9; the console cannot detect the
television's aspect setting.

```csharp
using Dolphin.Graphics;

var device = GraphicsDevice.Default;
device.ConfigureDisplay(new DisplayOptions {
    Resolution = GraphicsResolution.R640x480,
    Scan = ScanMode.Interlaced,
    Widescreen = true
});
```

Configure the display during setup or between frames, on the application thread.
Configuration during a frame, drawing pass, raw stream, or externally owned GX
session throws `InvalidOperationException`. Unsupported combinations also throw
without changing the current display. Replacement framebuffer allocation failure
throws `OutOfMemoryException` and preserves the previous configuration.

## Standard modes

| Resolution | Interlaced | Progressive |
| --- | --- | --- |
| `R320x240` | Unsupported | 240p double-strike; analog cables supported |
| `R640x480` | 480i | 480p; component/DTV signal required |
| `R640x576` | 576i | 576p; component/DTV signal required |

`Resolution` and `Scan` default to `Auto`. Both `Auto` values preserve the current
boot/native video mode. With an explicit resolution, `ScanMode.Auto` preserves
the current scan mode when the resolution matches; otherwise it selects
progressive when the component/DTV signal is present and interlaced when absent.
320x240 always uses progressive double-strike. `Resolution.Auto` with an explicit
scan mode uses the current standard resolution, or rejects an unrecognized mode.
480-line modes preserve NTSC/MPAL versus PAL60 timing where possible; 576-line
modes use PAL 50 Hz. These are libogc2 presets, not Swiss extended resolutions.

The component/DTV signal indicates the cable capability, not the television's
supported modes. Select a mode that the connected display supports.

`device.Display` reports the resolved `Resolution`, `Scan`, `Widescreen`,
`AspectRatio`, `OutputSize`, `RenderSize`, and nominal `RefreshRate` in Hz.
Native display information requires SDK ownership of GX.
`device.Width` and `Height` are render-buffer dimensions. PAL 576 modes render
into a 640x480 EFB and scale the copy to a 640x576 output buffer.

## Widescreen changes rendering only

```csharp
// Keep the current output resolution, scan mode, timing and framebuffers.
device.ConfigureDisplay(new DisplayOptions { Widescreen = true });
```

`Widescreen` defaults to `false`. Setting it changes the rendering aspect from
4:3 to 16:9 without changing the video output. The image is anamorphic: the
television's 16:9 stretch restores its intended proportions. Every configuration
uses the supplied options, so include `Widescreen = true` when changing modes
if widescreen should remain enabled.

### Cameras

`new Camera()` follows the display aspect automatically. Camera rendering,
projection, unprojection, picking and culling use the same aspect. Perspective
cameras preserve vertical field of view. Automatic orthographic cameras preserve
height and adjust width.

```csharp
var camera = new Camera(); // AspectRatioMode.Display
camera.FieldOfViewDegrees = 60;

// Supplying a positive aspect keeps existing explicit-aspect behavior.
var fixedCamera = new Camera(60, 4f / 3f);
fixedCamera.AspectRatioMode = AspectRatioMode.Display; // opt into automatic aspect
```

The constructor's omitted/zero aspect selects automatic behavior.
`SetPerspective` and `SetOrthographic` select `AspectRatioMode.Explicit`;
set it back to `Display` afterward to follow the display. Renderer overloads
accepting projection matrices and raw GX projections remain caller-controlled.

### 2D coordinates

Default `Draw2D` uses `CanvasScaleMode.Display`: a logical coordinate space with
square visual units after television stretching. Shapes, textures, text, retained
geometry and clipping share this projection. No per-object aspect correction is
needed. At a 640x480 render size, the full canvas is 640x480 for 4:3 and
approximately 853.333x480 for 16:9. Query the floating-point `LogicalSize` instead
of assuming it equals framebuffer pixels.

```csharp
using var draw = new Draw2D(device);
device.BeginFrame(Color32.Black);
draw.Begin();
var size = draw.LogicalSize;
draw.FillRectangle(new RectF(size.X - 28, 8, 20, 20), Color32.White);
draw.End();
device.EndFrame();
```

`LogicalSize` reflects the active viewport during a pass and the full framebuffer
outside a pass. `Camera2D` transforms operate in this logical space.
`Camera.Project`/`Unproject` use physical viewport pixels; convert input pixel
coordinates to the canvas coordinate space when interacting with 2D content.

For a fixed design size, choose `Fit` to preserve proportions with a centered
viewport, or `Stretch` to fill the viewport regardless of aspect:

```csharp
using var ui = new Draw2D(device, new Draw2DOptions {
    Width = 320, Height = 240, ScaleMode = CanvasScaleMode.Fit
});
```

Fit leaves the unused area at the frame clear color. Existing options supplying
`Width` and `Height` without a scale mode retain their previous stretch behavior.
Explicit `Display` scaling requires both dimensions to be zero; `Fit` requires
both dimensions to be specified. Display compensation also accounts for the
vertical scaling of PAL 576 output.
