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
    pending = None
    for text in output.splitlines():
        line = text.strip()
        if line.endswith(('.cs:', '.c:', '.cpp:')):
            heading = line[:-1].removeprefix('CU: ').strip()
        match = ROW.match(text)
        if not match:
            continue
        address = int(match['address'], 16)
        # DWARF rows describe [address, next address). Equal-PC views before
        # the last row have no executable extent. Include native/hidden rows
        # in this ordering so they can supersede an earlier C# row as well.
        if pending is not None and address > pending[2]:
            if pending[1] > 0 and pending[2] != 0 and pending[2] % 4 == 0:
                rows.append(pending)
        pending = None
        if match['line'] == '-':  # end_sequence is an exclusive boundary
            continue
        number = int(match['line'])
        file = norm(match['file'])
        context = norm(heading)
        # GNU readelf may put the full path only in a section heading.
        if '/' not in file and context.endswith('/' + file):
            file = context
        pending = (file, number, address)
    # A final row without a successor/end_sequence has no proven extent.
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
            # The compiler graph conservatively includes methods whose sections
            # the native linker can discard (for example, unused vtable entries).
            # Keep their identity, but never invent an extent or breakpoint PC.
            result.append({'symbol': name, 'assembly': method['assembly'],
                           'methodToken': method['methodToken'], 'breakpoints': [],
                           'unavailableReason': 'Method is absent from the linked ELF'})
            continue
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
                       'nativeStart': f'0x{begin:08x}', 'nativeSize': size,
                       'breakpoints': breakpoints})
    return {'schemaVersion': 1, 'methods': sorted(result, key=lambda method: method['symbol'])}


def _register(name):
    if name in ('ra', 'lr'):
        return 65  # DWARF PowerPC LR; RSP uses a different register number.
    if name == 'sp':
        return 1
    match = re.fullmatch(r'r(\d+)', name)
    if match and int(match[1]) in (*range(32), 65):
        return int(match[1])
    return None


def _unwind_row(columns, values):
    if len(columns) != len(values):
        return None
    data = dict(zip(columns, values))
    cfa = re.fullmatch(r'(r\d+|sp)([+-]\d+)', data.get('CFA', ''))
    if not cfa or _register(cfa[1]) is None:
        return None
    rules = {}
    for name, value in data.items():
        register = _register(name)
        if register is None:
            continue
        if value in ('s', 'u'):
            rule = {'kind': 'same' if value == 's' else 'undefined'}
        elif match := re.fullmatch(r'c([+-]\d+)', value):
            rule = {'kind': 'memory', 'offset': int(match[1])}
        elif (other := _register(value)) is not None:
            rule = {'kind': 'register', 'register': other}
        else:
            return None
        rules[str(register)] = rule
    if '65' not in rules:
        return None
    return {'cfaRegister': _register(cfa[1]), 'cfaOffset': int(cfa[2]), 'registers': rules}


def parse_unwind(output):
    """Bounded subset of readelf's interpreted PowerPC CFI; unsupported rows stay explicit."""
    cies, result, block = {}, {}, None

    def flush():
        nonlocal block
        if block is None:
            return
        if block['kind'] == 'CIE':
            cies[block['id']] = (block['ra'], block['rows'][-1][1] if block['rows'] else None)
        else:
            begin, end = block['begin'], block['end']
            cie = cies.get(block['cie'])
            powerpc = cie is not None and cie[0] == 65
            rows = [(pc, rule if powerpc else None) for pc, rule in block['rows']]
            if not rows or rows[0][0] > begin:
                rows.insert(0, (begin, cie[1] if powerpc else None))
            rows = [(pc, rule) for pc, rule in rows if begin <= pc < end]
            for index, (pc, rule) in enumerate(rows):
                limit = rows[index + 1][0] if index + 1 < len(rows) else end
                if pc >= limit or pc % 4 or limit % 4:
                    continue
                row = {'start': f'0x{pc:08x}', 'end': f'0x{limit:08x}', 'supported': rule is not None}
                if rule is not None:
                    row.update(rule)
                key = (pc, limit)
                if key in result and result[key] != row:
                    row = {'start': row['start'], 'end': row['end'], 'supported': False}
                result[key] = row
        block = None

    for text in output.splitlines():
        if text.startswith('Contents of the '):
            flush()
            cies = {}
        cie = re.match(r'^\s*([0-9a-fA-F]+)\s+.*\bCIE\b.*\bra=(\d+)', text)
        fde = re.match(r'^\s*[0-9a-fA-F]+\s+.*\bFDE\s+cie=([0-9a-fA-F]+)\s+pc=([0-9a-fA-F]+)\.\.([0-9a-fA-F]+)', text)
        if cie:
            flush()
            block = {'kind': 'CIE', 'id': int(cie[1], 16), 'ra': int(cie[2]), 'rows': [], 'columns': []}
        elif fde:
            flush()
            block = {'kind': 'FDE', 'cie': int(fde[1], 16), 'begin': int(fde[2], 16),
                     'end': int(fde[3], 16), 'rows': [], 'columns': []}
        elif 'ZERO terminator' in text:
            flush()
        elif block is not None:
            values = text.split()
            if 'CFA' in values:
                block['columns'] = values
            elif values and block['columns']:
                columns = block['columns']
                if columns[0] == 'LOC':
                    if not re.fullmatch(r'[0-9a-fA-F]+', values[0]):
                        continue
                    pc = int(values[0], 16)
                    rule = _unwind_row(columns[1:], values[1:])
                else:
                    pc = block.get('begin', 0)
                    rule = _unwind_row(columns, values)
                block['rows'].append((pc, rule))
    flush()
    return [result[key] for key in sorted(result)]



