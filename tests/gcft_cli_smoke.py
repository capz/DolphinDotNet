#!/usr/bin/env python3
"""Smoke test desktop RARC pack/inspect/extract using original synthetic bytes."""
import pathlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
def command(*args):
    return subprocess.run(
        ["dotnet", "run", "--project", "tools/DolphinDotNet.Assets",
         "-c", "Release", "--", "gcft", *map(str, args)],
        cwd=ROOT, check=True, capture_output=True, text=True,
    ).stdout

with tempfile.TemporaryDirectory() as tmp:
    base = pathlib.Path(tmp)
    source = base / "source"
    (source / "sub").mkdir(parents=True)
    (source / "hi.txt").write_bytes(b"hello gamecube")
    (source / "sub" / "bytes.bin").write_bytes(bytes(range(256)))
    archive = base / "output.arc"
    command("pack", source, archive)
    listing = command("inspect", archive)
    assert "hi.txt" in listing and "sub/bytes.bin" in listing, listing
    target = base / "target"
    command("extract", archive, target)
    assert (target / "hi.txt").read_bytes() == b"hello gamecube"
    assert (target / "sub" / "bytes.bin").read_bytes() == bytes(range(256))
    archive2 = base / "output2.arc"
    command("pack", target, archive2)
    assert archive.read_bytes() == archive2.read_bytes(), "nondeterministic RARC packing"
print("GCFT desktop archive roundtrip passed")
