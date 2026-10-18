# Two-player Pong

A self-contained C# GameCube game demonstrating `GraphicsDevice`, `Draw2D`,
`Controller`, `FrameClock` and `Dolphin.Audio.Sound.FromPcm8`.

The game uses a 640×480 logical playfield, colored rectangle paddles, a square
ball, seven-segment scores, paddle deflections with increasing ball speed,
wall bounces, and a first-to-seven match. Audio is generated procedurally on
startup: separate triangle-wave chirps for paddle hits, wall hits and goals.
No textures, bitmap fonts, SD card, disc, network initialization or external sound files are needed.

## Controls

| Action | Player 1 (port 0) | Player 2 (port 1) |
| --- | --- | --- |
| Move paddle | Analog stick or D-pad up/down | Analog stick or D-pad up/down |
| Serve / start new match after win | A | A |
| Pause / resume | Start | Start |
| Restart match | B | B |
| Exit sample | X | — |

Both controllers must be connected to play. A disconnected controller
automatically freezes the game. After a point, press **A** to serve again.
Press **A** after a player reaches seven to reset and serve a new match.

## Compile and run

Open **[Pong.sln](Pong.sln)** in Visual Studio or Rider. Select the **Dolphin**
launch profile in Visual Studio and use **Ctrl+F5**, or **Pong in Dolphin → Run**
in Rider. Build produces `bin/Debug/net8.0/Pong.dol` and `.elf`, and Run launches
Dolphin. See the [IDE setup guide](../../docs/ide-workflow.md) for the one-time
toolchain/Dolphin setup, local settings and controller configuration.

```sh
dotnet build samples/Pong/Pong.sln
dotnet run --project samples/Pong
```

Every build regenerates and fully rebuilds the native output. No native incremental
cache is used. IntelliSense/design-time builds skip the native pipeline.

### Manual bundle generation

From repository root, with .NET 8, devkitPPC and libogc2:

```sh
dotnet build samples/Pong/Pong.csproj -c Release -p:DndBuildNative=false
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle samples/Pong/bin/Release/net8.0/Pong.dll out/Pong --platform gamecube
make -C out/Pong
```

Load `out/Pong/Game.dol` in Dolphin or on hardware. Configure two
GameCube controllers on ports 1 and 2 (0 and 1 in C#). The DOL contains all
game and sound synthesis logic.

For isolated host-side rules checks:
```sh
dotnet run --project tests/PongRulesSmoke/PongRulesSmoke.csproj -c Release
```

## API updates

Both players use routed input with transition suppression. Paddle hits and
match wins pulse the appropriate controller's rumble. Consecutive match-win
records are persisted per player under `DPNG` as LeftStreak and RightStreak.
Card insertion/removal is checked every two seconds and pending improvements
save after insertion. The HUD uses the shared bitmap font.
