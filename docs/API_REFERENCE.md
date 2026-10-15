# DolphinDotNet — Public API Reference

> One-file reference to the application-facing APIs in `DolphinDotNet.Core` and `DolphinDotNet.GameCube`, checked against the public source on `main` (15 October 2026). Each example contains **at most five lines of C#**. Add the corresponding `using` directives and place statements inside a method; names such as `device`, `model`, `input`, `path` and `camera` denote objects created during setup when an example uses them.

**Target and status.** These are the *implemented subset* available through the project's AOT compiler, not the entire .NET API. Core-provided types can also be accessed through the supported `System.*` metadata substitutions (e.g. `System.IO.File` ↔ `Dolphin.IO.File`, `System.Threading.Tasks.Task` ↔ `Dolphin.Threading.Tasks.Task`). Most native APIs require a GameCube DOL; desktop `dotnet run` is not a GameCube emulator. The Wii and Wii U build targets do not imply matching native support.

**Common rules.** Poll controllers once per update; create/draw/end each frame on the application executor; dispose native resources; mount a volume or memory card before storage use; initialize ARAM before other ARAM users. `async/await` progresses automatically without application task pumping. Samples are intentionally short and illustrate calls, not complete programs.

**Contents:** [Runtime](#runtime-and-diagnostics) · [Input](#controller-input) · [Time](#time-and-scheduling) · [Graphics](#managed-graphics-and-models) · [Audio](#audio) · [Storage](#storage-and-file-io) · [ARAM](#auxiliary-memory-aram) · [Networking](#networking-and-http) · [Tasks](#tasks-and-cancellation) · [Collections](#collections-and-core-runtime) · [Raw graphics](#low-level-gx-gu-and-video) · [Limits](#limitations-and-build).

## Runtime and diagnostics

### DolphinDotNet.Runtime / Dolphin.Platform

Compile-time GameCube platform identity; useful for logging and feature selection.

**API:** `Runtime.Platform`, `Platform.Name`.

~~~csharp
string target = Dolphin.Platform.Name;
~~~

### Dolphin.Diagnostics.DebugOverlay

Text log overlay and opt-in FPS/frame-time/triangles/memory diagnostics, drawn with the GameCube frame.

**API:** `DebugOverlay.WriteLine(string)`, `DebugOverlay.PerformanceVisible`.

~~~csharp
DebugOverlay.PerformanceVisible = true;
DebugOverlay.WriteLine("Snake started");
~~~

### Platform.Reboot / ResetButtonPressed

Explicit native console reset and physical reset-button input. Complete saves
before rebooting. The bootloader/ODE determines what executes next; requesting
the IPL menu might be intercepted by the boot environment.

**API:** `Platform.Reboot(RebootMode.Restart/ResetConsole/SystemMenu)`,
`Platform.ResetButtonPressed`.

~~~csharp
if (Platform.ResetButtonPressed) SaveGame();
Platform.Reboot(RebootMode.ResetConsole);
~~~

### Dolphin.Hardware — non-destructive device inventory

Discover controllers, GBA links, native memory cards, SD Gecko, SD2SP2,
BBA, modem, DVD media, GC Loader and Game Boy Player as typed entries.
Statuses are Present/Absent/Busy/Unknown/Unsupported; unknown or busy
does **not** mean absent. Safe scanning uses existing state; Extended may
make additional read-only probes on idle interfaces. Game Boy Player
detection remains Unknown (high-speed-port arbitration with ARAM).

**API:** `Hardware.Refresh([HardwareProbeMode.Safe/Extended])`,
`Hardware.Current`, `HardwareSnapshot.Devices/ConnectedDevices`,
`GetDevice(kind,port)`, `GetPresence(kind,port)`,
`IsPresent(kind,port)`, `HardwareDeviceInfo.NativeId/IsMounted`.

~~~csharp
var snapshot = Dolphin.Hardware.Hardware.Refresh();
if (snapshot.IsPresent(HardwareKind.MemoryCard, HardwarePort.SlotA))
    LoadGame();
~~~

## Controller input

### Dolphin.Input.Controller

Poll up to four GameCube controllers (ports 0–3), configure dead zones, and start/stop motor rumble. Poll once per game update.

**API:** `PortCount`, `Update()`, `GetState(port)`, `Configure(port,options)`, `Rumble(port,seconds)`, `StopRumble(port)`, `ReadButtonsDown(port)`.

~~~csharp
Controller.Update();
ControllerState pad = Controller.GetState(0);
if (pad.A.WasPressed) Controller.Rumble(0, 0.15f);
~~~

### ControllerState / ButtonState / StickState / ControllerButton

Immutable per-update input snapshots: connection transitions, digital button held/pressed/released, normalized left/C sticks and triggers.

**API:** `IsConnected`, `WasConnected`, `WasDisconnected`, `SupportsRumble`, `HeldButtons`, `PressedButtons`, `ReleasedButtons`, `Stick`, `CStick`, `LeftTrigger`, `RightTrigger`, `A/B/X/Y/Z/L/R/Start`, `DPadUp/Down/Left/Right`, `this[ControllerButton]`, `IsHeld`, `WasPressed`, `WasReleased`.

~~~csharp
var pad = Controller.GetState(0);
if (pad.IsConnected && pad.Start.WasPressed) Pause();
float horizontal = pad.Stick.X;
~~~

### ControllerOptions

Per-port analog-stick dead zones, maxima, and analog-trigger calibration.

**API:** `StickDeadZone`, `CStickDeadZone`, `StickMaximum`, `CStickMaximum`, `TriggerMinimum`, `TriggerMaximum`.

~~~csharp
Controller.Configure(0, new ControllerOptions { StickDeadZone = 0.2f });
~~~

### PlayerBindings / PlayerControls / PlayerInputState

Small typed action map for Jump, Interact, Run, Pause, Move and Look, supporting remapping without string-based commands.

**API:** `PlayerBindings.Jump/Interact/Run/Pause/Move/Look`, `PlayerControls(port,bindings)`, `GetState()`, `GetState(ControllerState)`, `PlayerInputState.Controller/Move/Look/Jump/Interact/Run/Pause`.

~~~csharp
var controls = new PlayerControls(0, new PlayerBindings { Jump = ControllerButton.A });
Controller.Update();
if (controls.GetState().Jump.WasPressed) Jump();
~~~

### InputRouter / InputContext / InputFrame

Priority-ordered context stack for modal menus, gameplay and input consumption; `InputContext` subclasses implement `HandleInput`.

**API:** `InputRouter(port,capacity)`, `Push(context)`, `Pop()`, `Remove(context)`, `Clear()`, `Current`, `Count`, `Update()`, `SuppressUntilRelease(buttons)`, `InputContext.IsModal`, `HandleInput(frame)`, `OnActivated/OnDeactivated/OnDisconnected`, `InputFrame.State`, `Consume(buttons)`, `ConsumeStick(stick)`, `ConsumeLeftTrigger/ConsumeRightTrigger/ConsumeAll`.

~~~csharp
var router = new InputRouter(0);
router.Push(menuContext); // an InputContext subclass
Controller.Update();
router.Update();
~~~

### ButtonBinding / ButtonCaptureContext / ButtonCaptureStatus

Read or consume a remappable physical button; capture the next allowed button press through a modal routing context.

**API:** `ButtonBinding(button)`, `Button`, `GetState(frame/state)`, `Consume(frame)`, `ButtonCaptureContext(binding,cancelButton,allowedButtons)`, `Status`, `CapturedButton`, `Waiting/Completed/Cancelled`.

~~~csharp
var jump = new ButtonBinding(ControllerButton.A);
var router = new InputRouter(0);
router.Push(new ButtonCaptureContext(jump));
~~~

## Time and scheduling

### Dolphin.Timing.Clock

Monotonic microsecond timestamps for calculating elapsed intervals (not wall-clock dates).

**API:** `Frequency`, `Timestamp`, `ElapsedSeconds(start,end)`.

~~~csharp
long started = Clock.Timestamp;
// Work occurs here.
double elapsed = Clock.ElapsedSeconds(started, Clock.Timestamp);
~~~

### Dolphin.Timing.Stopwatch

Reusable elapsed-time measurement with accumulated start/stop spans.

**API:** `StartNew()`, `Start()`, `Stop()`, `Reset()`, `Restart()`, `ElapsedTicks`, `ElapsedMilliseconds`, `ElapsedSeconds`, `IsRunning`.

~~~csharp
var watch = Dolphin.Timing.Stopwatch.StartNew();
watch.Stop();
long ms = watch.ElapsedMilliseconds;
~~~

### Dolphin.Timing.FrameClock

Per-frame delta/total seconds with clamping and pause/resume; the first tick returns zero.

**API:** `Tick()`, `Pause()`, `Resume()`, `Reset()`, `DeltaSeconds`, `TotalSeconds`, `MaximumDeltaSeconds`, `IsPaused`.

~~~csharp
var frameClock = new FrameClock { MaximumDeltaSeconds = 0.1f };
float dt = frameClock.Tick();
UpdateWorld(dt);
~~~

### Dolphin.Timing.Time / Task.Delay

Asynchronous one-shot delays, optionally cancellable; callbacks resume on their appropriate executor.

**API:** `Time.DelayAsync(milliseconds[,token])`, `Task.Delay(milliseconds[,token])`.

~~~csharp
await Dolphin.Timing.Time.DelayAsync(250);
await System.Threading.Tasks.Task.Delay(250);
~~~

### Dolphin.Timing.Timer

Disposable delayed or periodic callback scheduler. `Completion` reports completion or callback faults.

**API:** `Timer.Schedule(callback,dueMilliseconds[,periodMilliseconds,token])`, `Completion`, `Dispose()`.

~~~csharp
using var timer = Dolphin.Timing.Timer.Schedule(() => Refresh(), 100, 500);
~~~

## Managed graphics and models

### GraphicsDevice

Owns the GameCube frame and the exclusive 2D/3D/raw drawing passes; manages viewport, scissor, synchronization and EFB copies.

**API:** `Default`, `Width`, `Height`, `IsFrameActive`, `NativeBytes`, `BeginFrame(clear)`, `EndFrame()`, `BeginRaw()`, `EndRaw()`, `SetViewport(...)`, `SetScissor(...)`, `WaitIdle()`, `CopyToTexture(texture,x,y)`.

~~~csharp
var device = GraphicsDevice.Default;
device.BeginFrame(Color32.Black);
// Render one or more non-overlapping passes.
device.EndFrame();
~~~

### Draw2D / Draw2DOptions

Ordered native-backed 2D batch for shapes, textures, sprites, bitmap text, clipping, blend modes, and nine-slice panels.

**API:** `Draw2D(device,options)`, `Draw2DOptions.Width/Height/VertexCapacity`, `Begin()`, `End()`, `Flush()`, `SetClip(...)`, `ResetClip()`, `SetBlend(...)`, `FillRectangle(...)`, `DrawRectangle(...)`, `DrawLine(...)`, `FillTriangle(...)`, `DrawTexture(...)`, `DrawText(...)`, `DrawNineSlice(...)`, `DrawCalls`, `Flushes`, `Dispose()`.

~~~csharp
using var batch = new Draw2D(device, new Draw2DOptions { Width = 640, Height = 480 });
device.BeginFrame(Color32.Black);
batch.Begin(); batch.FillRectangle(new RectF(20, 20, 80, 30), Color32.White);
batch.End(); device.EndFrame();
~~~

### Color32 / Vector2 / RectF / Border

Value objects for colors (RGBA), 2D coordinates, rectangles and nine-slice borders.

**API:** `Color32(r,g,b[,a])`, `Color32.White/Black/Transparent`, `Packed`, `Vector2(x,y)`, `Vector2.Zero`, `RectF(x,y,w,h)`, `Border.Left/Top/Right/Bottom`.

~~~csharp
var color = new Color32(50, 180, 230);
var area = new RectF(10, 10, 120, 48);
batch.FillRectangle(area, color);
~~~

### Texture2D

Native GPU texture ownership from RGBA pixels or prebuilt `.dndtex` packages; supports async file loading.

**API:** `Create(width,height,rgba[,linear])`, `FromPackage(bytes[,linear])`, `LoadAsync(path[,token,linear])`, `Width`, `Height`, `NativeBytes`, `Dispose()`.

~~~csharp
using var texture = Texture2D.Create(2, 2, new byte[16]);
batch.DrawTexture(texture, new RectF(8, 8, 64, 64), Color32.White);
~~~

### BitmapFont

Bitmap glyph atlas for text drawing and measurement; can load `.dndfont` packages.

**API:** `BitmapFont(texture,cellWidth,cellHeight[,first,count])`, `LoadAsync(path[,token])`, `MeasureText(text[,scale])`, `Texture`, `CellWidth`, `CellHeight`, `FirstCharacter`, `CharacterCount`, `Dispose()`.

~~~csharp
using var font = await BitmapFont.LoadAsync("sd:/font.dndfont");
batch.DrawText(font, "READY", new Vector2(20, 20), Color32.White);
~~~

### Renderer

Convenience layer for a camera's 3D pass, meshes, primitives and model instances; can own a whole frame or attach to an existing one.

**API:** `Renderer([device])`, `Begin3D(camera)`, `End3D()`, `DrawMesh(mesh,material,transform)`, `DrawPrimitives(vertices,offset,count,primitive,transform)`, `BeginFrame(camera/matrices)`, `Draw(gameObject)`, `DrawModel(instance,transform/matrix)`, `EndFrame()`.

~~~csharp
var renderer = new Renderer(device);
device.BeginFrame(Color32.Black);
renderer.Begin3D(camera); renderer.DrawMesh(mesh, material, transform);
renderer.End3D(); device.EndFrame();
~~~

### Mesh / DynamicMesh

Own native indexed or unindexed vertex buffers; dynamic meshes support replacing selected vertex ranges between frames.

**API:** `Mesh.Create(VertexPositionColor[][,indices])`, `Mesh.Create(VertexPositionColorTexture[][,indices])`, `Mesh.Create(VertexPositionNormalColorTexture[][,indices])`, `NativeBytes`, `DynamicMesh.Create(...)`, `DynamicMesh.SetVertices(vertices[,offset,count])`, `Dispose()`.

~~~csharp
using var mesh = Mesh.Create(vertices); // VertexPositionColor[]
renderer.DrawMesh(mesh, material, transform);
~~~

### Material / render enums

Simple vertex/texture material: tint, blend, lighting, face culling, depth read/write and alpha cutoff.

**API:** `Texture`, `Color`, `Blend`, `Lighting`, `Cull`, `DepthTest`, `DepthWrite`, `AlphaCutoff`, `BlendMode`, `LightingMode`, `CullMode`, `PrimitiveType`.

~~~csharp
var material = new Material { Color = Color32.White, Cull = CullMode.Back };
renderer.DrawMesh(mesh, material, transform);
~~~

### Camera

Right-handed, Y-up perspective projection and explicit look-at view.

**API:** `Camera(fov,aspect,near,far)`, `LookAt(eye,target,up)`.

~~~csharp
var camera = new Camera(60f, 4f / 3f);
camera.LookAt(new Vector3(0, 2, 5), Vector3.Zero, new Vector3(0, 1, 0));
~~~

### Transform / Vector3 / Quaternion

Scene hierarchy and local/world position, rotation, scale, translation and world-matrix extraction.

**API:** `LocalPosition`, `LocalRotation`, `LocalScale`, `Position`, `Parent`, `Translate(offset[,worldSpace])`, `Rotate(x,y,z)`, `CopyWorldMatrix(float[])`, `Vector3.Zero/One`, `Quaternion.Identity`.

~~~csharp
var transform = new Transform();
transform.LocalPosition = new Vector3(0, 1, 0);
transform.Rotate(0, 45, 0);
~~~

### GameObject

Simple updateable scene object holding a transform and an optional model instance.

**API:** `GameObject([instance])`, `Transform`, `Model`, `Update(deltaSeconds)`, `Dispose()`.

~~~csharp
using var character = new GameObject(model.CreateInstance());
character.Transform.LocalScale = Vector3.One;
character.Update(1f / 60f);
~~~

### Model / AnimationCollection / AnimationClipInfo

Loads packaged `.dndmodel` assets and enumerates available animation clips; supports ARAM eviction and reloading.

**API:** `Model.LoadAsync(path[,token,nativeBudgetBytes])`, `Animations`, `NodeCount`, `IsResident`, `NativeBytes`, `AramBytes`, `TryGetAnimation(name,out clip)`, `GetNodeName(index)`, `CreateInstance([budget])`, `UnloadToAramAsync()`, `PrepareAsync()`, `PrepareAnimationAsync(name)`, `Dispose()`, `AnimationClipInfo.Name/DurationSeconds`.

~~~csharp
using var model = await Model.LoadAsync("sd:/player.dndmodel");
var instance = model.CreateInstance();
await model.PrepareAnimationAsync("Walk");
~~~

### ModelInstance / Animator

Per-object node state and animation playback with looping, pausing, seeking, cross-fades and speed control.

**API:** `ModelInstance.Model`, `Animator`, `Update(dt)`, `GetNodeTransform(index,matrix)`, `NativeBytes`, `Dispose()`, `Animator.Play(name[,loop])`, `CrossFade(name,duration[,loop])`, `Pause()`, `Resume()`, `Stop()`, `Seek(seconds)`, `SetSpeed(speed)`.

~~~csharp
using var instance = model.CreateInstance();
instance.Animator.Play("Walk", loop: true);
instance.Update(1f / 60f);
~~~

### ImmediateRenderer

Compatibility immediate-mode colored-triangle drawing for minimal examples; not nestable with another device frame.

**API:** `BeginFrame(r,g,b,a)`, `SetFrustum(...)`, `Translate(...)`, `Rotate(...)`, `BeginTriangles()`, `Color(r,g,b)`, `Vertex(x,y,z)`, `EndTriangles()`, `EndFrame()`.

~~~csharp
ImmediateRenderer.BeginFrame(0, 0, 0, 1);
ImmediateRenderer.BeginTriangles();
ImmediateRenderer.Color(1, 0, 0); ImmediateRenderer.Vertex(0, 0, 0);
ImmediateRenderer.EndTriangles(); ImmediateRenderer.EndFrame();
~~~

### Vertex structs

Typed layouts passed to mesh creation and primitive drawing.

**API:** `VertexPositionColor`, `VertexPositionColorTexture`, `VertexPositionNormalColorTexture`, `Position`, `Normal`, `Color`, `TextureCoordinate`.

~~~csharp
var vertex = new VertexPositionColor(new Vector3(0, 0, 0), Color32.White);
~~~

## Audio

### Dolphin.Audio.Audio

Initializes the GameCube libogc2 AESND playback system (also initialized lazily when loading or generating a sound).

**API:** `Initialize()`.

~~~csharp
Dolphin.Audio.Audio.Initialize();
~~~

### Sound

Owned native PCM clip, loaded from a mounted WAV file or synthesized unsigned mono PCM8. The native backend copies managed PCM bytes to aligned memory.

**API:** `Sound.Load(path)`, `Sound.LoadAsync(path[,token])`, `Sound.FromPcm8(byte[],sampleRate)`, `SampleRate`, `LengthBytes`, `Volume`, `Pitch`, `Play([loop])`, `Dispose()`.

~~~csharp
using var sound = Sound.FromPcm8(new byte[] { 128, 255, 128, 0 }, 22050);
sound.Volume = 0.6f;
using var voice = sound.Play();
~~~

### SoundChannel

Active or stopped AESND voice handle with independent volume and pitch. The sound must remain alive during playback.

**API:** `IsPlaying`, `Volume`, `Pitch`, `Stop()`, `Dispose()`.

~~~csharp
using var sound = Sound.Load("sd:/audio/coin.wav");
using var channel = sound.Play();
channel.Pitch = 1.2f;
channel.Stop();
~~~

### Music

Convenience long-lived sound owner for looping background WAV playback **preloaded into native memory**; it is not an MP3/Ogg decoder or streaming player.

**API:** `Load(path)`, `LoadAsync(path[,token])`, `Volume`, `Pitch`, `IsPlaying`, `Play([loop=true])`, `Stop()`, `Dispose()`.

~~~csharp
using var music = Music.Load("sd:/audio/loop.wav");
music.Volume = 0.4f;
music.Play(loop: true);
~~~

## Storage and file IO

GameCube `StorageDevice` values: `SdSlotA` (`carda:/` SD adapter), `SdSlotB` (`cardb:/`), `SdSerialPort2` (`sd:/` SD2SP2), `DvdDrive` (`dvd:/`, read only), and `MemoryCardSlotA/B` (native card API, **not** ordinary file paths). Most `Dolphin.IO` operations are also exposed through the supported `System.IO` names.

### Dolphin.Storage.Storage / DiscFormat

Mount/unmount SD or disc devices, switch ISO9660 versus GameCube original-disc FST, and use device-scoped file/directory helpers.

**API:** `Mount(device)`, `MountDisc(format)`, `IsMounted(device)`, `Unmount(device)`, `GetPath(device,path)`, `FileExists/DirectoryExists`, `ReadAllBytes/WriteAllBytes`, `Open(...)`, `CreateDirectory(...)`, `EnumerateFiles/EnumerateDirectories`, `GetFiles/GetDirectories`, `DeleteFile/DeleteDirectory`, `DiscFormat.Iso9660/GameCubeFst`.

~~~csharp
if (Storage.Mount(StorageDevice.SdSerialPort2)) {
    Storage.WriteAllBytes(StorageDevice.SdSerialPort2, "save.bin", new byte[] { 1 });
    Storage.Unmount(StorageDevice.SdSerialPort2);
}
~~~

### MemoryCard / MemoryCardIdentity

Native GameCube card slots with a four-character game code, two-character company code and separate named save entries. Card APIs work without SD.

**API:** `MemoryCard.Mount(slot,identity)`, `ReadAllBytes(name)`, `WriteAllBytes(name,data)`, `WriteSave(name,data,options)`, `GetEntries()`, `Delete(name)`, `Dispose()`, `MemoryCardIdentity(gameCode,companyCode)`.

~~~csharp
using var card = MemoryCard.Mount(StorageDevice.MemoryCardSlotA, new MemoryCardIdentity("DSNK", "01"));
card.WriteAllBytes("Save01", new byte[] { 1, 2, 3 });
byte[] saved = card.ReadAllBytes("Save01");
~~~

### MemoryCardSaveOptions / MemoryCardEntry

Metadata for a native card save. `WriteSave` accepts title/comment, optional 96×32 banner and 32×32 icon (both GX-tiled RGB5A3); entries report name and logical length.

**API:** `Title`, `Comment`, `Banner`, `Icon`, `MemoryCardEntry.Name`, `MemoryCardEntry.Length`.

~~~csharp
var options = new MemoryCardSaveOptions { Title = "Snake", Comment = "Level 1" };
card.WriteSave("SnakeRun", data, options);
foreach (var item in card.GetEntries()) Log(item.Name);
~~~

### Dolphin.IO.File / System.IO.File

Basic and asynchronous binary/text files, copy/move/delete and stream access on mounted volumes. Normal writes are not power-loss atomic; use native card transactions for resilient saves.

**API:** `Exists`, `ReadAllBytes/WriteAllBytes`, `ReadAllBytesAsync/WriteAllBytesAsync`, `ReadAllText/WriteAllText/AppendAllText`, `ReadAllLines/WriteAllLines/AppendAllLines`, `ReadLines`, `ReadAllTextAsync/WriteAllTextAsync/AppendAllTextAsync`, `ReadAllLinesAsync/WriteAllLinesAsync/AppendAllLinesAsync`, `Open/OpenRead/OpenWrite/Create`, `Copy`, `Move`, `Delete`.

~~~csharp
Dolphin.IO.File.WriteAllBytes("sd:/state.bin", new byte[] { 1, 2 });
byte[] data = Dolphin.IO.File.ReadAllBytes("sd:/state.bin");
await Dolphin.IO.File.WriteAllTextAsync("sd:/note.txt", "OK");
~~~

### Dolphin.IO.Directory / DirectoryInfo

Create/remove/move directories, set current directory and enumerate filenames or directories with optional `*`/`?` patterns and recursion.

**API:** `CreateDirectory`, `Exists`, `Delete([recursive])`, `Move`, `GetCurrentDirectory/SetCurrentDirectory`, `GetFiles/GetDirectories/GetFileSystemEntries`, `EnumerateFiles/EnumerateDirectories/EnumerateFileSystemEntries`, `DirectoryInfo.FullName/Name/Exists/Create/Delete`.

~~~csharp
Dolphin.IO.Directory.CreateDirectory("sd:/saves");
foreach (string file in Dolphin.IO.Directory.EnumerateFiles("sd:/saves", "*.bin"))
    Log(file);
~~~

### Dolphin.IO.Path / System.IO.Path

Path operations for `volume:/relative/path` names; supports normalized slashes but not Windows drive/UNC semantics.

**API:** `Combine(first,second)`, `GetFullPath(path)`, `IsPathRooted(path)`, `GetFileName(path)`, `GetDirectoryName(path)`, `GetExtension(path)`.

~~~csharp
string file = Dolphin.IO.Path.Combine("sd:/saves", "slot1.bin");
string name = Dolphin.IO.Path.GetFileName(file);
~~~

### Dolphin.IO.FileStream / System.IO.FileStream

Seekable native file descriptor with modes, access/share flags and deterministic close/flush; includes inherited stream async operations.

**API:** `FileStream(path,FileMode[,FileAccess,FileShare])`, `CanRead/CanWrite/CanSeek`, `Length`, `Position`, `Read/Write`, `Seek`, `SetLength`, `Flush`, `Close`, `Dispose`.

~~~csharp
using var file = Dolphin.IO.File.Open("sd:/level.dat", System.IO.FileMode.Create);
file.Write(new byte[] { 42 }, 0, 1);
file.Flush();
~~~

### Dolphin.IO.Stream / System.IO.Stream

Base byte-stream abstraction for file, memory, networking and ARAM; synchronous or cooperative async I/O plus copy/flush.

**API:** `Read/ReadAsync`, `Write/WriteAsync`, `ReadByte/WriteByte`, `Seek`, `SetLength`, `Flush/FlushAsync`, `CopyTo/CopyToAsync`, `CanRead/CanWrite/CanSeek`, `Length`, `Position`, `Close/Dispose`.

~~~csharp
using Dolphin.IO.Stream source = Dolphin.IO.File.OpenRead("sd:/from.dat");
using Dolphin.IO.Stream dest = Dolphin.IO.File.Create("sd:/to.dat");
await source.CopyToAsync(dest, 4096);
~~~

### Dolphin.IO.MemoryStream

In-memory byte stream with expandable or fixed backing array, seek, capacity, buffer access and `ToArray`.

**API:** `MemoryStream([capacity/byte[]/segment])`, `Capacity`, `Length`, `Position`, `Read/Write`, `ReadByte/WriteByte`, `Seek`, `SetLength`, `ToArray`, `GetBuffer`, `WriteTo`, `Dispose`.

~~~csharp
using var stream = new Dolphin.IO.MemoryStream();
stream.WriteByte(42);
byte[] bytes = stream.ToArray();
~~~

### Dolphin.IO.TextReader / StreamReader

Decode line-oriented or entire-text content from a file or stream with optional encoding, BOM detection, buffered reads and async helpers.

**API:** `StreamReader(path[,encoding,detectBOM,bufferSize])`, `StreamReader(stream[,encoding,...])`, `Read/ReadBlock`, `Peek`, `ReadLine/ReadToEnd`, `ReadAsync/ReadLineAsync/ReadToEndAsync`, `EndOfStream`, `CurrentEncoding`, `DiscardBufferedData`, `Close/Dispose`.

~~~csharp
using var reader = new Dolphin.IO.StreamReader("sd:/notes.txt");
string? firstLine = reader.ReadLine();
~~~

### Dolphin.IO.TextWriter / StreamWriter

Text output to files or streams with encoding, buffering, configurable newline, optional auto-flush and async write.

**API:** `StreamWriter(path[,append,encoding,bufferSize])`, `StreamWriter(stream[,encoding,bufferSize,leaveOpen])`, `Write/WriteLine`, `WriteAsync/WriteLineAsync`, `Flush/FlushAsync`, `NewLine`, `AutoFlush`, `Encoding`, `BaseStream`, `Close/Dispose`.

~~~csharp
using var writer = new Dolphin.IO.StreamWriter("sd:/notes.txt");
writer.WriteLine("Ready");
writer.Flush();
~~~

### Dolphin.Text.Encoding / UTF8Encoding / UnicodeEncoding

Encode/decode UTF-8, UTF-16 LE/BE, ASCII, and Latin-1 (code page 28591); choose strict or replacement behavior and optional BOM.

**API:** `Encoding.UTF8/Unicode/BigEndianUnicode/ASCII`, `GetEncoding(name/codePage)`, `GetBytes/GetString`, `GetChars/GetCharCount/GetByteCount`, `GetPreamble`, `UTF8Encoding(bom,strict)`, `UnicodeEncoding(bigEndian,bom,strict)`.

~~~csharp
byte[] bytes = Dolphin.Text.Encoding.UTF8.GetBytes("Hello");
string text = Dolphin.Text.Encoding.UTF8.GetString(bytes);
~~~

### DiscIdentity — on-disc GameCube game metadata and banner

Read the cached boot.bin header for a mounted `dvd:/` GameCube FST, whether
from a real game disc, ODE or ISO/GCM file, and parse optional `opening.bnr`
artwork and text. Returns `GameId` (six characters), `GameCode`,
`MakerCode`, disc number, revision, internal name and banner title,
publisher, short description. The optional `DiscBanner` has BNR1/BNR2
language selection plus 96x32 RGBA pixels compatible with
`Texture2D.Create`, tiled RGB5A3 pixels and unmodified raw data.
Banner strings use western Latin-1; raw banner data preserves Japanese
Shift-JIS text, which is not yet natively decoded.

**API:** `Storage.GetMountedDiscIdentity([DiscBannerLanguage])`,
`DiscIdentity.Title/GameId/ShortDescription/Banner/RawHeader`,
`DiscBanner.Title/Description/ShortTitle/Publisher/Rgba32/Rgb5A3/RawData`.

~~~csharp
var disc = Storage.GetMountedDiscIdentity(DiscBannerLanguage.Dutch);
string id = disc.GameId, title = disc.Title;
if (disc.Banner != null)
    using var texture = Texture2D.Create(96, 32, disc.Banner.Rgba32);
~~~

### Storage — unified GameCube game disc and ISO mounts

Every original GameCube FST uses `dvd:/`. For a physical or ODE-presented disc
call `MountGameCubeDisc()`; for a plain ISO/GCM image, give a full mounted
filesystem path to `MountDisc(path)` or `MountGameCubeDisc(path)`. Continue
using normal `Storage.GetFiles`, `ReadAllBytes` and `Open` on
`StorageDevice.DvdDrive`. GC Loader's optional SD filesystem mounts at
`gcloader:/` via `StorageDevice.GcLoader`.

**API:** `Storage.MountGameCubeDisc([isoPath])`,
`Storage.MountDisc(DiscFormat)`, `Storage.MountDisc(string imagePath)`,
`Storage.Unmount(StorageDevice.DvdDrive)`.

~~~csharp
if (Storage.MountDisc("sd:/games/game.iso")) {
    string[] names = Storage.GetFiles(StorageDevice.DvdDrive, "");
    Storage.Unmount(StorageDevice.DvdDrive);
}
~~~

## Auxiliary memory (ARAM)

On GameCube, ARAM is separate auxiliary RAM. Initialize it before allocating ARAM-backed streams, caching cold assets, or using assets that require it. Copies are asynchronous and native-managed; never assume a managed array stays pinned across a transfer.

### Dolphin.Memory.Aram / AramStatistics

Initialize the ARAM allocator; inspect availability, free space, total capacity, largest block and allocations.

**API:** `Initialize([reservedBytes])`, `IsAvailable`, `Statistics`, `TryAllocate(length,out buffer)`, `Allocate(length)`, `AramStatistics.CapacityBytes/AllocatedBytes/FreeBytes/LargestFreeBlockBytes/AllocationCount`.

~~~csharp
if (Aram.Initialize()) {
    int available = Aram.Statistics.FreeBytes;
}
~~~

### Dolphin.Memory.AramBuffer

Owned fixed-size ARAM allocation with asynchronous ranged transfers and disposal.

**API:** `Length`, `WriteAsync(source,sourceOffset,destOffset,count[,token])`, `ReadAsync(sourceOffset,destination,destOffset,count[,token])`, `Dispose()`.

~~~csharp
using var ram = Aram.Allocate(4096);
await ram.WriteAsync(bytes, 0, 0, bytes.Length);
await ram.ReadAsync(0, result, 0, result.Length);
~~~

### Dolphin.IO.AramStream

Seekable, bounded ARAM-backed `System.IO.Stream`, suitable for streaming large downloads without keeping them in the managed heap.

**API:** `AramStream(capacity[,writable])`, `Capacity`, `Read/ReadAsync`, `Write/WriteAsync`, `Length`, `Position`, `Seek`, `SetLength`, `Flush`, `Dispose`, `CanRead/CanWrite/CanSeek`.

~~~csharp
using var aram = new Dolphin.IO.AramStream(65536);
await source.CopyToAsync(aram, 4096);
aram.Position = 0;
~~~

### Dolphin.Memory.AramAssetCache

Budgeted ID-keyed cold-asset store in ARAM, with priorities, eviction rules and ranged prefetches.

**API:** `AramAssetCache(capacityBytes)`, `CapacityBytes`, `UsedBytes`, `Contains(id)`, `StoreAsync(id,data[,token,priority,evictable])`, `ReadAsync(id,offset,buffer,destOffset,count[,token])`, `PrefetchAsync(id,offset,count[,token])`, `Remove(id)`, `Dispose`.

~~~csharp
using var cache = new AramAssetCache(1024 * 1024);
await cache.StoreAsync("level1", levelBytes);
byte[] head = await cache.PrefetchAsync("level1", 0, 32);
~~~


## Networking and HTTP

Networking requires `await NetworkRuntime.StartAsync(...)`; the native service thread then makes progress without application pumping. Supported protocols are **IPv4 TCP/UDP, DNS and plain HTTP**; no TLS/HTTPS. Standard `System.Net.*`, `System.Net.Sockets.*` and `System.Net.Http.*` signatures are lowered to these implementations.

### Dolphin.Network.NetworkRuntime / NetworkOptions

Start/stop GameCube networking with DHCP or static IPv4, DNS servers, retry timeout and operation status. `Pump` remains a compatibility hook but is not needed for ordinary awaits.

**API:** `StartAsync()`, `StartAsync(options,token)`, `IsReady`, `Options`, `PendingOperations`, `Stop()`, `Pump(maxOperations,maxMilliseconds)`, `NetworkOptions.UseDhcp/Address/Netmask/Gateway/DnsServers/DnsPort/OperationTimeoutMilliseconds`.

~~~csharp
await NetworkRuntime.StartAsync();
try { /* perform HTTP or socket operations */ }
finally { if (NetworkRuntime.IsReady) NetworkRuntime.Stop(); }
~~~

### IPAddress / IPEndPoint / EndPoint

IPv4 parsing/formatting and network socket endpoint representation.

**API:** `IPAddress.Parse(text)`, `IPAddress.TryParse(text,out address)`, `IPAddress(byte[])`, `Any/Loopback/Broadcast`, `GetAddressBytes()`, `Format()`, `IsLoopback(address)`, `IPEndPoint(address,port)`, `Address`, `Port`.

~~~csharp
var ip = Dolphin.Network.IPAddress.Parse("192.168.0.3");
var endpoint = new Dolphin.Network.IPEndPoint(ip, 8080);
~~~

### Dns / IPHostEntry

Asynchronous DNS host lookup with IPv4 A records and CNAME handling.

**API:** `Dns.GetHostAddressesAsync(host[,token])`, `Dns.GetHostEntryAsync(host)`, `IPHostEntry.HostName/Aliases/AddressList`.

~~~csharp
var addresses = await Dolphin.Network.Dns.GetHostAddressesAsync("example.com");
var first = addresses[0];
~~~

### Socket / socket enums / SocketException

Lower-level asynchronous IPv4 TCP/UDP socket operations, endpoint binding/listening and nonblocking datagrams.

**API:** `Socket(family,type,protocol)`, `Bind(endpoint)`, `Listen(backlog)`, `ConnectAsync(endpoint[,token])`, `AcceptAsync([token])`, `ReceiveAsync(buffer,offset,count,flags[,token])`, `SendAsync(...)`, `TryReceiveFrom(...)`, `TrySendTo(...)`, `LocalEndPoint`, `RemoteEndPoint`, `NoDelay`, `EnableBroadcast`, `Close/Dispose`, `AddressFamily.InterNetwork`, `SocketType.Stream/Dgram`, `ProtocolType.Tcp/Udp`, `SocketFlags.None`, `SocketException.ErrorCode`.

~~~csharp
using var socket = new Dolphin.Network.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("192.168.0.3"), 9000));
~~~

