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

From repository root, with .NET 8, devkitPPC and libogc2:

```sh
dotnet build samples/Pong/Pong.csproj -c Release
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
