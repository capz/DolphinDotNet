#!/usr/bin/env python3
"""Build the SHA-256-pinned independent exFAT checker in a private CI prefix."""
import argparse
import hashlib
from pathlib import Path
import subprocess
import tarfile
import tempfile
import urllib.request

VERSION = "1.2.8"
SHA256 = "d742b17af7b09370b9334deff167222f21720577c6eea3f9b3c51770ac544516"
URL = f"https://github.com/exfatprogs/exfatprogs/releases/download/{VERSION}/exfatprogs-{VERSION}.tar.xz"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prefix", type=Path, required=True)
    args = parser.parse_args()
    prefix = args.prefix.absolute()
    prefix.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryDirectory(prefix="dnd-p6-exfatprogs-") as temporary:
        work = Path(temporary)
        archive = work / "source.tar.xz"
        urllib.request.urlretrieve(URL, archive)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != SHA256:
            raise SystemExit("Independent exfatprogs archive checksum mismatch")
        with tarfile.open(archive) as package:
            package.extractall(work, filter="data")
        source = work / f"exfatprogs-{VERSION}"
        for command in (("./configure", "--prefix=" + str(prefix)), ("make", "-j2"), ("make", "install")):
            subprocess.run(command, cwd=source, check=True)
    print(f"Verified exfatprogs {VERSION}: {SHA256}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