### UdpClient / UdpReceiveResult

Datagram client with bind/default peer, send and receive, datagram-size cap and remote endpoint metadata.

**API:** `UdpClient()`, `UdpClient(port/IPEndPoint)`, `Connect(endpoint)`, `SendAsync(bytes,count[,endpoint,token])`, `ReceiveAsync([token])`, `MaximumDatagramBytes`, `Client`, `Close/Dispose`, `UdpReceiveResult.Buffer/RemoteEndPoint`.

~~~csharp
using var udp = new UdpClient(9000);
var incoming = await udp.ReceiveAsync();
byte[] payload = incoming.Buffer;
~~~

### TcpClient / TcpListener

Convenience TCP connect, read/write streams and listening/accepting sockets.

**API:** `TcpClient()`, `ConnectAsync(IPAddress,int)`, `ConnectAsync(host,int[,token])`, `GetStream()`, `NoDelay`, `Client`, `Close/Dispose`, `TcpListener(IPAddress,port/IPEndPoint)`, `Start([backlog])`, `AcceptTcpClientAsync([token])`, `LocalEndpoint`, `Stop()`.

~~~csharp
using var client = new TcpClient();
await client.ConnectAsync("192.168.0.3", 9000);
var stream = client.GetStream();
~~~

### NetworkStream

