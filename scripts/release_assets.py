#!/usr/bin/env python3
"""Select only packages produced by this run; missing selected extensions fail release."""
import argparse
from pathlib import Path


def collect_assets(samples, rider_dir=None, visual_studio_dir=None):
    samples = Path(samples)
    assets = [samples]
    if samples.suffix != '.zip':
        raise ValueError('Samples must be a ZIP archive')
    for directory, suffix in ((rider_dir, '.zip'), (visual_studio_dir, '.vsix')):
        if directory is None:
            continue
        matches = sorted(Path(directory).rglob('*' + suffix))
        if len(matches) != 1:
            raise ValueError(f'Expected exactly one {suffix} package in {directory}, found {len(matches)}')
        assets.extend(matches)
    for path in assets:
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f'Missing or empty release asset: {path}')
        if '\n' in str(path) or '\r' in str(path):
            raise ValueError('Release asset paths must fit one line')
    if len({p.name for p in assets}) != len(assets):
        raise ValueError('Release assets must have unique filenames')
    return assets


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('samples', type=Path)
    parser.add_argument('--rider-dir', type=Path)
    parser.add_argument('--visual-studio-dir', type=Path)
    args = parser.parse_args()
    try:
        for path in collect_assets(args.samples, args.rider_dir, args.visual_studio_dir):
            print(path)
    except ValueError as error:
        parser.exit(1, str(error) + '\n')
