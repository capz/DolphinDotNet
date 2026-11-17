# Full-resolution GX hardware antialiasing

`DisplayOptions.AntialiasingMode = AntialiasingMode.FullResolution` enables
native three-sample AA at 640×480, interlaced or progressive. It uses two
248-row EFB passes with a 16-row overlap. All 480 output rows are retained.
Legacy `Antialiasing = true` still selects the reduced-height libogc AA mode.
576-line and 320-line modes do not support FullResolution.

Prepare animation, resources, visibility and input once, then replay drawing:

```csharp
device.ConfigureDisplay(new DisplayOptions {
    Resolution = GraphicsResolution.R640x480,
    AntialiasingMode = AntialiasingMode.FullResolution
});
device.BeginFrame(clear);
do {
    // Same prepared world, followed by HUD. End every 3D/2D pass.
    DrawPreparedScene();
} while (device.NextFramePart());
device.EndFrame();
```

`NextFramePart` resolves the current band and returns true for the second band.
It returns false after the final band. EndFrame then presents once. Calling
EndFrame before completing all AA parts is rejected. Existing single-pass
programs retain their behavior with None/ReducedResolution. Replay drawing
only: no simulation, input, resource eviction or display changes inside the loop.
SDK 3D/model/2D projections, viewports and scissors use full logical coordinates.
Raw GX clients must implement their own cropped projection and should not assume
that arbitrary raw GX commands are transformed by the SDK.

Full-screen SceneCapture works at 640×480 or half-size 320×240 in RGB565/RGBA8.
Each part updates its resident band in the existing capture texture, with block
aligned overlap. Capture then render refraction in each part. Small distortion
can sample the guard rows; larger cross-band reads may see the previous frame.
Other source regions are rejected in full AA mode.

## Cost

- Existing double 640×480 YUV XFBs: 1,228,800 bytes (1,200 KiB), same as non-AA.
- No extra AA texture or full-frame command recording buffer.
- Same physical EFB, now RGB565/Z16 with three samples; no destination alpha.
- Two scene submissions, repeated geometry work and extra resolve/clear bandwidth.
- Existing capture storage is reused. Half-size RGB565 capture stays 153,600 bytes.
- Switching between non-AA 640×480 and full AA reuses the same XFB pair,
  avoiding a mode-switch allocation peak. Changes that need larger buffers
  allocate replacements transactionally before releasing the old pair.

This is hardware AA and does not require Dolphin host MSAA. Emulator tests verify
layout/rendering; performance still needs measurement on real GameCube hardware.