Duplex socket-backed byte stream, async reads and full async writes; synchronous reads and writes intentionally throw.

**API:** `NetworkStream(socket[,ownsSocket])`, `ReadAsync(buffer,offset,count,token)`, `WriteAsync(buffer,offset,count,token)`, `Flush()`, `CanRead/CanWrite`, `Dispose()`.

~~~csharp
var stream = client.GetStream();
byte[] buffer = new byte[256];
int received = await stream.ReadAsync(buffer, 0, buffer.Length);
~~~

### HttpClient / HttpCompletionOption

Bounded plain HTTP client supporting GET and request messages, redirect limits, buffered replies or streamed response headers.

**API:** `GetAsync(uri[,completionOption,token])`, `SendAsync(request[,completionOption,token])`, `GetStringAsync(uri)`, `GetByteArrayAsync(uri)`, `GetStreamAsync(uri)`, `MaximumResponseBytes`, `MaximumRedirects`, `DefaultRequestHeaders`, `Dispose()`, `ResponseContentRead/ResponseHeadersRead`.

~~~csharp
using var http = new HttpClient();
using var response = await http.GetAsync("http://192.168.0.3:8080/data");
response.EnsureSuccessStatusCode();
string text = await response.Content.ReadAsStringAsync();
~~~

### HttpRequestMessage / HttpResponseMessage