def parse_location_lists(output):
    """Bounded DWARF4 .debug_loc ranges, including readelf's split view rows."""
    lists, active, pending = {}, None, None
    for line in output.splitlines():
        if '<End of list>' in line:
            active, pending = None, None
            continue
        if 'location view pair' in line:
            continue
        view = re.match(r'^\s*([0-9a-fA-F]+)\s+v[0-9a-fA-F]+\s+v[0-9a-fA-F]+\s+views at .*for:', line)
        if view:
            pending = int(view[1], 16)
            if active is None:
                active = pending; lists[active] = []
            continue
        row = re.match(r'^\s*(?:([0-9a-fA-F]+)\s+)?([0-9a-fA-F]{8,16})\s+([0-9a-fA-F]{8,16})\s+(\(DW_OP_.*\))\s*$', line)
        if not row:
            continue
        if active is None:
            if row[1] is None and pending is None:
                continue
            active = int(row[1], 16) if row[1] else pending
            lists[active] = []
        begin, end = int(row[2], 16), int(row[3], 16)
        if begin < end:
            lists[active].append((begin, end, row[4]))
    return lists


def parse_variable_locations(output, location_dump=''):
    """Read a conservative DIE subset and bounded location lists; unsupported expressions stay unavailable."""
    stack, methods, current = [], {}, None
    location_lists = parse_location_lists(location_dump)
    for line in output.splitlines():
        match = re.match(r'^\s*<(\d+)><[0-9a-fA-F]+>:\s+Abbrev Number:\s+\d+(?:\s+\((DW_TAG_\w+)\))?', line)
        if match:
            depth, tag = int(match[1]), match[2]
            while stack and stack[-1]['depth'] >= depth:
                stack.pop()
            current = {'depth': depth, 'tag': tag, 'attrs': {}, 'children': []}
            if stack:
                stack[-1]['children'].append(current)
            if tag:
                stack.append(current)
            if tag == 'DW_TAG_subprogram':
                methods[id(current)] = current
            continue
        attr = re.match(r'^\s*<[0-9a-fA-F]+>\s+(DW_AT_\w+)\s*:\s*(.*)', line)
        if current is not None and attr:
            current['attrs'][attr[1]] = attr[2].strip()
    result = {}
    def name(value):
        # readelf --wide emits optional (strp)/(string)/offset decorations.
        return re.sub(r'^.*?\):\s*', '', value).strip() if '):' in value else re.sub(r'^\([^)]*\)\s*', '', value).strip()
    for method in methods.values():
        attrs = method['attrs']
        symbol = name(attrs.get('DW_AT_name', ''))
        if not symbol.startswith('dnd_value_') or 'DW_AT_low_pc' not in attrs:
            continue
        frame = attrs.get('DW_AT_frame_base', '')
        variables = {}
        # Generated ABI/local names belong directly to this generated C function;
        # do not accidentally collect inlined/nested function variables.
        for child in method['children']:
            if child['tag'] not in ('DW_TAG_variable', 'DW_TAG_formal_parameter'):
                continue
            a = child['attrs']
            native_name = name(a.get('DW_AT_name', ''))
            loc = a.get('DW_AT_location', '')
            def convert(expression):
                start = expression.find('(DW_OP_')
                text = expression[start + 1:-1].strip() if start >= 0 and expression.endswith(')') else ''
                location = {'kind': 'unavailable', 'reason': 'Unsupported or optimized-away DWARF location'}
                if m := re.fullmatch(r'DW_OP_fbreg:\s*(-?\d+)', text):
                    if re.search(r'\(DW_OP_call_frame_cfa\)\s*$', frame):
                        location = {'kind': 'cfaOffset', 'offset': int(m[1])}
                elif m := re.fullmatch(r'DW_OP_reg(\d+)(?:\s+\(r\d+\))?', text):
                    if int(m[1]) <= 31:
                        location = {'kind': 'register', 'register': int(m[1])}
                return location
            location = convert(loc)
            if '(location list)' in loc:
                offset = re.search(r'(?:\(sec_offset\)\s*)?(0x[0-9a-fA-F]+)\s*\(location list\)', loc)
                ranges = location_lists.get(int(offset[1], 16), []) if offset else []
                location = {'kind': 'rangeList', 'ranges': [(a, b, convert(e)) for a, b, e in ranges]}
            # Duplicate/aliased DIEs are ambiguous; never pick the final value.
            if native_name in variables:
                location = {'kind': 'unavailable', 'reason': 'Ambiguous DWARF variable'}
            variables[native_name] = location
        if symbol in result:
            result[symbol] = {}  # Conflicting function instances must not be guessed.
        else:
            result[symbol] = variables
    return result


