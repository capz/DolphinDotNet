#!/usr/bin/env python3
"""Measure warmed host CLR allocations for SDK transforms; not a PPC heap probe."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--revision", help="Git source revision; omit for the working tree")
args = parser.parse_args()
sources = [
    "src/DolphinDotNet.GameCube/Dolphin/Graphics/Transform.cs",
    "src/DolphinDotNet.GameCube/Dolphin/Graphics/Vector3.cs",
    "src/DolphinDotNet.GameCube/Dolphin/Graphics/Quaternion.cs",
    "src/DolphinDotNet.Core/Dolphin/Collections/List.cs",
    "src/DolphinDotNet.Core/Dolphin/Collections/ReadOnlyCollection.cs",
]
with tempfile.TemporaryDirectory(prefix="dnd-transform-memory-") as temporary:
    work = Path(temporary)
    for source in sources:
        content = subprocess.check_output(["git", "show", args.revision + ":" + source], cwd=ROOT) if args.revision else (ROOT / source).read_bytes()
        (work / Path(source).name).write_bytes(content)
    for name in ("Program.cs", "NativeModel.cs"):
        shutil.copyfile(ROOT / "tests/TransformMemoryTests" / name, work / name)
    (work / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>')
    print("Source:", args.revision or "working tree", flush=True)
    subprocess.run(["dotnet", "run", "--project", str(work / "Probe.csproj"), "-c", "Release", "--", "--measure-only"], check=True)