Explicit HTTP method, URL, request headers/body and response status, reason, response headers/body; verify status before consuming.

**API:** `HttpRequestMessage(method,uri)`, `Method`, `RequestUri`, `Headers`, `Content`, `Dispose()`, `HttpResponseMessage.StatusCode/ReasonPhrase/IsSuccessStatusCode/Headers/Content`, `EnsureSuccessStatusCode()`.

~~~csharp
using var request = new HttpRequestMessage(HttpMethod.Post, "http://server.test/data");
request.Content = new StringContent("hello");
using var response = await http.SendAsync(request);
~~~

### HttpContent / ByteArrayContent / StringContent / HttpHeaders / HttpMethod

Byte/text request bodies; response body as text, bytes or stream; validated header names/values, and GET/HEAD/POST/PUT/DELETE methods.

**API:** `ReadAsByteArrayAsync()`, `ReadAsStringAsync()`, `ReadAsStreamAsync()`, `HttpHeaders.Add(name,value)`, `Get(name)`, `HttpMethod.Get/Head/Post/Put/Delete`, `ByteArrayContent(bytes)`, `StringContent(text)`, `Dispose()`.

~~~csharp
var request = new HttpRequestMessage(HttpMethod.Put, "http://server.test/item");
request.Headers.Add("x-client", "gamecube");
request.Content = new ByteArrayContent(new byte[] { 1, 2 });
~~~

### JsonReader / JsonTokenType

Bounded forward-only JSON tokenizer, avoiding heavyweight DOM or `System.Text.Json`; `GetInt32` accepts an integer Number token.

**API:** `JsonReader(text)`, `Read()`, `TokenType`, `Value`, `GetInt32()`, `JsonTokenType.StartObject/PropertyName/String/Number/True/False/Null/StartArray/EndArray/EndObject`.

~~~csharp
var json = new Dolphin.Serialization.Json.JsonReader("{\"score\":42}");
while (json.Read())
    if (json.TokenType == JsonTokenType.Number) score = json.GetInt32();
~~~

## Tasks and cancellation

The managed program begins on the **application executor**. Normal pending `await` captures the current executor; `Task.Run` and pending `ConfigureAwait(false)` use the service executor. Rendering and input must remain on the application executor. No application-level async pumping is needed.

### Task / Task<T> / System.Threading.Tasks

Ordinary C# async state machines, tasks, result/error/cancellation state, task factories and composition.

**API:** `CompletedTask`, `FromResult(value)`, `FromException(exception)`, `FromCanceled(token)`, `Run(action/function[,token])`, `Yield()`, `WhenAll(Task[])`, `WhenAny(Task[])`, `Delay(ms[,token])`, `IsCompleted/IsCompletedSuccessfully/IsFaulted/IsCanceled`, `Wait()`, `Result`, `GetAwaiter()`, `ConfigureAwait(bool)`.

~~~csharp
int value = await System.Threading.Tasks.Task.Run(() => ComputeScore());
await System.Threading.Tasks.Task.Delay(100);
await System.Threading.Tasks.Task.WhenAll(new System.Threading.Tasks.Task[] { SaveAsync(), UploadAsync() });
~~~

### TaskCompletionSource<T>

Bridge callback-style work to an awaitable task; manually complete, fail or cancel the pending result.

**API:** `Task`, `SetResult(value)`, `SetException(error)`, `SetCanceled()`.

~~~csharp
var completion = new TaskCompletionSource<int>();
completion.SetResult(42);
int value = await completion.Task;
~~~

### Dolphin.Threading.Tasks.Scheduler

Low-level managed executor queue and compatibility pump; use `Post` for application-executor callbacks, not as a manual async loop.

**API:** `IsApplicationThread`, `Post(Action)`, `Pump()`, `Pump(maxActions)`, `RunUntilIdle()`.

~~~csharp
Dolphin.Threading.Tasks.Scheduler.Post(() => UpdateUi());
~~~

### CancellationTokenSource / CancellationToken

Cooperative cancellation for supported async I/O, networking, tasks and timers; no timed cancellation or callback registration.

**API:** `CancellationTokenSource.Token`, `IsCancellationRequested`, `Cancel()`, `Dispose()`, `CancellationToken.None`, `CanBeCanceled`, `ThrowIfCancellationRequested()`.

~~~csharp
using var cts = new System.Threading.CancellationTokenSource();
cts.Cancel();
cts.Token.ThrowIfCancellationRequested();
~~~

### Compiler-generated awaitables and builders

Compiler-facing glue for `async Task`/`Task<T>`, awaiters, `YieldAwaitable`, and `ConfigureAwait`. Usually generated by C#; not application APIs that need direct invocation.

**API:** `INotifyCompletion`, `ICriticalNotifyCompletion`, `IAsyncStateMachine`, `TaskAwaiter/TaskAwaiter<T>`, `ConfiguredTaskAwaitable<T>`, `YieldAwaitable`, `AsyncTaskMethodBuilder<T>`.

~~~csharp
async System.Threading.Tasks.Task<int> AnswerAsync() {
    await System.Threading.Tasks.Task.Yield();
    return 42;
}
~~~

## Collections and core runtime

`System.Collections.Generic.List<T>`, comparer interfaces, arrays and basic core APIs are mapped to the available managed implementation. This is a targeted subset: `Dictionary<TKey,TValue>`, `HashSet<T>`, and LINQ are **not** part of the implemented contract.

### List<T>

Resizable indexed collection with ranges, predicates, sorting, binary search and version-checked enumeration. The normal `System.Collections.Generic.List<T>` maps to it.

**API:** `List()`, `List(capacity)`, `List(IEnumerable<T>)`, `Count`, `Capacity`, `this[index]`, `Add`, `AddRange`, `Insert`, `InsertRange`, `Clear`, `Remove/RemoveAt/RemoveRange/RemoveAll`, `IndexOf/LastIndexOf/Contains`, `Find/FindLast/FindAll`, `FindIndex/FindLastIndex`, `Exists`, `TrueForAll`, `ForEach`, `CopyTo`, `ToArray`, `GetRange`, `TrimExcess`, `Reverse`, `Sort`, `BinarySearch`, `GetEnumerator`.

~~~csharp
var items = new System.Collections.Generic.List<int>();
items.Add(3); items.Add(1);
items.Sort();
int smallest = items[0];
~~~

### EqualityComparer<T> / Comparer<T> / StringComparer

Default typed equality, hashing, order and delegate comparers; ordinal and Unicode simple ordinal-ignore-case string comparison.

**API:** `EqualityComparer<T>.Default`, `Equals(x,y)`, `GetHashCode(x)`, `Comparer<T>.Default`, `Compare(x,y)`, `Comparer<T>.Create(comparison)`, `StringComparer.Ordinal`, `StringComparer.OrdinalIgnoreCase`.