def attach_variables(mapped, source_map, info_dump, location_dump=''):
    locations = parse_variable_locations(info_dump, location_dump)
    sources = {m['symbol']: m for m in source_map['methods']}
    for method in mapped['methods']:
        source = sources[method['symbol']]
        variables = []
        for variable in source.get('variables', []):
            value = dict(variable)
            value['location'] = locations.get(method['symbol'], {}).get(variable['nativeName'],
                {'kind': 'unavailable', 'reason': 'Missing native DWARF variable'})
            visible = []
            for point in method['breakpoints']:
                offsets = [p['ilOffset'] for p in source.get('sourceLocations', [])
                           if p['document'] == point['document'] and p['startLine'] == point['line']]
                # A source line spanning multiple lexical scopes is deliberately
                # unavailable rather than guessed from the line number alone.
                if offsets and all(variable['scopeStart'] <= i < variable['scopeEnd'] for i in offsets):
                    visible.extend(point['addresses'])
            location = value['location']
            if location['kind'] == 'rangeList':
                by_location = {}
                for pc in sorted(set(visible)):
                    candidates = [loc for begin, end, loc in location['ranges'] if begin <= int(pc, 16) < end]
                    resolved = candidates[0] if len(candidates) == 1 else {'kind': 'unavailable', 'reason': 'Missing or ambiguous DWARF location range'}
                    key = json.dumps(resolved, sort_keys=True)
                    by_location.setdefault(key, (resolved, []))[1].append(pc)
                for resolved, pcs in by_location.values():
                    variables.append(dict(value, location=resolved, visibleAddresses=pcs))
            else:
                value['visibleAddresses'] = sorted(set(visible))
                variables.append(value)
        method['variables'] = variables

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
    frames = subprocess.run([args.readelf, '--debug-dump=frames-interp', str(args.elf)],
                            capture_output=True, text=True, check=True).stdout
    mapped['unwind'] = parse_unwind(frames)
    info = subprocess.run([args.readelf, '--wide', '--debug-dump=info', str(args.elf)],
                          capture_output=True, text=True, check=True).stdout
    locs = subprocess.run([args.readelf, '--wide', '--debug-dump=loc', str(args.elf)],
                          capture_output=True, text=True, check=True).stdout
    attach_variables(mapped, json.loads(source_bytes), info, locs)
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
    absent = sum('unavailableReason' in method for method in mapped['methods'])
    if absent:
        print(f'{absent} managed methods are unavailable in the linked ELF; no addresses were assigned')
    print(f'Validated {count} source breakpoint lines in {args.elf}')


if __name__ == '__main__':
    main()
