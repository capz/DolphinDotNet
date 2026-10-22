# Debugger repository boundary

The Rider plugin, Visual Studio VSIX, shared .NET DAP adapter, GDB/RSP client,
acceptance probe, protocol tests and IDE packaging workflows are maintained in
[DolphinDotNet.Debuggers](https://github.com/capz/DolphinDotNet.Debuggers).
Install its ZIP/VSIX from that repository's releases or successful Actions runs.
The main SDK repository no longer builds or publishes IDE extensions.

This repository owns compiler-produced debug/source/address metadata, native
GDB/heartbeat support and emulator/native acceptance fixtures. Their schemas,
identity hashes and PowerPC ABI form the interface consumed by the debugger.

`eng/debugger-source.json` pins a full reviewed debugger commit. Run
`python3 eng/prepare_debugger.py` to fetch it into the ignored `out/debuggers`
dependency directory. The script verifies origin and revision, reuses the pinned
checkout, and refuses to overwrite local changes. Updating the pin is an explicit
SDK change; it does not follow the debugger repository's moving main branch.
Git/network access is needed the first time a revision is fetched.

`python3 eng/pack.py` prepares/restores the dependency and includes its adapter,
RSP library, dependencies and DAP acceptance script in DolphinDotNet.Sdk.
For a direct `dotnet pack sdk/DolphinDotNet.Sdk`, prepare the dependency first.
No game consumer needs Git or another source checkout to load the packaged
adapter. IDE extensions bundle their own adapter builds.

Run `python3 eng/check_debugger.py` to test compiler/native contracts against
that revision. The check invokes the debugger suite with `DND_RUNTIME_ROOT`
pointing at this checkout; standalone debugger CI runs protocol tests separately.
For manual emulator source checks, build/publish the pinned adapter or probe
under `out/debuggers/tools/`. `scripts/dolphin_smoke.py --dap-adapter ...` loads
`dnd_dap_probe.py` beside the provided adapter (including SDK package outputs).
Native/Dolphin checks remain local/manual under the current Windows/macOS-only
hosted CI policy.

Migration order: merge the standalone debugger build PR first, then the SDK
removal PR. The SDK pin names the exact validated debugger commit, independent
of subsequent source changes or squash-merge commit IDs. Original source history
remains in this repository and the filtered debugger history preserves authors.

## Migration validation

Local validation on 2026-10-22 passed: standalone RSP/protocol tests, Visual Studio
attach/settings tests, 68 adapter tests (66 standalone plus two explicit runtime
boundary checks), SDK packaging with the fetched debugger revision, a clean
external consumer/native Pong build, and real Dolphin DAP source breakpoints at
two C# lines, source stepping and pause using the packaged probe. CI selection,
workflow gate, smoke-harness, sample-release and source-fetch safety tests passed.
No packages or extension releases were published as tests.

Windows/macOS hosted packaging, JetBrains Plugin Verifier, interactive IDE
sessions and hardware are separate checks. At migration preparation time the
new repository had no `main` branch, and initializing one required explicit
approval under the no-direct-main rule. The standalone feature branch contains
the complete extracted history and source. Do not merge this SDK removal until
the standalone repository's migration/build setup is reviewed and ready.