~~~csharp
bool equal = System.Collections.Generic.EqualityComparer<int>.Default.Equals(1, 1);
int order = System.Collections.Generic.Comparer<int>.Default.Compare(1, 2);
~~~

### ArrayEnumerable<T>

Adapter that explicitly exposes a `T[]` through a standard `IEnumerable<T>` and enumerator.

**API:** `ArrayEnumerable<T>(items)`, `Enumerate(items)`, `GetEnumerator()`, `Current`, `MoveNext()`, `Reset()`, `Dispose()`.

~~~csharp
var sequence = new Dolphin.Collections.ArrayEnumerable<int>(new int[] { 1, 2 });
foreach (int number in sequence) Log(number);
~~~

### Nullable<T> / Nullable helper

Selected nullable primitives and generic comparisons, `HasValue`, `Value`, `GetValueOrDefault`, equality and boxing.

**API:** `Nullable<T>`, `HasValue`, `Value`, `GetValueOrDefault()`, `Nullable.Equals<T>(x,y)`, `Nullable.Compare<T>(x,y)`.

~~~csharp
int? score = null;
int displayed = score.GetValueOrDefault();
~~~

### Core language and collection interfaces

Supported subset of `Object`, `String`, `Array`, exceptions, constructors/inheritance, virtual/interface calls, delegates, typed catches, boxing, generics, and collection interfaces. Consult documented limits below.

**API:** `Object.Equals/GetHashCode`, `String.Length/Substring/Concat(string,string)/StartsWith/EndsWith/Contains/IndexOf`, `Array.Length/Copy/Clear/IndexOf`, `IEnumerable<T>`, `ICollection<T>`, `IList<T>`, `IReadOnlyList<T>`, `KeyValuePair<K,V>`, `ArraySegment<T>`, `IDisposable`, `Action/Func`.

~~~csharp
string label = string.Concat("LEVEL ", "1");
var numbers = new int[] { 1, 2, 3 };
Array.Copy(numbers, 0, numbers, 1, 2);
~~~

### Primitive formatting / Guid / AppContext / Console

Limited `ToString()` on primitive values and selected native runtime primitives for GUIDs, basic console output and app-context probes. GUID randomness is **not cryptographically secure** on GameCube; framework mapping coverage is narrower than desktop .NET.

**API:** `int/long/uint/ulong/bool/char ToString()`, `Guid.Parse/NewGuid/ToString`, `Console.Write/WriteLine (selected)`, `AppContext.BaseDirectory/TryGetSwitch (selected)`.

~~~csharp
Dolphin.Diagnostics.DebugOverlay.WriteLine(42.ToString());
~~~


## Low-level GX, GU and VIDEO

These are the generated libogc2 GPU/video bindings in `Dolphin.Graphics.Raw`: **351 C-named commands** and **678 numeric constants**. Raw use demands native ABI layouts, aligned buffers, explicit cache handling and carefully balanced draw primitives; never pass addresses from the moving managed heap. A raw pass must be opened/closed through `GraphicsDevice` when using managed presentation.

### NativeBuffer / NativeObjectKind

Own aligned native RAM or allocate correctly sized GX objects; write/read arrays, maintain CPU/GPU cache coherency, and dispose after native/GPU work.

**API:** `Allocate(bytes[,alignment])`, `AllocateObject(kind)`, `Length`, `Address`, `UncachedAddress`, `Write(byte[]/float[]/ushort[])`, `Read(byte[])`, `Flush(offset,count)`, `Invalidate(offset,count)`, `Dispose()`, `NativeObjectKind.Fifo/Texture/Palette/Light/VideoMode/Matrix3x4/Matrix4x4/etc.`.

~~~csharp
using var matrix = NativeBuffer.AllocateObject(NativeObjectKind.Matrix3x4);
GU.guMtxIdentity(matrix.Address);
matrix.Flush();
~~~

### NativeAddress / GXColor / GXColorS10

Native address plus offsets and the native-value GX color structs. `UnsafeFromUInt64` bypasses managed ownership checks and needs care.

**API:** `NativeAddress.Null`, `IsNull`, `Add(bytes)`, `UnsafeFromUInt64(address)`, `GXColor(r,g,b[,a])`, `GXColorS10(r,g,b,a)`.

~~~csharp
using var fifo = NativeBuffer.Allocate(16384);
NativeAddress next = fifo.Address.Add(32);
var color = new GXColor(0, 0, 0);
~~~

### GX

Direct GX hardware calls to configure rendering state, lighting, textures, vertex streams, framebuffers, display lists, and command submission.

**API:** `GX.GX_Begin(...)`, `GX.GX_End()`, `GX.GX_SetCullMode(byte)`, `GX.GX_SetVtxDesc(byte,byte)`, `GX.GX_LoadPosMtxImm(...)`, `GX.GX_SetCopyClear(...)`, `GX.GX_CopyDisp(...)`, `GX.GX_Init(...)`.

~~~csharp
device.BeginRaw();
GX.GX_SetCullMode((byte)GXConstants.GX_CULL_NONE);
device.EndRaw();
~~~

### GU

Low-level native 3×4/4×4 matrices, projections, look-at transforms and 3D vector math.

**API:** `guMtxIdentity(address)`, `guMtxConcat(...)`, `guMtx44Identity(address)`, `guPerspective(...)`, `guLookAt(...)`, `guVecNormalize(...)`.

~~~csharp
using var matrix = NativeBuffer.AllocateObject(NativeObjectKind.Matrix4x4);
GU.guMtx44Identity(matrix.Address);
~~~

### VIDEO

Low-level television/display mode, framebuffer, retrace/vsync and pan/interruption operations. Do not replace the managed display setup mid-frame.

**API:** `VIDEO_Init()`, `VIDEO_GetPreferredMode(...)`, `VIDEO_Configure(...)`, `VIDEO_GetCurrentTvMode()`, `VIDEO_WaitVSync()`, `VIDEO_SetNextFramebuffer(...)`.

~~~csharp
uint mode = VIDEO.VIDEO_GetCurrentTvMode();
~~~

### GXConstants

Numeric ids and enum values consumed by raw GX commands (vertex formats, culling, lighting, TEV, blend/depth modes, copy/display and performance).

**API:** `GX_CULL_NONE`, `GX_TRIANGLES`, `GX_VA_POS`, `GX_TF_RGBA8`, `GX_ALWAYS`.

~~~csharp
GX.GX_SetCullMode((byte)GXConstants.GX_CULL_NONE);
~~~

### Complete generated method index

This inventory lists every generated public raw command, grouped by purpose. For full arguments/return types, consult [GX](../src/DolphinDotNet.GameCube/Dolphin/Graphics/Raw/GX.Generated.cs), [GU](../src/DolphinDotNet.GameCube/Dolphin/Graphics/Raw/GU.Generated.cs) and [VIDEO](../src/DolphinDotNet.GameCube/Dolphin/Graphics/Raw/VIDEO.Generated.cs). All examples above are five lines or less.

#### GX frame, metric, primitive, texture and display helpers (38)

- `GX_AbortFrame`, `GX_AdjustForOverscan`, `GX_Begin`, `GX_BeginDispList`, `GX_CallDispList`, `GX_ClearBoundingBox`, `GX_ClearGPMetric`, `GX_ClearPixMetric`, `GX_ClearVCacheMetric`
- `GX_ClearVtxDesc`, `GX_CopyDisp`, `GX_CopyTex`, `GX_DisableBreakPt`, `GX_DrawDone`, `GX_EnableBreakPt`, `GX_EnableTexOffsets`, `GX_End`, `GX_EndDispList`
- `GX_FastBegin`, `GX_FastCallDispList`, `GX_Flush`, `GX_InvVtxCache`, `GX_InvalidateTexAll`, `GX_InvalidateTexRegion`, `GX_MatrixIndex1x8`, `GX_PixModeSync`, `GX_PreloadEntireTexture`
- `GX_ReadBoundingBox`, `GX_ReadClksPerVtx`, `GX_ReadGPMetric`, `GX_ReadPixMetric`, `GX_ReadVCacheMetric`, `GX_ReadXfRasMetric`, `GX_RedirectWriteGatherPipe`, `GX_ResetOverflowCount`, `GX_RestoreWriteGatherPipe`
- `GX_TexModeSync`, `GX_WaitDrawDone`

#### GX vertex attribute streams (36)

- `GX_Color1u16`, `GX_Color1u32`, `GX_Color1x16`, `GX_Color1x8`, `GX_Color3f32`, `GX_Color3u8`, `GX_Color4u8`, `GX_Normal1x16`, `GX_Normal1x8`
- `GX_Normal3f32`, `GX_Normal3s16`, `GX_Normal3s8`, `GX_Position1x16`, `GX_Position1x8`, `GX_Position2f32`, `GX_Position2s16`, `GX_Position2s8`, `GX_Position2u16`
- `GX_Position2u8`, `GX_Position3f32`, `GX_Position3s16`, `GX_Position3s8`, `GX_Position3u16`, `GX_Position3u8`, `GX_TexCoord1f32`, `GX_TexCoord1s16`, `GX_TexCoord1s8`
- `GX_TexCoord1u16`, `GX_TexCoord1u8`, `GX_TexCoord1x16`, `GX_TexCoord1x8`, `GX_TexCoord2f32`, `GX_TexCoord2s16`, `GX_TexCoord2s8`, `GX_TexCoord2u16`, `GX_TexCoord2u8`

#### GX queries and counters (39)

- `GX_GetCPUFifo`, `GX_GetCurrentGXThread`, `GX_GetDrawSync`, `GX_GetFifoBase`, `GX_GetFifoCount`, `GX_GetFifoPtrs`, `GX_GetFifoSize`, `GX_GetFifoWrap`, `GX_GetGPFifo`
- `GX_GetGPStatus`, `GX_GetNumXfbLines`, `GX_GetOverflowCount`, `GX_GetScissor`, `GX_GetTexBufferSize`, `GX_GetTexObjAll`, `GX_GetTexObjBiasClamp`, `GX_GetTexObjData`, `GX_GetTexObjEdgeLOD`
- `GX_GetTexObjFmt`, `GX_GetTexObjHeight`, `GX_GetTexObjLODAll`, `GX_GetTexObjLODBias`, `GX_GetTexObjMagFilt`, `GX_GetTexObjMaxAniso`, `GX_GetTexObjMaxLOD`, `GX_GetTexObjMinFilt`, `GX_GetTexObjMinLOD`
- `GX_GetTexObjMipMap`, `GX_GetTexObjTlut`, `GX_GetTexObjUserData`, `GX_GetTexObjWidth`, `GX_GetTexObjWrapS`, `GX_GetTexObjWrapT`, `GX_GetTlutObjAll`, `GX_GetTlutObjData`, `GX_GetTlutObjFmt`
- `GX_GetTlutObjNumEntries`, `GX_GetVtxDescv`, `GX_GetYScaleFactor`

#### GX native object initialization (40)

