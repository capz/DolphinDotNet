#!/usr/bin/env python3
"""Build a complete, deterministic IPL apploader image from a PPC32 ELF.

The image includes the 0x20-byte header and zero-initialized allocated BSS.
Place it at disc offset 0x2440; this loader reads the main DOL at 0x10000.
"""
import argparse
from pathlib import Path
import struct

BASE = 0x81200000
DISC_OFFSET = 0x2440
DOL_OFFSET = 0x10000
MAX_BODY = DOL_OFFSET - DISC_OFFSET - 32
DATE = b'2026/11/11\0\0\0\0\0\0'


def image(elf):
    if len(elf) < 52 or elf[:7] != b'\x7fELF\x01\x02\x01':
        raise ValueError('apploader requires a big-endian ELF32 executable')
    kind, machine = struct.unpack_from('>HH', elf, 16)
    entry, = struct.unpack_from('>I', elf, 24)
    start, = struct.unpack_from('>I', elf, 32)
    stride, count = struct.unpack_from('>HH', elf, 46)
    if kind != 2 or machine != 20 or entry != BASE:
        raise ValueError('apploader must be a PPC executable with entry at 0x81200000')
    if stride != 40 or not count or start < 52 or start + count * stride > len(elf):
        raise ValueError('invalid ELF section table')
    sections = []
    for n in range(count):
        _, section_kind, flags, address, offset, size = struct.unpack_from('>6I', elf, start + n * stride)
        if not flags & 2 or not size:
            continue
        if section_kind not in (1, 8) or address < BASE or address - BASE + size > MAX_BODY:
            raise ValueError('allocated ELF section exceeds the apploader reserved area')
        if section_kind != 8 and (offset > len(elf) or size > len(elf) - offset):
            raise ValueError('truncated allocated ELF section')
        sections.append((address, size, section_kind, offset, flags))
    sections.sort()
    if not sections or not any(a <= entry < a + size and flags & 4 and kind == 1
                               for a, size, kind, _, flags in sections):
        raise ValueError('entry is not backed by executable bytes')
    for left, right in zip(sections, sections[1:]):
        if left[0] + left[1] > right[0]:
            raise ValueError('overlapping allocated ELF sections')
    length = (max(a + size for a, size, *_ in sections) - BASE + 31) & ~31
    if length > MAX_BODY:
        raise ValueError('aligned apploader exceeds the disc boot area')
    body = bytearray(length)
    for address, size, kind, offset, _ in sections:
        if kind != 8:
            body[address - BASE:address - BASE + size] = elf[offset:offset + size]
    return DATE + struct.pack('>4I', entry, length, 0, 0) + body


def validate_dol(data):
    if len(data) < 256:
        raise ValueError('truncated DOL header')
    sections = []
    for i in range(18):
        offset, address, size = (struct.unpack_from('>I', data, base + i * 4)[0]
                                 for base in (0, 0x48, 0x90))
        if not size:
            continue
        if offset < 256 or offset % 32 or address % 32 or size % 32 or offset + size > len(data):
            raise ValueError('DOL section is truncated or not DMA aligned')
        if address < 0x80003100 or address + size > BASE:
            raise ValueError('DOL section overlaps IPL memory or the apploader')
        sections.append((offset, address, size, i))
    for field in (0, 1):
        ordered = sorted(sections, key=lambda s: s[field])
        for left, right in zip(ordered, ordered[1:]):
            if left[field] + left[2] > right[field]:
                raise ValueError('overlapping DOL sections')
    bss, size, entry = struct.unpack_from('>3I', data, 0xd8)
    if size and (bss < 0x80003100 or bss + size > BASE):
        raise ValueError('DOL BSS overlaps IPL memory or the apploader')
    if size:
        for offset, address, length, _ in sections:
            first, end = max(address, bss), min(address + length, bss + size)
            if first < end and any(data[offset + first - address:offset + end - address]):
                raise ValueError('nonzero initialized DOL bytes overlap BSS')
    if not any(i < 7 and address <= entry < address + size for _, address, size, i in sections):
        raise ValueError('DOL entry is outside executable text')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('elf', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--dol', type=Path)
    args = parser.parse_args()
    try:
        result = image(args.elf.read_bytes())
        if args.dol:
            validate_dol(args.dol.read_bytes())
        args.output.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.output.with_suffix(args.output.suffix + '.tmp')
        temporary.write_bytes(result)
        temporary.replace(args.output)
    except (ValueError, OSError) as error:
        parser.exit(1, f'apploader: {error}\n')
    print(f'{args.output}: {len(result)} bytes including IPL header and initialized BSS')


if __name__ == '__main__':
    main()
