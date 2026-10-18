# Green Men

An original, self-contained Space Invaders-style game for GameCube, written in
C#. Defend the ground against a marching green formation, shoot through
breakable bunkers, and survive increasingly fast waves. The last invaders move
faster; only the lowest invader in each column fires. You get three lives and a
brief invulnerability period after a hit. Invaders reaching the ground end the
run. Your best score lasts until the application exits.

All pixel art, the small HUD font, and three PCM8 sound effects are generated or
embedded in code. No SD card, disc, downloaded assets or network setup is needed.
`GreenMenGame` contains deterministic platform-independent rules; the renderer
uses batched `Draw2D` rectangles in a logical 640 × 480 playfield.

| Controller 1 | Action |
| --- | --- |
| Left/right stick or D-pad | Move |
| A | Start / hold to fire / retry after game over |
| Start | Pause/resume, including between waves |
| B | Return to title |
| Z | Toggle debug statistics |
| X | Finish the sample; Start then exits its result screen |

Unplugging controller 1 freezes the game. Reconnect to continue; no elapsed time
is accumulated while disconnected or paused.

Open [GreenMen.sln](GreenMen.sln) in Rider or Visual Studio. In Rider choose
**GreenMen in Dolphin (play without debugger)** for normal play or
**GreenMen in Dolphin (wait for Rider)** to attach the DolphinDotNet debugger.
Configure Dolphin's controller 1 first; keyboard controls use its pad mappings.
See [IDE setup](../../docs/ide-workflow.md).

```sh
dotnet build samples/GreenMen/GreenMen.sln -c Debug
dotnet run --project samples/GreenMen -c Release
```

The build creates `GreenMen.dol`, `.elf`, and matching Debug maps in the sample's
own output directory. Set breakpoints in `Program.cs` or `GreenMenGame.Update`.

Run the deterministic gameplay tests without the GameCube toolchain:

```sh
dotnet run --project tests/GreenMenRulesSmoke -c Release
```

CI also runs these rules through AOT under memory sanitizers and forced GC,
builds the actual PowerPC DOL, and drives the game using Dolphin pipe inputs to
check start/fire, movement, pause/resume, returning to title and restarting.

## API updates

On the title screen hold Start and press Y to remap fire to A, X or Y;
B cancels capture. Release the opening buttons before choosing. The input router
suppresses held actions across title, play and pause transitions. Start+X remains
an exit chord if X is rebound. Hits/damage pulse rumble. High scores are kept
while no card is present and merged/saved when either slot receives a card
(`DGRN` identity, two-second hotplug polling).
