#!/usr/bin/env python3
"""Build the vendored jitterentropy core with its required compiler settings.

No network access is needed. The application and adapter may use optimization
and LTO; these timing-sensitive upstream translation units must not use either.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shlex
import subprocess
import tempfile


REVISION = "e783cf1c450bce4d72f95c9f9c84546a6094976a"  # jitterentropy v3.7.0
SOURCES = (
    "jitterentropy-base.c",
    "jitterentropy-gcd.c",
    "jitterentropy-health.c",
    "jitterentropy-noise.c",
    "jitterentropy-sha3.c",
    "jitterentropy-status.c",
    "jitterentropy-timer.c",
)


def compiler_flags(cflags, source):
    # The last -O switch wins in GCC, but removing inherited switches makes the
    # security requirement inspectable and also removes GCC LTO sub-options.
    flags = [flag for flag in shlex.split(cflags)
             if not flag.startswith(("-O", "-flto", "-fno-lto"))
             and flag not in ("-fwhole-program", "-ffat-lto-objects",
                              "-fno-fat-lto-objects", "-fuse-linker-plugin")]
    return flags + ["-std=c11", "-O0", "-fno-lto", "-fwrapv",
                    "-I" + str(source), "-I" + str(source / "src"),
                    "-I" + str(source.parent)]


def build(output, source, cc, ar, cflags):
    output = Path(output).resolve()
    source = Path(source).resolve()
    output.mkdir(parents=True, exist_ok=True)
    inputs = [source / "src" / name for name in SOURCES]
    inputs += sorted(source.rglob("*.h"))
    inputs += [source / "LICENSE", source / "UPSTREAM.md",
               source.parent / "dnd_jitterentropy_port.h", Path(__file__).resolve()]
    # Include the content of the port and the pinned tree in the cache key:
    # edits to a health test, compiler flags or adapter must rebuild the core.
    hashes = [(str(path.relative_to(source.parent)) if path.is_relative_to(source.parent)
               else path.name, hashlib.sha256(path.read_bytes()).hexdigest())
              for path in inputs]
    flags = compiler_flags(cflags, source)
    identity = json.dumps({
        "revision": REVISION, "cc": cc, "ar": ar, "flags": flags,
        "inputs": hashes,
        "compiler": subprocess.check_output(shlex.split(cc) + ["--version"])
                              .decode(errors="replace"),
    }, sort_keys=True)
    library = output / "libdndentropy.a"
    stamp = output / "configuration.json"
    if library.is_file() and stamp.is_file() and stamp.read_text() == identity:
        return
    with tempfile.TemporaryDirectory(prefix="entropy-", dir=output) as folder:
        temporary = Path(folder)
        objects = []
        for name in SOURCES:
            obj = temporary / (Path(name).stem + ".o")
            subprocess.run(shlex.split(cc) + flags +
                           ["-c", str(source / "src" / name), "-o", str(obj)], check=True)
            objects.append(str(obj))
        archive = temporary / library.name
        subprocess.run(shlex.split(ar) + ["rcs", str(archive), *objects], check=True)
        archive.replace(library)
    stamp.write_text(identity)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    parser.add_argument("--source", default="source/jitterentropy")
    parser.add_argument("--cc", default="cc")
    parser.add_argument("--ar", default="ar")
    parser.add_argument("--cflags", default="")
    args = parser.parse_args()
    build(args.output, args.source, args.cc, args.ar, args.cflags)
