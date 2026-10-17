#!/usr/bin/env python3
"""Check the native output and Rider artifacts of one or all sample builds."""
import argparse
import hashlib
import json
from pathlib import Path

from dol_sections import sections


def validate_debug_maps(output, sample):
    elf = output / f'{sample}.elf'
    source_path = output / f'{sample}.dnd-debug.json'
    addresses = json.loads((output / f'{sample}.dnd-addresses.json').read_text())
    source = json.loads(source_path.read_text())
    if source.get('schemaVersion') != 2 or addresses.get('schemaVersion') != 1:
        raise ValueError('Unsupported debug map schema')
    for path, key in ((elf, 'elfSha256'), (source_path, 'sourceMapSha256')):
        if addresses.get(key, '').lower() != hashlib.sha256(path.read_bytes()).hexdigest():
            raise ValueError(f'Debug map does not match {path.name}')
    points = [point for method in addresses['methods'] for point in method['breakpoints']
              if point.get('addresses')]
    suffix = f'/samples/{sample}/Program.cs'.lower()
    own_points = [point for point in points
                  if ('/' + point['document'].replace('\\', '/')).lower().endswith(suffix)]
    if not own_points:
        raise ValueError(f'No verified breakpoint addresses in {sample}/Program.cs')
    for method in addresses['methods']:
        for point in method['breakpoints']:
            begin, size = int(method['nativeStart'], 16), method['nativeSize']
            if any(not (begin <= int(pc, 16) < begin + size) or int(pc, 16) % 4
                   for pc in point['addresses']):
                raise ValueError('Breakpoint address outside its aligned native method extent')
    return len(own_points)


def validate(sample_directory, configuration='Debug'):
    sample_directory = Path(sample_directory).resolve()
    sample = sample_directory.name
    output = sample_directory / 'bin' / configuration / 'net8.0'
    for name in (f'{sample}.dll', f'{sample}.pdb', f'{sample}.elf',
                 'DolphinDotNet.Build.dll', 'DolphinDotNet.Build.deps.json',
                 'DolphinDotNet.Build.runtimeconfig.json'):
        if not (output / name).is_file():
            raise ValueError(f'Missing build output: {output / name}')
    dol = output / f'{sample}.dol'
    sections(dol.read_bytes(), aligned=True)
    header = (output / f'{sample}.elf').read_bytes()[:20]
    if len(header) < 20 or header[:6] != b'\x7fELF\x01\x02' or header[18:20] != b'\x00\x14':
        raise ValueError('Expected a big-endian 32-bit PowerPC ELF')
    config = json.loads((output / 'dolphin-run.json').read_text())
    if Path(config['Dol']).resolve() != dol.resolve():
        raise ValueError('Launcher references a different sample or output directory')
    script = sample_directory / 'obj' / configuration / 'net8.0/native/scripts/build_exi_overlay.sh'
    if b'\r' in script.read_bytes():
        raise ValueError('Bundled shell script contains CRLF line endings')
    own_points = validate_debug_maps(output, sample) if configuration == 'Debug' else None
    return own_points


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('samples', nargs='*', type=Path,
                        help='Sample directories; default: every sample in this checkout')
    parser.add_argument('--configuration', choices=('Debug', 'Release'), default='Debug')
    args = parser.parse_args()
    samples = args.samples or sorted({p.parent for p in
        (Path(__file__).resolve().parents[1] / 'samples').glob('*/*.csproj')})
    if not samples:
        parser.error('No sample projects found')
    for sample in samples:
        try:
            count = validate(sample, args.configuration)
        except (OSError, ValueError, KeyError) as error:
            parser.exit(1, f'{sample}: {error}\n')
        detail = f'; {count} verified sample source lines' if count is not None else ''
        print(f'{sample.name}: {args.configuration} native output and launcher validated{detail}')


if __name__ == '__main__':
    main()
