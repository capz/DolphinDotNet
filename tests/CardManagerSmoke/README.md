# Native all-game card acceptance

This fixture writes/deletes original synthetic saves. Run **only** with two fresh
isolated emulator card files; never run it on a physical user's cards.

Build against this SDK source:

```sh
dotnet build tests/CardManagerSmoke/CardManagerSmoke.csproj -c Release \
  -p:DndBuildNative=true -p:DndBuildBackend=Docker
```

Run `verify_native.py --dol <built.dol> --logs <fresh-directory>` in cloud-local
X11 with Dolphin NoGUI. The verifier forces raw emulated cards in both slots and
reads the native result through Dolphin's debugger. Twenty-four presented frames
allow libogc's 300 ms EXI attachment debounce before mounting.

`native-review/result.json` records the passing DOL: both mounts ready, 14 bounded
transfer steps, a non-repeating game-scoped payload roundtrip, unfiltered listing,
verified copy, deliberate stale-snapshot rejection, move, icon read and delete.
The cached native exception text is the deliberately caught stale-save exception,
not an unhandled failure. Both isolated cards end empty. This does not establish
physical console removal/power-loss behavior.

Earlier diagnostics caught empty-card `CARD_ERROR_NOFILE`, mutating libogc file
handles/timestamps and fixture naming/physical-container assumptions. Host fault
tests now model those real boundary behaviors. Save length exposed by the manager
is physical card length, including any game-owned headers and block padding.
