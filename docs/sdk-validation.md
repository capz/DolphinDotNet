# SDK distribution acceptance record

Validated in the fresh DolphinDotNet cloud coding environment on 2026-10-22.
The existing checkout was `/workspace/DolphinDotNet`, clean at
`484310dc13611dedc43434de17a3d48c51a309a8`; `git ls-remote` confirmed current remote
main matched before implementation. Work uses `feature/nuget-dotnet-sdk`.
Saved setup/startup instructions were read before implementation. Restored .NET
8.0.426, Docker daemon, pinned libogc2 image and headless Dolphin image were
checked. Tests requiring .NET named mutexes, network or Docker ran outside the
restricted command sandbox through the supported escalation mechanism.

## Executed successfully

- Local NuGet packing at default and overridden preview versions. SDK, Core,
  GameCube, Templates and optional Assets/SaveImage tool archives pass ZIP
  integrity checks and contain repository license files. Nothing was uploaded.
- A relocated local feed, empty NuGet/CLI caches, and external paths containing
  spaces. All three templates installed and built; the library's ordinary Build
  produced no native output, and design-time BuildDol did no native work.
- Real application → First package → Second package dependency resolution,
  Debug bundle generation without dependency PDBs, and host execution of the
  generated C returning 42. Missing transitive implementation and unsupported
  Console.Beep calls produced actionable diagnostics.
- External Debug Pong built using NuGet SDK/API dependencies, with nonempty
  DOL, ELF and matching source/address metadata.
- A 65-second external Pong Dolphin smoke passed with emulator-observed PCs at
  main, dnd_aot_entry, dnd_audio_create_pcm8 and dnd_device_end; the harness
  reported no DOL-specific loader or PPC errors.
- The SDK's packaged DAP adapter attached to that external Pong in real Dolphin,
  bound and hit Program.cs lines 24 and 25, performed source step and pause, and
  passed the existing smoke harness. This establishes adapter/metadata operation,
  not desktop IDE interaction.
- Existing compiler integration runner, including host execution, generic AOT,
  missing/stale entry PDB checks, and bundle/debug metadata contracts.
- Existing GDB/RSP debugger protocol runner; 19 CI-selection and 16 Dolphin
  smoke Python unit tests.
- Existing repository Pong Release build through source SDK imports and pinned
  Docker backend. Existing nullable/C warnings remain.
- Preview workflow YAML structure, Python syntax and git whitespace checks.

The final external consumer run also passed optional tool installation/help,
packaged effects generation, duplicate-type diagnostics and launch settings
(including a separate GDB-enabled Dolphin profile). It used all six packages
at 0.1.0-preview.4. The real Dolphin/DAP checks used the equivalent SDK/compiler
code from 0.1.0-preview.3; subsequent packaging changes added licenses, optional
tools and clean staging without changing native compilation or adapter code.

## Not verified here

- Interactive Visual Studio on Windows and Rider on supported desktop hosts;
  extension installation, toolbar launches and IDE breakpoint interaction.
- Windows/macOS execution of the packaged SDK, ARM Docker emulation, and native
  devkitPPC/libogc2 builds. Docker uses the pinned Linux amd64 toolchain.
- Physical GameCube hardware, BBA/USB Gecko targets and other hardware behavior.
- Full preexisting sample/test/CI matrices and hosted execution of the new
  GitHub Actions workflow. Local representative tests do not establish full CI.
- Actual nuget.org publication, package ownership, scoped release credentials,
  protected-environment approval configuration and NuGet ingestion. Publishing
  was intentionally not performed for testing.
- Future environment snapshot restoration. This fresh restored coding workspace
  and retained daemon images were checked; subsequent snapshots are separate.

No UI changes, main-branch edits or package publication are part of this change.
