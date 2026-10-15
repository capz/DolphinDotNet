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
