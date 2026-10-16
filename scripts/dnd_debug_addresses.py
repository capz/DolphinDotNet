#!/usr/bin/env python3
"""Translate verified C# PDB line locations to native PowerPC ELF breakpoint PCs.

Uses symbol extents and ELF DWARF line records; never guesses PCs from IL offsets.
Requires GNU readelf, available in devkitPPC. No external Python packages.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROW = re.compile(r'^\s*(?P<file>.+?)\s+(?P<line>\d+|-)\s+(?P<address>0x[0-9a-fA-F]+|[0-9a-fA-F]+)(?:\s|$)')


def norm(path):
    return re.sub('/+', '/', path.strip().replace('\\', '/')).lower()


def parse_lines(output):
    rows, heading = [], ''
    for text in output.splitlines():
        line = text.strip()
        if line.endswith(('.cs:', '.c:', '.cpp:')):
            heading = line[:-1].removeprefix('CU: ').strip()
        match = ROW.match(text)
        if not match or match['line'] == '-':
            continue
        number, address = int(match['line']), int(match['address'], 16)
        if number < 1 or address == 0 or address % 4:
            continue
        file = norm(match['file'])
        context = norm(heading)
        # GNU readelf may put the full path only in a section heading.
        if '/' not in file and context.endswith('/' + file):
            file = context
        rows.append((file, number, address))
    return rows


def parse_symbols(output):
    symbols = {}
    for text in output.splitlines():
        parts = text.split()
        if len(parts) < 8 or not parts[0].endswith(':') or parts[3] != 'FUNC' or parts[6] in ('UND', 'ABS'):
            continue
        try:
            address, size = int(parts[1], 16), int(parts[2])
        except ValueError:
            continue
        name = parts[7]
        if not name.startswith('dnd_value_') or not size or not address:
            continue
        if name in symbols and symbols[name] != (address, size):
            raise ValueError('Conflicting symbol extents for ' + name)
        symbols[name] = (address, size)
    return symbols


def matches(dwarf_file, document, basename_index):
    path = norm(document)
    if dwarf_file == path or dwarf_file.endswith('/' + path):
        return True
    basename = path.rsplit('/', 1)[-1]
    if dwarf_file == basename:
        if len(basename_index.get(basename, set())) != 1:
            raise ValueError('Ambiguous DWARF basename ' + basename + '; use full source paths')
        return True
    return False


def resolve(source_map, line_dump, symbol_dump):
    if source_map.get('schemaVersion') != 2:
        raise ValueError('Expected matching Portable PDB source map schemaVersion 2')
    lines = parse_lines(line_dump)
    symbols = parse_symbols(symbol_dump)
    if not symbols:
        raise ValueError('No generated native function symbols found')
    basenames = {}
    for method in source_map['methods']:
        for location in method.get('sourceLocations', []):
            path = norm(location['document'])
            basenames.setdefault(path.rsplit('/', 1)[-1], set()).add(path)
    result = []
    for method in source_map['methods']:
        name = method['symbol']
        if name not in symbols:
            raise ValueError('Native ELF missing generated method: ' + name)
        begin, size = symbols[name]
        locations = {}
        for source in method.get('sourceLocations', []):
            locations.setdefault((source['document'], source['startLine']), set()).add(source['ilOffset'])
        breakpoints = []
        for (document, line), il_offsets in sorted(locations.items()):
            addresses = sorted({pc for filename, number, pc in lines
                                if begin <= pc < begin + size and number == line
                                and matches(filename, document, basenames)})
            if addresses:
                breakpoints.append({
                    'document': document, 'line': line, 'ilOffsets': sorted(il_offsets),
                    'addresses': [f'0x{pc:08x}' for pc in addresses]})
        result.append({'symbol': name, 'assembly': method['assembly'],
                       'methodToken': method['methodToken'],
                       'breakpoints': breakpoints})
    return {'schemaVersion': 1, 'methods': sorted(result, key=lambda method: method['symbol'])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--elf', required=True, type=Path)
    parser.add_argument('--source-map', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--readelf', default='readelf')
    parser.add_argument('--require-source')
    args = parser.parse_args()
    for path in (args.elf, args.source_map):
        if not path.is_file():
            parser.error('Missing input: ' + str(path))
    source_bytes = args.source_map.read_bytes()
    lines = subprocess.run([args.readelf, '--debug-dump=decodedline', str(args.elf)],
                           capture_output=True, text=True, check=True).stdout
    symbols = subprocess.run([args.readelf, '-Ws', str(args.elf)],
                             capture_output=True, text=True, check=True).stdout
    mapped = resolve(json.loads(source_bytes), lines, symbols)
    mapped['elfSha256'] = hashlib.sha256(args.elf.read_bytes()).hexdigest()
    mapped['sourceMapSha256'] = hashlib.sha256(source_bytes).hexdigest()
    count = sum(len(method['breakpoints']) for method in mapped['methods'])
    if not count:
        parser.error('No verifiable native breakpoint addresses')
    if args.require_source:
        source = norm(args.require_source)
        if not any(norm(bp['document']).endswith(source)
                   for method in mapped['methods'] for bp in method['breakpoints']):
            parser.error('No native breakpoint address in source ' + args.require_source)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(mapped, indent=2, sort_keys=True) + '\n')
    print(f'Validated {count} source breakpoint lines in {args.elf}')


if __name__ == '__main__':
    main()
