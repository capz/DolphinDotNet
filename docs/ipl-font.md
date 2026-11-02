# Native IPL font

`IplFont.Load()` reads the console's firmware font through libogc `SYS_InitFont`
and `SYS_GetFontTexel`. There is no bundled font, ROM dump, or desktop font
dependency. When the firmware font is unavailable, loading fails explicitly.

```csharp
using var font = IplFont.Load(); // application executor, between frames
using var ui = new Draw2D(GraphicsDevice.Default);
// Inside a device frame:
ui.Begin();
ui.DrawText(font, "Welcome home!", new Vector2(32, 40), Color32.Black, .75f);
ui.End();
```

The first version exposes printable ASCII (U+0020..U+007E), proportional widths,
line height, ascent and descent. Other code points render `?`; CR/LF and tabs
are handled by measurement and drawing. `FirmwareEncoding` reports ANSI or
Shift-JIS; it does not promise Japanese/Unicode text layout.

The owned 512×256 IA8 atlas costs 256 KiB. Firmware decode storage is shared and
remains alive until graphics shutdown because libogc retains pointers into it:
131,360 bytes for ANSI or 1,183,488 for Shift-JIS. `SharedFirmwareBytes` reports
this additional residency; `NativeBytes` reports the owned atlas only. Dispose
each font to retire its atlas. `dnd_draw_shutdown` releases the shared decode.
Loading during an active frame or off the application executor is rejected.

`tests/ipl_font_test.c` uses a synthetic provider, checks every extracted pixel
across tile boundaries, ANSI/Shift-JIS metadata, cache reuse/shutdown and failure
paths. `tests/IplFontSmoke` exercises managed measurement and batched rendering
on the AOT/native path. Emulator font availability depends on its configured
system font support; physical GameCube validation remains a separate check.
