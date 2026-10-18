# Fox

A minimal, original Star Fox 64-inspired rail shooter for GameCube. Fly a
polygon ship down a scrolling corridor, destroy incoming fighters, dodge aimed
fire, collect gold shield rings, and defeat the moving carrier at the end of the
route. The mission takes about a minute at cruise speed, followed by the boss.
Boosting reaches the carrier sooner.

The sample builds its ship, carrier, scenery and rings from colored triangles.
It combines a perspective `Renderer`/`Mesh` pass with a `Draw2D` HUD, uses
synthesized PCM8 sounds, and needs no textures, downloaded assets, disc, SD card,
or networking. No Nintendo models, music or game data are included.

| Controller 1 | Action |
| --- | --- |
| Stick or D-pad | Fly left/right and up/down |
| A | Launch / hold to fire twin lasers / retry |
| X during flight | Boost while energy remains |
| Y during flight | Brake |
| L / R click | Barrel roll left/right; temporary invulnerability with a cooldown |
| Start | Pause / resume |
| B | Return to title |
| Z | Toggle debug statistics |
| X on title | Finish sample; Start then exits its result screen |

Five shield points protect the ship. Damage grants a brief grace period; gold
rings repair one shield point and refill boost energy. Boost recharges when
released; after exhaustion, release X before boosting again. Missed fighters
can fly past, but direct collisions damage the ship. The carrier core requires
20 laser hits. Best score lasts for the application session.

Unplugging controller 1 freezes flight and timers. Reconnect to continue without
an accumulated time jump. Pause also freezes boost, roll and invulnerability
clocks.

Open [Fox.sln](Fox.sln) in Rider or Visual Studio. Choose **Fox in Dolphin (play
without debugger)** for normal play, or **Fox in Dolphin (wait for Rider)** to
attach the DolphinDotNet debugger. Configure Dolphin's controller mappings first.
See [IDE setup](../../docs/ide-workflow.md).

```sh
dotnet build samples/Fox/Fox.sln -c Debug
dotnet run --project samples/Fox -c Release
```

Breakpoints in `Program.cs` and `FoxGame.Update` use the sample's own generated
Debug source/address maps. `FoxGame` keeps the flight/combat rules independent of
graphics and hardware.

```sh
dotnet run --project tests/FoxRulesSmoke -c Release
```

CI runs those rules on .NET, under native AOT with sanitizers/forced GC, and as a
PowerPC DOL in headless Dolphin. The controller acceptance check launches the
actual game and verifies launch/fire, horizontal and vertical flight, boost,
brake, roll, pause/resume, returning to title and restarting through real pipe
inputs. GDB observes state without patching guest input or game data.

## API updates

The player uses an original embedded glTF-derived model and looping Hover
animation through `ModelInstance.Animator`. Procedural geometry remains a fallback
and is retained for tinted enemy variants. Regenerate with
`python3 scripts/generate_sample_model.py`; CI checks the generated source.

On the title screen hold Start and press Y to capture a new fire binding (A, X or
Y; B cancels). Release the opening buttons before choosing. Context changes suppress
held actions, preventing the title's start press from firing. Start+X exits from
the title even when X is rebound. Hits/damage produce rumble. Best scores merge
and save automatically when a native memory card is present in either slot
(`DFOX`), with two-second hotplug checks.
