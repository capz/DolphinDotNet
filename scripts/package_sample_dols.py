#!/usr/bin/env python3
"""Collect existing GameCube sample DOLs and make one release archive."""

import argparse
from pathlib import Path
import re
from shutil import copyfile
from zipfile import ZIP_DEFLATED, ZipFile


def _sample_name(name: str) -> bool:
    return re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]*", name) is not None


def stage_samples(output_dir: Path, specifications: list[str]) -> list[Path]:
    """Copy only DOLs built by the current CI job into an artifact directory."""
    if not specifications:
        raise ValueError("At least one built sample DOL is required")

    samples = []
    seen = set()
    for specification in specifications:
        name, separator, filename = specification.partition("=")
        if not separator or not _sample_name(name) or not filename:
            raise ValueError(f"Expected SampleName=path/to/built.dol: {specification}")
        if name in seen:
            raise ValueError(f"Duplicate sample: {name}")
        seen.add(name)
        source = Path(filename)
        if source.suffix.lower() != ".dol" or not source.is_file() or source.stat().st_size == 0:
            raise ValueError(f"Missing or empty built sample DOL: {source}")
        samples.append((name, source))

    output_dir.mkdir(parents=True, exist_ok=True)
    staged = []
    for name, source in samples:
        target = output_dir / f"{name}.dol"
        copyfile(source, target)
        print(f"Staged {source} -> {target}")
        staged.append(target)
    return staged


def bundle_samples(output_zip: Path, input_dir: Path, expected_names: list[str]) -> Path:
    """Bundle precisely the samples built in the normal CI workflow into one ZIP."""
    if not expected_names or any(not _sample_name(name) for name in expected_names):
        raise ValueError("Expected a list of valid sample names")
    expected = set(expected_names)
    if len(expected) != len(expected_names):
        raise ValueError("Duplicate expected sample names")

    actual_files = list(input_dir.glob("*.dol"))
    actual = {path.stem for path in actual_files}
    if actual != expected or len(actual_files) != len(expected):
        raise ValueError(f"Missing/unexpected DOLs: expected {sorted(expected)}, got {sorted(actual)}")
    for name in expected:
        if (input_dir / f"{name}.dol").stat().st_size == 0:
            raise ValueError(f"Empty sample DOL: {name}")

    output_zip.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(output_zip, "w", compression=ZIP_DEFLATED, compresslevel=1) as archive:
        for name in sorted(expected):
            archive.write(input_dir / f"{name}.dol", arcname=f"{name}.dol")
    print(f"Packaged {len(expected)} existing sample DOLs into {output_zip}")
    return output_zip


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    stage = commands.add_parser("stage", help="Copy existing built sample DOLs into an artifact")
    stage.add_argument("output_dir", type=Path)
    stage.add_argument("samples", nargs="+", metavar="NAME=BUILT_DOL")
    bundle = commands.add_parser("bundle", help="Create the single downloadable ZIP")
    bundle.add_argument("output_zip", type=Path)
    bundle.add_argument("input_dir", type=Path)
    bundle.add_argument("expected", nargs="+", metavar="SAMPLE_NAME")
    args = parser.parse_args()
    try:
        if args.command == "stage":
            stage_samples(args.output_dir, args.samples)
        else:
            bundle_samples(args.output_zip, args.input_dir, args.expected)
    except ValueError as error:
        parser.error(str(error))


if __name__ == "__main__":
    main()
