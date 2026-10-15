# Snake — memory-card save/load sample

A playable C# GameCube Snake demonstrating native `Dolphin.Storage.MemoryCard`
saves, `FrameClock`, `Controller`, `GraphicsDevice` and `Draw2D`. All
graphics, including the seven-row bitmap-like lettering, are created with
rectangles. No textures, SD card or disc assets are required.

## Gameplay

Use **D-pad** or **left analog stick** to steer, eat red fruit, grow and score.
Touching a wall or your body ends the run. Snake accelerates as you eat fruit.

The title screen offers **Continue** (only when a valid save exists) and
**New Game**. Use up/down to select and **A** to confirm; **X** exits from the
title screen. **Start** pauses/resumes during gameplay. **B** saves the current
run and returns to the title screen. On game over, **A** restarts and **B**
returns to the title. The game freezes if the controller disconnects.

## Persistent storage

The game searches for a native GameCube memory card in **slot A**, then
**slot B**. It uses game code `DSNK`, company code `01`, and save name
`SnakeRun`. Dolphin can emulate the GameCube memory card; configure a card
in Dolphin's GameCube slot A or B. For hardware, insert a writable GameCube
memory card in one of the slots.

The sample saves immediately on New Game, every **120 seconds of connected,
active gameplay** (pauses and menus do not count), and when returning to the
title screen with B. Failed periodic saves retry after 15 active seconds.
A game over clears its resumable save. If no memory card is available, play
still works, but saves and Continue are disabled; the status line explains why.

`SnakeSaveStore` uses `MemoryCard.Mount`, `GetEntries`, `ReadAllBytes`,
`WriteSave`, `Delete`, and `Dispose`. `WriteSave` provides the card's
transactional replacement mechanism. The game format is compact, versioned,
and checksummed; `SnakeSaveCodec` validates the complete body, direction,
score and food before accepting Continue.

## Build

With .NET 8, devkitPPC and libogc2 installed, from the repository root:

```sh
dotnet build samples/Snake/Snake.csproj -c Release
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle samples/Snake/bin/Release/net8.0/Snake.dll out/Snake --platform gamecube
make -C out/Snake
```

Load `out/Snake/Game.dol` in Dolphin or on a GameCube. The standalone bundle
includes the project's verified libogc2 EXI override.

Host-side logic and serialization tests:

```sh
dotnet run --project tests/SnakeRulesSmoke/SnakeRulesSmoke.csproj -c Release
```
