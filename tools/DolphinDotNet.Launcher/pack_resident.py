"""Pack load bytes before the receiver's first CRT invocation.

Debug metadata stays in receiver.elf; it consumes no resident memory. The
register-only trampoline restores initialized libogc data and zero-filled BSS.
"""
import argparse
from pathlib import Path
import re
import struct


def layout():
    return {name: int(value, 0) for name, value in re.findall(
        r"^#define (DND_\w+) (0x[0-9a-fA-F]+|[0-9]+)$",
        Path(__file__).with_name("resident.h").read_text(), re.M)}


def pack(elf, stub):
    limits = layout()
    base, end = limits['DND_RESIDENT_BASE'], limits['DND_RESIDENT_END']
    plan_at, stub_at, image_at = (limits['DND_RESIDENT_' + name]
                                  for name in ('PLAN', 'STUB', 'IMAGE'))
    if len(elf) < 52 or elf[:7] != b'\x7fELF\x01\x02\x01':
        raise ValueError('receiver must be ELF32 big-endian')
    kind, machine, version, entry, phoff = struct.unpack_from('>HHIII', elf, 16)
    ehsize, phsize, count = struct.unpack_from('>HHH', elf, 40)
    if (kind, machine, version, ehsize, phsize) != (2, 20, 1, 52, 32):
        raise ValueError('receiver must be fixed-address PowerPC ELF')
    if not 1 <= count <= 64 or phoff < 52 or phoff + count * 32 > len(elf):
        raise ValueError('invalid program-header table')
    segments, image, executable = [], bytearray(), False
    for index in range(count):
        kind, offset, address, physical, files, memory, flags, align = struct.unpack_from(
            '>8I', elf, phoff + index * 32)
        if kind in (2, 3):
            raise ValueError('dynamic executable is unsupported')
        if kind != 1 or not memory:
            continue
        if (files > memory or offset > len(elf) or files > len(elf) - offset or
                physical not in (0, address) or address < 0x80003100 or
                address >= base or memory > base - address or flags & ~7 or
                (align > 1 and (align & (align - 1) or address % align != offset % align))):
            raise ValueError('receiver load range is invalid or overlaps resident memory')
        if any(address < a + m and a < address + memory for _, a, _, m in segments):
            raise ValueError('overlapping receiver load ranges')
        segments.append((len(image), address, files, memory))
        image.extend(elf[offset:offset + files])
        executable |= bool(flags & 1 and files >= 4 and address <= entry <= address + files - 4)
    if not executable or entry & 3 or not 1 <= len(segments) <= 16:
        raise ValueError('invalid receiver entry or segment count')
    if plan_at + 8 + 16 * 16 > stub_at or not stub or len(stub) > image_at - stub_at or len(stub) % 4:
        raise ValueError('restore code does not fit resident layout')
    if image_at + len(image) > end - base:
        raise ValueError('pristine receiver exceeds resident reservation')
    result = bytearray(image_at)
    struct.pack_into('>4I', result, 0, 0x444e4452, 1, image_at + len(image), len(image))
    struct.pack_into('>2I', result, plan_at, entry, len(segments))
    for index, segment in enumerate(segments):
        struct.pack_into('>4I', result, plan_at + 8 + 16 * index, *segment)
    result[stub_at:stub_at + len(stub)] = stub
    return result + image


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('elf', type=Path)
    parser.add_argument('stub', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.write_bytes(pack(args.elf.read_bytes(), args.stub.read_bytes()))