- `GX_Init`, `GX_InitFifoBase`, `GX_InitFifoLimits`, `GX_InitFifoPtrs`, `GX_InitFogAdjTable`, `GX_InitLightAttn`, `GX_InitLightAttnA`, `GX_InitLightAttnK`, `GX_InitLightColor`
- `GX_InitLightDir`, `GX_InitLightDirv`, `GX_InitLightDistAttn`, `GX_InitLightPos`, `GX_InitLightPosv`, `GX_InitLightShininess`, `GX_InitLightSpot`, `GX_InitSpecularDir`, `GX_InitSpecularDirHA`
- `GX_InitSpecularDirHAv`, `GX_InitSpecularDirv`, `GX_InitTexCacheRegion`, `GX_InitTexObj`, `GX_InitTexObjBiasClamp`, `GX_InitTexObjCI`, `GX_InitTexObjData`, `GX_InitTexObjEdgeLOD`, `GX_InitTexObjFilterMode`
- `GX_InitTexObjLOD`, `GX_InitTexObjLODBias`, `GX_InitTexObjMaxAniso`, `GX_InitTexObjMaxLOD`, `GX_InitTexObjMinLOD`, `GX_InitTexObjTlut`, `GX_InitTexObjUserData`, `GX_InitTexObjWrapMode`, `GX_InitTexPreloadRegion`
- `GX_InitTlutObj`, `GX_InitTlutObjData`, `GX_InitTlutRegion`, `GX_InitXfRasMetric`

#### GX resource/matrix uploads (13)

- `GX_LoadLightObj`, `GX_LoadLightObjIdx`, `GX_LoadNrmMtxIdx3x3`, `GX_LoadNrmMtxImm`, `GX_LoadNrmMtxImm3x3`, `GX_LoadPosMtxIdx`, `GX_LoadPosMtxImm`, `GX_LoadProjectionMtx`, `GX_LoadTexMtxIdx`
- `GX_LoadTexMtxImm`, `GX_LoadTexObj`, `GX_LoadTexObjPreloaded`, `GX_LoadTlut`

#### GX EFB pixel access (12)

- `GX_PeekARGB`, `GX_PeekZ`, `GX_PokeARGB`, `GX_PokeAlphaMode`, `GX_PokeAlphaRead`, `GX_PokeAlphaUpdate`, `GX_PokeBlendMode`, `GX_PokeColorUpdate`, `GX_PokeDither`
- `GX_PokeDstAlpha`, `GX_PokeZ`, `GX_PokeZMode`

#### GX hardware-state setters (89)

- `GX_SetAlphaCompare`, `GX_SetAlphaUpdate`, `GX_SetArray`, `GX_SetBlendMode`, `GX_SetBreakPtCallback`, `GX_SetCPUFifo`, `GX_SetChanAmbColor`, `GX_SetChanCtrl`, `GX_SetChanMatColor`
- `GX_SetClipMode`, `GX_SetCoPlanar`, `GX_SetColorUpdate`, `GX_SetCopyClamp`, `GX_SetCopyClear`, `GX_SetCopyFilter`, `GX_SetCullMode`, `GX_SetCurrentGXThread`, `GX_SetCurrentMtx`
- `GX_SetDispCopyDst`, `GX_SetDispCopyFrame2Field`, `GX_SetDispCopyGamma`, `GX_SetDispCopySrc`, `GX_SetDispCopyYScale`, `GX_SetDither`, `GX_SetDrawDone`, `GX_SetDrawDoneCallback`, `GX_SetDrawSync`
- `GX_SetDrawSyncCallback`, `GX_SetDstAlpha`, `GX_SetFieldMask`, `GX_SetFieldMode`, `GX_SetFog`, `GX_SetFogColor`, `GX_SetFogRangeAdj`, `GX_SetGPFifo`, `GX_SetGPMetric`
- `GX_SetIndTexCoordScale`, `GX_SetIndTexMatrix`, `GX_SetIndTexOrder`, `GX_SetLineWidth`, `GX_SetMisc`, `GX_SetNumChans`, `GX_SetNumIndStages`, `GX_SetNumTevStages`, `GX_SetNumTexGens`
- `GX_SetPixelFmt`, `GX_SetPointSize`, `GX_SetScissor`, `GX_SetScissorBoxOffset`, `GX_SetTevAlphaIn`, `GX_SetTevAlphaOp`, `GX_SetTevColor`, `GX_SetTevColorIn`, `GX_SetTevColorOp`
- `GX_SetTevColorS10`, `GX_SetTevDirect`, `GX_SetTevIndBumpST`, `GX_SetTevIndBumpXYZ`, `GX_SetTevIndRepeat`, `GX_SetTevIndTile`, `GX_SetTevIndWarp`, `GX_SetTevIndirect`, `GX_SetTevKAlphaSel`
- `GX_SetTevKColor`, `GX_SetTevKColorS10`, `GX_SetTevKColorSel`, `GX_SetTevOp`, `GX_SetTevOrder`, `GX_SetTevSwapMode`, `GX_SetTevSwapModeTable`, `GX_SetTexCoordBias`, `GX_SetTexCoordCylWrap`
- `GX_SetTexCoordGen`, `GX_SetTexCoordGen2`, `GX_SetTexCoordScaleManually`, `GX_SetTexCopyDst`, `GX_SetTexCopySrc`, `GX_SetTexRegionCallback`, `GX_SetTlutRegionCallback`, `GX_SetVCacheMetric`, `GX_SetViewport`
- `GX_SetViewportJitter`, `GX_SetVtxAttrFmt`, `GX_SetVtxAttrFmtv`, `GX_SetVtxDesc`, `GX_SetVtxDescv`, `GX_SetZCompLoc`, `GX_SetZMode`, `GX_SetZTexture`

#### GU projection and other transforms (15)

- `guFrustum`, `guLightFrustum`, `guLightOrtho`, `guLightPerspective`, `guLookAt`, `guOrtho`, `guPerspective`, `guQuatAdd`, `guQuatDotProduct`
- `guQuatInverse`, `guQuatMtx`, `guQuatMultiply`, `guQuatNormalize`, `guQuatScale`, `guQuatSub`

#### GU 4×4 matrices (12)

- `guMtx44Concat`, `guMtx44Copy`, `guMtx44Identity`, `guMtx44Inverse`, `guMtx44RotAxisRad`, `guMtx44RotRad`, `guMtx44RotTrig`, `guMtx44Scale`, `guMtx44ScaleApply`
- `guMtx44Trans`, `guMtx44TransApply`, `guMtx44Transpose`

#### GU 3×4 matrices (17)

- `guMtxApplyScale`, `guMtxApplyTrans`, `guMtxConcat`, `guMtxCopy`, `guMtxIdentity`, `guMtxInvXpose`, `guMtxInverse`, `guMtxQuat`, `guMtxReflect`
- `guMtxRotAxisRad`, `guMtxRotRad`, `guMtxRotTrig`, `guMtxScale`, `guMtxScaleApply`, `guMtxTrans`, `guMtxTransApply`, `guMtxTranspose`

#### GU vector algebra (9)

- `guVecAdd`, `guVecCross`, `guVecDotProduct`, `guVecHalfAngle`, `guVecMultiply`, `guVecMultiplySR`, `guVecNormalize`, `guVecScale`, `guVecSub`

#### VIDEO — framebuffer, output mode, retrace (31)

- `VIDEO_ClearFrameBuffer`, `VIDEO_Configure`, `VIDEO_ConfigurePan`, `VIDEO_DisablePositionInterrupt`, `VIDEO_EnablePositionInterrupt`, `VIDEO_Flush`, `VIDEO_GetAdjustingValues`, `VIDEO_GetAspectRatio`, `VIDEO_GetCurrentFramebuffer`
- `VIDEO_GetCurrentLine`, `VIDEO_GetCurrentTvMode`, `VIDEO_GetFrameBufferPan`, `VIDEO_GetFrameBufferSize`, `VIDEO_GetNextField`, `VIDEO_GetNextFramebuffer`, `VIDEO_GetPreferredMode`, `VIDEO_GetRetraceCount`, `VIDEO_GetRetraceRate`
- `VIDEO_GetScanMode`, `VIDEO_HaveComponentCable`, `VIDEO_Init`, `VIDEO_Set3D`, `VIDEO_SetAdjustingValues`, `VIDEO_SetBlack`, `VIDEO_SetNextFramebuffer`, `VIDEO_SetNextRightFramebuffer`, `VIDEO_SetPostRetraceCallback`
- `VIDEO_SetPreRetraceCallback`, `VIDEO_WaitForFlush`, `VIDEO_WaitForRetrace`, `VIDEO_WaitVSync`

### Complete numeric constants index (678)

These constants correspond to the IDs and enum values in [GXConstants.Generated.cs](../src/DolphinDotNet.GameCube/Dolphin/Graphics/Raw/GXConstants.Generated.cs). They are **values**, not invocable methods, and the exact numeric values live in that source. Alphabetical index:

#### A (15)

- `GX_AF_NONE`, `GX_AF_SPEC`, `GX_AF_SPOT`, `GX_ALPHA0`, `GX_ALPHA1`, `GX_ALPHA_BUMP`, `GX_ALPHA_BUMPN`, `GX_ALWAYS`, `GX_ANISO_1`, `GX_ANISO_2`
- `GX_ANISO_4`, `GX_AOP_AND`, `GX_AOP_OR`, `GX_AOP_XNOR`, `GX_AOP_XOR`

#### B (19)

- `GX_BIGTLUT0`, `GX_BIGTLUT1`, `GX_BIGTLUT2`, `GX_BIGTLUT3`, `GX_BLEND`, `GX_BL_DSTALPHA`, `GX_BL_DSTCLR`, `GX_BL_INVDSTALPHA`, `GX_BL_INVDSTCLR`, `GX_BL_INVSRCALPHA`
- `GX_BL_INVSRCCLR`, `GX_BL_ONE`, `GX_BL_SRCALPHA`, `GX_BL_SRCCLR`, `GX_BL_ZERO`, `GX_BM_BLEND`, `GX_BM_LOGIC`, `GX_BM_NONE`, `GX_BM_SUBTRACT`

#### C (61)

- `GX_CA_A0`, `GX_CA_A1`, `GX_CA_A2`, `GX_CA_APREV`, `GX_CA_KONST`, `GX_CA_ONE`, `GX_CA_RASA`, `GX_CA_TEXA`, `GX_CA_ZERO`, `GX_CC_A0`
- `GX_CC_A1`, `GX_CC_A2`, `GX_CC_APREV`, `GX_CC_C0`, `GX_CC_C1`, `GX_CC_C2`, `GX_CC_CPREV`, `GX_CC_HALF`, `GX_CC_KONST`, `GX_CC_ONE`
- `GX_CC_QUARTER`, `GX_CC_RASA`, `GX_CC_RASC`, `GX_CC_TEXA`, `GX_CC_TEXC`, `GX_CC_ZERO`, `GX_CH_ALPHA`, `GX_CH_BLUE`, `GX_CH_GREEN`, `GX_CH_RED`
- `GX_CLAMP`, `GX_CLAMP_BOTTOM`, `GX_CLAMP_NONE`, `GX_CLAMP_TOP`, `GX_CLIP_DISABLE`, `GX_CLIP_ENABLE`, `GX_CLIP_REJECT_DISABLE`, `GX_CLR_RGB`, `GX_CLR_RGBA`, `GX_COLOR0`
- `GX_COLOR0A0`, `GX_COLOR1`, `GX_COLOR1A1`, `GX_COLORNULL`, `GX_COLORZERO`, `GX_COLOR_NULL`, `GX_COLOR_ZERO`, `GX_COPY_INTERLACED`, `GX_COPY_INTLC_EVEN`, `GX_COPY_INTLC_ODD`
- `GX_COPY_MIPMAP`, `GX_COPY_NONE`, `GX_COPY_PROGRESSIVE`, `GX_CS_DIVIDE_2`, `GX_CS_SCALE_1`, `GX_CS_SCALE_2`, `GX_CS_SCALE_4`, `GX_CULL_ALL`, `GX_CULL_BACK`, `GX_CULL_FRONT`
- `GX_CULL_NONE`

#### D (31)

