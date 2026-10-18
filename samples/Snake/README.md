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
title screen with B.
A game over clears its resumable save. Both slots are polled every **two seconds**
with `MemoryCards.Update`/`GetState`. Removing the mounted card releases it and
clears Continue availability; a card in the other slot can take over. Inserting
a card during play or pause saves the current run at the next poll. At the
title screen, an inserted card makes its existing save available for Continue
without automatically loading or overwriting it.

If no card is available, play still works. Requested saves remain queued and
begin once a writable card is present, including after B returns to the title.
Pending writes and failed mounts retry on the two-second poll; game over or
New Game cancels the previous pending run. A busy/unknown probe preserves the
last confirmed connection state. The status line reports failures; physical
presence does not guarantee a formatted, writable card.

`SnakeSaveStore` uses `MemoryCard.Mount`, `GetEntries`, `ReadAllBytes`,
`WriteSave`, `Delete`, and `Dispose`. `WriteSave` provides the card's
transactional replacement mechanism. The game format is compact, versioned,
and checksummed; `SnakeSaveCodec` validates the complete body, direction,
score and food before accepting Continue.

## Build

Open [Snake.sln](Snake.sln) in Visual Studio or Rider and use the Dolphin
launch profile, or build and run from the repository root:

```sh
dotnet run --project samples/Snake -c Release
```

See [IDE setup](../../docs/ide-workflow.md) for prerequisites and local settings.

The build produces `samples/Snake/bin/Release/net8.0/Snake.dol`, also suitable
for loading on a GameCube, with the project's verified libogc2 EXI override.

Host-side logic and serialization tests:

```sh
dotnet run --project tests/SnakeRulesSmoke/SnakeRulesSmoke.csproj -c Release
```

## API updates

Input now uses the shared router, suppressing held actions across screen
transitions. Finishing a run pulses rumble. The HUD uses the common bitmap font;
the existing serialized save format and deterministic RNG are retained.
