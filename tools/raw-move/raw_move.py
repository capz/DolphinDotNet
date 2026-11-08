#!/usr/bin/env python3
"""Experimental OFFLINE IMAGE mover. Original input is never opened writable."""
import argparse
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ["tools/raw-move/main.c", "tools/raw-move/host.c", "source/raw_move.c",
           "source/raw_move_fs.c", "source/raw_move_codec.c", "source/defrag_allocation.c",
           "source/verified_copy.c"]


def build(output):
    subprocess.run([os.environ.get("CC", "cc"), "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror",
                    "-I" + str(ROOT / "include"), *[str(ROOT / p) for p in SOURCES], "-o", str(output)], check=True)


def clone(source, target):
    # O_EXCL prevents replacement of any existing output, including symlinks.
    source_fd = os.open(source, os.O_RDONLY | os.O_NOFOLLOW)
    try:
        before = os.fstat(source_fd)
        if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size <= 0 or before.st_size % 512:
            raise ValueError("Source must be a regular, single-link, sector-aligned offline image.")
        target_fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(target_fd, "wb", buffering=0) as output:
            with os.fdopen(os.dup(source_fd), "rb", buffering=0) as input_file:
                shutil.copyfileobj(input_file, output, 32768)
            os.fsync(output.fileno())
        after = os.fstat(source_fd)
        if (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns) != (
                after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns):
            raise ValueError("Backup image changed during cloning; keep the incomplete output offline.")
        parent = os.open(Path(target).parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(parent)
        finally:
            os.close(parent)
    finally:
        os.close(source_fd)


def parser():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--binary", type=Path, help="Use an explicitly selected prebuilt image-only test driver.")
    sub = p.add_subparsers(dest="command", required=True)
    for name in ("plan", "move"):
        item = sub.add_parser(name)
        item.add_argument("image", type=Path)
        item.add_argument("--partition", type=int, default=0)
        item.add_argument("--path", required=True)
        item.add_argument("--first", type=int, required=True)
        item.add_argument("--count", type=int, required=True)
        item.add_argument("--destination", type=int, required=True)
        if name == "move":
            item.add_argument("--output", type=Path, required=True)
            item.add_argument("--journal-target", type=Path, required=True)
            item.add_argument("--journal-scratch", type=Path, required=True)
            item.add_argument("--fail-after", type=int, help=argparse.SUPPRESS)
    for name in ("recover", "inspect"):
        item = sub.add_parser(name)
        item.add_argument("image", type=Path)
        item.add_argument("--backup", type=Path, required=True)
        item.add_argument("--journal-target", type=Path, required=True)
        item.add_argument("--journal-scratch", type=Path, required=True)
    return p


def run(args, binary):
    if args.command in ("plan", "move"):
        if min(args.partition, args.first, args.destination) < 0 or args.count <= 0:
            raise ValueError("Partition, cluster index and destination must be nonnegative; count must be positive.")
        options = [str(args.partition), args.path, str(args.first), str(args.count), str(args.destination)]
        if args.command == "plan":
            command = [binary, "plan", str(args.image.absolute()), *options]
        else:
            clone(args.image, args.output)
            command = [binary, "execute", str(args.output.absolute()), str(args.image.absolute()),
                       str(args.journal_target.absolute()), str(args.journal_scratch.absolute()), *options]
            if args.fail_after is not None:
                command += ["--fail-after", str(args.fail_after)]
    else:
        command = [binary, args.command, str(args.image.absolute()), str(args.backup.absolute()),
                   str(args.journal_target.absolute()), str(args.journal_scratch.absolute())]
    return subprocess.run(command, check=False).returncode


def main():
    args = parser().parse_args()
    try:
        if args.binary:
            return run(args, str(args.binary.absolute()))
        with tempfile.TemporaryDirectory(prefix="dnd-p6-driver-") as work:
            binary = Path(work) / "raw-move-image"
            build(binary)
            return run(args, str(binary))
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"P6 image operation refused: {error}", file=__import__("sys").stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