- `GX_DA_GENTLE`, `GX_DA_MEDIUM`, `GX_DA_OFF`, `GX_DA_STEEP`, `GX_DECAL`, `GX_DF_CLAMP`, `GX_DF_NONE`, `GX_DF_SIGNED`, `GX_DIRECT`, `GX_DISABLE`
- `GX_DTTIDENTITY`, `GX_DTTMTX0`, `GX_DTTMTX1`, `GX_DTTMTX10`, `GX_DTTMTX11`, `GX_DTTMTX12`, `GX_DTTMTX13`, `GX_DTTMTX14`, `GX_DTTMTX15`, `GX_DTTMTX16`
- `GX_DTTMTX17`, `GX_DTTMTX18`, `GX_DTTMTX19`, `GX_DTTMTX2`, `GX_DTTMTX3`, `GX_DTTMTX4`, `GX_DTTMTX5`, `GX_DTTMTX6`, `GX_DTTMTX7`, `GX_DTTMTX8`
- `GX_DTTMTX9`

#### E (2)

- `GX_ENABLE`, `GX_EQUAL`

#### F (21)

- `GX_F32`, `GX_FALSE`, `GX_FIFO_HIWATERMARK`, `GX_FIFO_MINSIZE`, `GX_FIFO_OBJSIZE`, `GX_FOG_EXP`, `GX_FOG_EXP2`, `GX_FOG_LIN`, `GX_FOG_NONE`, `GX_FOG_ORTHO_EXP`
- `GX_FOG_ORTHO_EXP2`, `GX_FOG_ORTHO_LIN`, `GX_FOG_ORTHO_REVEXP`, `GX_FOG_ORTHO_REVEXP2`, `GX_FOG_PERSP_EXP`, `GX_FOG_PERSP_EXP2`, `GX_FOG_PERSP_LIN`, `GX_FOG_PERSP_REVEXP`, `GX_FOG_PERSP_REVEXP2`, `GX_FOG_REVEXP`
- `GX_FOG_REVEXP2`

#### G (5)

- `GX_GEQUAL`, `GX_GM_1_0`, `GX_GM_1_7`, `GX_GM_2_2`, `GX_GREATER`

#### I (49)

- `GX_IDENTITY`, `GX_INDEX16`, `GX_INDEX8`, `GX_INDTEXSTAGE0`, `GX_INDTEXSTAGE1`, `GX_INDTEXSTAGE2`, `GX_INDTEXSTAGE3`, `GX_ITBA_OFF`, `GX_ITBA_S`, `GX_ITBA_T`
- `GX_ITBA_U`, `GX_ITB_NONE`, `GX_ITB_S`, `GX_ITB_ST`, `GX_ITB_STU`, `GX_ITB_SU`, `GX_ITB_T`, `GX_ITB_TU`, `GX_ITB_U`, `GX_ITF_3`
- `GX_ITF_4`, `GX_ITF_5`, `GX_ITF_8`, `GX_ITM_0`, `GX_ITM_1`, `GX_ITM_2`, `GX_ITM_OFF`, `GX_ITM_S0`, `GX_ITM_S1`, `GX_ITM_S2`
- `GX_ITM_T0`, `GX_ITM_T1`, `GX_ITM_T2`, `GX_ITS_1`, `GX_ITS_128`, `GX_ITS_16`, `GX_ITS_2`, `GX_ITS_256`, `GX_ITS_32`, `GX_ITS_4`
- `GX_ITS_64`, `GX_ITS_8`, `GX_ITW_0`, `GX_ITW_128`, `GX_ITW_16`, `GX_ITW_256`, `GX_ITW_32`, `GX_ITW_64`, `GX_ITW_OFF`

#### K (5)

- `GX_KCOLOR0`, `GX_KCOLOR1`, `GX_KCOLOR2`, `GX_KCOLOR3`, `GX_KCOLOR_MAX`

#### L (34)

- `GX_LEQUAL`, `GX_LESS`, `GX_LIGHT0`, `GX_LIGHT1`, `GX_LIGHT2`, `GX_LIGHT3`, `GX_LIGHT4`, `GX_LIGHT5`, `GX_LIGHT6`, `GX_LIGHT7`
- `GX_LIGHTARRAY`, `GX_LIGHTNULL`, `GX_LIGHT_NULL`, `GX_LINEAR`, `GX_LINES`, `GX_LINESTRIP`, `GX_LIN_MIP_LIN`, `GX_LIN_MIP_NEAR`, `GX_LO_AND`, `GX_LO_CLEAR`
- `GX_LO_COPY`, `GX_LO_EQUIV`, `GX_LO_INV`, `GX_LO_INVAND`, `GX_LO_INVCOPY`, `GX_LO_INVOR`, `GX_LO_NAND`, `GX_LO_NOOP`, `GX_LO_NOR`, `GX_LO_OR`
- `GX_LO_REVAND`, `GX_LO_REVOR`, `GX_LO_SET`, `GX_LO_XOR`

#### M (40)

- `GX_MAXCOORD`, `GX_MAXLIGHT`, `GX_MAXTEXWRAPMODE`, `GX_MAXVTXFMT`, `GX_MAX_ALPHAOP`, `GX_MAX_ANISOTROPY`, `GX_MAX_BLENDMODE`, `GX_MAX_INDTEXSTAGE`, `GX_MAX_ITBALPHA`, `GX_MAX_ITBIAS`
- `GX_MAX_ITFORMAT`, `GX_MAX_ITSCALE`, `GX_MAX_ITWRAP`, `GX_MAX_KCOLOR`, `GX_MAX_LIGHT`, `GX_MAX_TEVBIAS`, `GX_MAX_TEVCLAMPMODE`, `GX_MAX_TEVREG`, `GX_MAX_TEVSCALE`, `GX_MAX_TEVSTAGE`
- `GX_MAX_TEVSWAP`, `GX_MAX_TEXCOORD`, `GX_MAX_TEXMAP`, `GX_MAX_TEXOFFSET`, `GX_MAX_TEXWRAPMODE`, `GX_MAX_TLUTFMT`, `GX_MAX_VTXATTRFMT`, `GX_MAX_VTXATTRFMT_LISTSIZE`, `GX_MAX_VTXDESC`, `GX_MAX_VTXDESC_LISTSIZE`
- `GX_MAX_VTXFMT`, `GX_MAX_Z24`, `GX_MAX_ZTEXOP`, `GX_MIRROR`, `GX_MODULATE`, `GX_MTX2x4`, `GX_MTX3x4`, `GX_MT_DL_SAVE_CTX`, `GX_MT_NULL`, `GX_MT_XF_FLUSH`

#### N (10)

- `GX_NEAR`, `GX_NEAR_MIP_LIN`, `GX_NEAR_MIP_NEAR`, `GX_NEQUAL`, `GX_NEVER`, `GX_NONE`, `GX_NRMMTXARRAY`, `GX_NRM_NBT`, `GX_NRM_NBT3`, `GX_NRM_XYZ`

#### O (1)

- `GX_ORTHOGRAPHIC`

#### P (83)

- `GX_PASSCLR`, `GX_PERF0_AVG_QUAD_CNT`, `GX_PERF0_CLIP_CLKS`, `GX_PERF0_CLIP_RATIO`, `GX_PERF0_CLIP_VTX`, `GX_PERF0_CLOCKS`, `GX_PERF0_NONE`, `GX_PERF0_QUAD_0CVG`, `GX_PERF0_QUAD_1CVG`, `GX_PERF0_QUAD_2CVG`
- `GX_PERF0_QUAD_3CVG`, `GX_PERF0_QUAD_4CVG`, `GX_PERF0_QUAD_NON0CVG`, `GX_PERF0_TRIANGLES`, `GX_PERF0_TRIANGLES_0CLR`, `GX_PERF0_TRIANGLES_0TEX`, `GX_PERF0_TRIANGLES_1CLR`, `GX_PERF0_TRIANGLES_1TEX`, `GX_PERF0_TRIANGLES_2CLR`, `GX_PERF0_TRIANGLES_2TEX`
- `GX_PERF0_TRIANGLES_3TEX`, `GX_PERF0_TRIANGLES_4TEX`, `GX_PERF0_TRIANGLES_5TEX`, `GX_PERF0_TRIANGLES_6TEX`, `GX_PERF0_TRIANGLES_7TEX`, `GX_PERF0_TRIANGLES_8TEX`, `GX_PERF0_TRIANGLES_CULLED`, `GX_PERF0_TRIANGLES_PASSED`, `GX_PERF0_TRIANGLES_SCISSORED`, `GX_PERF0_VERTICES`
- `GX_PERF0_XF_BOT_CLKS`, `GX_PERF0_XF_LIT_CLKS`, `GX_PERF0_XF_REGLD_CLKS`, `GX_PERF0_XF_REGRD_CLKS`, `GX_PERF0_XF_WAIT_IN`, `GX_PERF0_XF_WAIT_OUT`, `GX_PERF0_XF_XFRM_CLKS`, `GX_PERF1_CALL_REQ`, `GX_PERF1_CLOCKS`, `GX_PERF1_CP_ALL_REQ`
- `GX_PERF1_FIFO_REQ`, `GX_PERF1_NONE`, `GX_PERF1_TC_CHECK1_2`, `GX_PERF1_TC_CHECK3_4`, `GX_PERF1_TC_CHECK5_6`, `GX_PERF1_TC_CHECK7_8`, `GX_PERF1_TC_MISS`, `GX_PERF1_TEXELS`, `GX_PERF1_TX_IDLE`, `GX_PERF1_TX_MEMSTALL`
- `GX_PERF1_TX_REGS`, `GX_PERF1_VC_ALL_STALLS`, `GX_PERF1_VC_ELEMQ_FULL`, `GX_PERF1_VC_MEMREQ_FULL`, `GX_PERF1_VC_MISSQ_FULL`, `GX_PERF1_VC_MISSREP_FULL`, `GX_PERF1_VC_MISS_REQ`, `GX_PERF1_VC_STATUS7`, `GX_PERF1_VC_STREAMBUF_LOW`, `GX_PERF1_VERTICES`
- `GX_PERSPECTIVE`, `GX_PF_RGB565_Z16`, `GX_PF_RGB8_Z24`, `GX_PF_RGBA6_Z24`, `GX_PF_U8`, `GX_PF_V8`, `GX_PF_Y8`, `GX_PF_YUV420`, `GX_PF_Z24`, `GX_PNMTX0`
- `GX_PNMTX1`, `GX_PNMTX2`, `GX_PNMTX3`, `GX_PNMTX4`, `GX_PNMTX5`, `GX_PNMTX6`, `GX_PNMTX7`, `GX_PNMTX8`, `GX_PNMTX9`, `GX_POINTS`
- `GX_POSMTXARRAY`, `GX_POS_XY`, `GX_POS_XYZ`

#### Q (1)

- `GX_QUADS`

#### R (11)

- `GX_READ_00`, `GX_READ_FF`, `GX_READ_NONE`, `GX_REPEAT`, `GX_REPLACE`, `GX_RGB565`, `GX_RGB8`, `GX_RGBA4`, `GX_RGBA6`, `GX_RGBA8`
- `GX_RGBX8`

#### S (11)

- `GX_S16`, `GX_S8`, `GX_SP_COS`, `GX_SP_COS2`, `GX_SP_FLAT`, `GX_SP_OFF`, `GX_SP_RING1`, `GX_SP_RING2`, `GX_SP_SHARP`, `GX_SRC_REG`
- `GX_SRC_VTX`

#### T (221)

