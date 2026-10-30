#!/usr/bin/env python3
"""Smoke test desktop RARC pack/inspect/extract using original synthetic bytes."""
import pathlib
import subprocess
import tempfile
import struct

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
    # Original synthetic GLY1 sheet: I4 8x8 pixels, fully opaque white.
    bfn = bytearray(0x80)
    bfn[:8] = b"FONTbfn1"
    struct.pack_into(">II", bfn, 8, 0x80, 2)
    bfn[0x20:0x24] = b"INF1"
    struct.pack_into(">I", bfn, 0x24, 0x20)
    bfn[0x40:0x44] = b"GLY1"
    struct.pack_into(">I", bfn, 0x44, 0x40)
    struct.pack_into(">HHHHI", bfn, 0x48, 65, 65, 8, 8, 32)
    struct.pack_into(">HHHH", bfn, 0x56, 1, 1, 8, 8)
    bfn[0x60:0x80] = b"\\xff" * 32
    font_file = base / "sample.bfn"
    font_file.write_bytes(bfn)
    assert "1 glyph sheets" in command("inspect", font_file)
    font_dir = base / "fonts"
    command("bfn", font_file, font_dir)
    png = (font_dir / "sheet-000.png").read_bytes()
    assert png[:8] == b"\\x89PNG\\r\\n\\x1a\\n"
    assert struct.unpack_from(">II", png, 16) == (8, 8)

print("GCFT desktop archive roundtrip passed")
