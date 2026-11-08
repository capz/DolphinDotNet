#!/usr/bin/env python3
"""Image-only P6 acceptance checks; never opens a physical device."""
import argparse
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
CORE = ["source/raw_move.c", "source/raw_move_fs.c", "source/raw_move_codec.c",
        "source/defrag_allocation.c", "source/verified_copy.c"]


def run(*args, env=None):
    print("+", *map(str, args), flush=True)
    subprocess.run(list(map(str, args)), cwd=ROOT, check=True, env=env)


def compile_test(directory, name, extra=(), cc="cc"):
    output = directory / (name + "-" + str(len(list(directory.iterdir()))))
    sources = CORE if name != "raw_move_codec" else CORE[2:3] + CORE[4:]
    run(cc, "-std=c11", "-O3", "-Wall", "-Wextra", "-Werror", "-Iinclude",
        *extra, "tests/" + name + "_test.c", *sources, "-o", output)
    return output


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("suite", choices=("quick", "faults", "large", "sanitize", "independent", "powerpc"))
    p.add_argument("--report", type=Path)
    args = p.parse_args()
    with tempfile.TemporaryDirectory(prefix="dnd-p6-ci-") as temporary:
        directory = Path(temporary)
        if args.suite in ("quick", "faults", "sanitize", "powerpc"):
            for name in ("raw_move_codec", "raw_move", "raw_move_safety"):
                extra, cc, prefix, suffix = (), "cc", (), ()
                if args.suite == "faults" and name != "raw_move_codec":suffix = ("--exhaustive",)
                if args.suite == "sanitize":extra = ("-O1", "-g", "-fsanitize=address,undefined", "-fno-omit-frame-pointer")
                if args.suite == "powerpc":
                    cc, extra, prefix = "powerpc-linux-gnu-gcc", ("-static", "-DDND_REQUIRE_PPC32"), ("qemu-ppc",)
                binary = compile_test(directory, name, extra, cc)
                env = dict(os.environ, ASAN_OPTIONS="detect_leaks=0") if args.suite == "sanitize" else None
                run(*prefix, binary, *suffix, env=env)
        elif args.suite == "large":
            for spc in (64, 128):
                run(compile_test(directory, "raw_move_large", ("-DRM_LARGE_SPC=" + str(spc),)))
        else:
            run(sys.executable, "tests/raw_move_images.py")
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        tools = {}
        for tool in ("cc", "mkfs.fat", "fsck.fat", "mkfs.exfat", "fsck.exfat", "powerpc-linux-gnu-gcc", "qemu-ppc"):
            if shutil.which(tool):
                version = subprocess.run([tool, "--version"], capture_output=True, text=True)
                tools[tool] = (version.stdout + version.stderr).splitlines()[:3]
        args.report.write_text(json.dumps({"suite": args.suite, "status": "passed", "platform": platform.platform(),
            "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
            "physicalDeviceWrites": False, "tools": tools}, indent=2) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