- `GX_TB_ADDHALF`, `GX_TB_SUBHALF`, `GX_TB_ZERO`, `GX_TC_EQ`, `GX_TC_GE`, `GX_TC_LE`, `GX_TC_LINEAR`, `GX_TEVPREV`, `GX_TEVREG0`, `GX_TEVREG1`
- `GX_TEVREG2`, `GX_TEVSTAGE0`, `GX_TEVSTAGE1`, `GX_TEVSTAGE10`, `GX_TEVSTAGE11`, `GX_TEVSTAGE12`, `GX_TEVSTAGE13`, `GX_TEVSTAGE14`, `GX_TEVSTAGE15`, `GX_TEVSTAGE2`
- `GX_TEVSTAGE3`, `GX_TEVSTAGE4`, `GX_TEVSTAGE5`, `GX_TEVSTAGE6`, `GX_TEVSTAGE7`, `GX_TEVSTAGE8`, `GX_TEVSTAGE9`, `GX_TEV_ADD`, `GX_TEV_COMP_BGR24_EQ`, `GX_TEV_COMP_BGR24_GT`
- `GX_TEV_COMP_GR16_EQ`, `GX_TEV_COMP_GR16_GT`, `GX_TEV_COMP_R8_EQ`, `GX_TEV_COMP_R8_GT`, `GX_TEV_COMP_RGB8_EQ`, `GX_TEV_COMP_RGB8_GT`, `GX_TEV_KASEL_1`, `GX_TEV_KASEL_1_2`, `GX_TEV_KASEL_1_4`, `GX_TEV_KASEL_1_8`
- `GX_TEV_KASEL_2_8`, `GX_TEV_KASEL_3_4`, `GX_TEV_KASEL_3_8`, `GX_TEV_KASEL_4_8`, `GX_TEV_KASEL_5_8`, `GX_TEV_KASEL_6_8`, `GX_TEV_KASEL_7_8`, `GX_TEV_KASEL_8_8`, `GX_TEV_KASEL_K0_A`, `GX_TEV_KASEL_K0_B`
- `GX_TEV_KASEL_K0_G`, `GX_TEV_KASEL_K0_R`, `GX_TEV_KASEL_K1_A`, `GX_TEV_KASEL_K1_B`, `GX_TEV_KASEL_K1_G`, `GX_TEV_KASEL_K1_R`, `GX_TEV_KASEL_K2_A`, `GX_TEV_KASEL_K2_B`, `GX_TEV_KASEL_K2_G`, `GX_TEV_KASEL_K2_R`
- `GX_TEV_KASEL_K3_A`, `GX_TEV_KASEL_K3_B`, `GX_TEV_KASEL_K3_G`, `GX_TEV_KASEL_K3_R`, `GX_TEV_KCSEL_1`, `GX_TEV_KCSEL_1_2`, `GX_TEV_KCSEL_1_4`, `GX_TEV_KCSEL_1_8`, `GX_TEV_KCSEL_2_8`, `GX_TEV_KCSEL_3_4`
- `GX_TEV_KCSEL_3_8`, `GX_TEV_KCSEL_4_8`, `GX_TEV_KCSEL_5_8`, `GX_TEV_KCSEL_6_8`, `GX_TEV_KCSEL_7_8`, `GX_TEV_KCSEL_8_8`, `GX_TEV_KCSEL_K0`, `GX_TEV_KCSEL_K0_A`, `GX_TEV_KCSEL_K0_B`, `GX_TEV_KCSEL_K0_G`
- `GX_TEV_KCSEL_K0_R`, `GX_TEV_KCSEL_K1`, `GX_TEV_KCSEL_K1_A`, `GX_TEV_KCSEL_K1_B`, `GX_TEV_KCSEL_K1_G`, `GX_TEV_KCSEL_K1_R`, `GX_TEV_KCSEL_K2`, `GX_TEV_KCSEL_K2_A`, `GX_TEV_KCSEL_K2_B`, `GX_TEV_KCSEL_K2_G`
- `GX_TEV_KCSEL_K2_R`, `GX_TEV_KCSEL_K3`, `GX_TEV_KCSEL_K3_A`, `GX_TEV_KCSEL_K3_B`, `GX_TEV_KCSEL_K3_G`, `GX_TEV_KCSEL_K3_R`, `GX_TEV_SUB`, `GX_TEV_SWAP0`, `GX_TEV_SWAP1`, `GX_TEV_SWAP2`
- `GX_TEV_SWAP3`, `GX_TEXCACHE_128K`, `GX_TEXCACHE_32K`, `GX_TEXCACHE_512K`, `GX_TEXCACHE_NONE`, `GX_TEXCOORD0`, `GX_TEXCOORD1`, `GX_TEXCOORD2`, `GX_TEXCOORD3`, `GX_TEXCOORD4`
- `GX_TEXCOORD5`, `GX_TEXCOORD6`, `GX_TEXCOORD7`, `GX_TEXCOORDNULL`, `GX_TEXCOORD_NULL`, `GX_TEXMAP0`, `GX_TEXMAP1`, `GX_TEXMAP2`, `GX_TEXMAP3`, `GX_TEXMAP4`
- `GX_TEXMAP5`, `GX_TEXMAP6`, `GX_TEXMAP7`, `GX_TEXMAPNULL`, `GX_TEXMAP_DISABLE`, `GX_TEXMAP_NULL`, `GX_TEXMTX0`, `GX_TEXMTX1`, `GX_TEXMTX2`, `GX_TEXMTX3`
- `GX_TEXMTX4`, `GX_TEXMTX5`, `GX_TEXMTX6`, `GX_TEXMTX7`, `GX_TEXMTX8`, `GX_TEXMTX9`, `GX_TEXMTXARRAY`, `GX_TEX_S`, `GX_TEX_ST`, `GX_TF_CI14`
- `GX_TF_CI4`, `GX_TF_CI8`, `GX_TF_CMPR`, `GX_TF_I4`, `GX_TF_I8`, `GX_TF_IA4`, `GX_TF_IA8`, `GX_TF_RGB565`, `GX_TF_RGB5A3`, `GX_TF_RGBA8`
- `GX_TG_BINRM`, `GX_TG_BUMP0`, `GX_TG_BUMP1`, `GX_TG_BUMP2`, `GX_TG_BUMP3`, `GX_TG_BUMP4`, `GX_TG_BUMP5`, `GX_TG_BUMP6`, `GX_TG_BUMP7`, `GX_TG_COLOR0`
- `GX_TG_COLOR1`, `GX_TG_MTX2x4`, `GX_TG_MTX3x4`, `GX_TG_NRM`, `GX_TG_POS`, `GX_TG_SRTG`, `GX_TG_TANGENT`, `GX_TG_TEX0`, `GX_TG_TEX1`, `GX_TG_TEX2`
- `GX_TG_TEX3`, `GX_TG_TEX4`, `GX_TG_TEX5`, `GX_TG_TEX6`, `GX_TG_TEX7`, `GX_TG_TEXCOORD0`, `GX_TG_TEXCOORD1`, `GX_TG_TEXCOORD2`, `GX_TG_TEXCOORD3`, `GX_TG_TEXCOORD4`
- `GX_TG_TEXCOORD5`, `GX_TG_TEXCOORD6`, `GX_TLUT0`, `GX_TLUT1`, `GX_TLUT10`, `GX_TLUT11`, `GX_TLUT12`, `GX_TLUT13`, `GX_TLUT14`, `GX_TLUT15`
- `GX_TLUT2`, `GX_TLUT3`, `GX_TLUT4`, `GX_TLUT5`, `GX_TLUT6`, `GX_TLUT7`, `GX_TLUT8`, `GX_TLUT9`, `GX_TLUT_128`, `GX_TLUT_16K`
- `GX_TLUT_1K`, `GX_TLUT_256`, `GX_TLUT_2K`, `GX_TLUT_32`, `GX_TLUT_4K`, `GX_TLUT_512`, `GX_TLUT_64`, `GX_TLUT_8K`, `GX_TL_IA8`, `GX_TL_RGB565`
- `GX_TL_RGB5A3`, `GX_TO_EIGHTH`, `GX_TO_FOURTH`, `GX_TO_HALF`, `GX_TO_ONE`, `GX_TO_SIXTEENTH`, `GX_TO_ZERO`, `GX_TRIANGLEFAN`, `GX_TRIANGLES`, `GX_TRIANGLESTRIP`
- `GX_TRUE`

#### U (2)

- `GX_U16`, `GX_U8`

#### V (47)

- `GX_VA_CLR0`, `GX_VA_CLR1`, `GX_VA_MAXATTR`, `GX_VA_MAX_ATTR`, `GX_VA_NBT`, `GX_VA_NRM`, `GX_VA_NULL`, `GX_VA_PNMTXIDX`, `GX_VA_POS`, `GX_VA_PTNMTXIDX`
- `GX_VA_TEX0`, `GX_VA_TEX0MTXIDX`, `GX_VA_TEX1`, `GX_VA_TEX1MTXIDX`, `GX_VA_TEX2`, `GX_VA_TEX2MTXIDX`, `GX_VA_TEX3`, `GX_VA_TEX3MTXIDX`, `GX_VA_TEX4`, `GX_VA_TEX4MTXIDX`
- `GX_VA_TEX5`, `GX_VA_TEX5MTXIDX`, `GX_VA_TEX6`, `GX_VA_TEX6MTXIDX`, `GX_VA_TEX7`, `GX_VA_TEX7MTXIDX`, `GX_VC_ALL`, `GX_VC_CLR0`, `GX_VC_CLR1`, `GX_VC_NRM`
- `GX_VC_POS`, `GX_VC_TEX0`, `GX_VC_TEX1`, `GX_VC_TEX2`, `GX_VC_TEX3`, `GX_VC_TEX4`, `GX_VC_TEX5`, `GX_VC_TEX6`, `GX_VC_TEX7`, `GX_VTXFMT0`
- `GX_VTXFMT1`, `GX_VTXFMT2`, `GX_VTXFMT3`, `GX_VTXFMT4`, `GX_VTXFMT5`, `GX_VTXFMT6`, `GX_VTXFMT7`

#### X (2)

- `GX_XF_FLUSH_NONE`, `GX_XF_FLUSH_SAFE`

#### Z (7)

- `GX_ZC_FAR`, `GX_ZC_LINEAR`, `GX_ZC_MID`, `GX_ZC_NEAR`, `GX_ZT_ADD`, `GX_ZT_DISABLE`, `GX_ZT_REPLACE`

## Limitations and build

- **Supported subset, not all of .NET.** In particular, no general LINQ, Dictionary/HashSet, StringBuilder, String.Format/interpolation, full formatting or globalization, reflection, arbitrary Math/Random family, multidimensional arrays, TLS/HTTPS, System.Text.Json, desktop threads or ValueTask.
- **Storage:** ordinary files require a mounted filesystem and are not power-loss atomic. GameCube memory-card entries are separate, journaled native saves. Media and free capacity matter.
- **Graphics:** only one frame and pass at once, native resources need Dispose, and raw GPU commands require manual state and pointer correctness.
- **Async:** no manual Scheduler.Pump call is needed for regular async/await; graphics and input stay on the application executor.
- **Audio:** AESND PCM/WAV; Music is preloaded WAV (not streamed MP3/Ogg).
- **Validation:** AOT and Dolphin emulator smoke tests do not prove correct physical memory-card writes, peripherals, or audio output on real hardware.
- **Authoritative details:** the source in src/ and the focused technical docs govern any edge cases, method overloads and platform limits.

### Build a GameCube sample

~~~sh
dotnet build samples/Snake/Snake.csproj -c Release
dotnet run --project tools/DolphinDotNet.Compiler -- --bundle samples/Snake/bin/Release/net8.0/Snake.dll out/Snake --platform gamecube
make -C out/Snake
~~~

The output is `out/Snake/Game.dol`; devkitPPC, libogc2 and .NET 8 are required. Standalone bundles include the verified GameCube EXI correction.
