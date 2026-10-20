#!/usr/bin/env python3
"""Zip existing GameCube sample DOLs without triggering additional builds."""

import argparse
from pathlib import Path
import re
from zipfile import ZIP_DEFLATED, ZipFile


def package_samples(output_dir: Path, specifications: list[str]) -> list[Path]:
    """Create one <Sample>.zip containing <Sample>.dol per existing CI output."""
    if not specifications:
        raise ValueError("At least one sample DOL is required")

    samples = []
    seen = set()
    for specification in specifications:
        name, separator, filename = specification.partition("=")
        if not separator or not re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]*", name) or not filename:
            raise ValueError(f"Expected SampleName=path/to/built.dol: {specification}")
        if name in seen:
            raise ValueError(f"Duplicate sample: {name}")
        seen.add(name)
        source = Path(filename)
        if source.suffix.lower() != ".dol" or not source.is_file() or source.stat().st_size == 0:
            raise ValueError(f"Missing or empty built sample DOL: {source}")
        samples.append((name, source))

    output_dir.mkdir(parents=True, exist_ok=True)
    archives = []
    for name, source in samples:
        archive_path = output_dir / f"{name}.zip"
        with ZipFile(archive_path, "w", compression=ZIP_DEFLATED, compresslevel=1) as archive:
            archive.write(source, arcname=f"{name}.dol")
        print(f"Packaged {source} -> {archive_path}")
        archives.append(archive_path)
    return archives


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument("samples", nargs="+", metavar="NAME=BUILT_DOL")
    arguments = parser.parse_args()
    try:
        package_samples(arguments.output_dir, arguments.samples)
    except ValueError as error:
        parser.error(str(error))


if __name__ == "__main__":
    main()
