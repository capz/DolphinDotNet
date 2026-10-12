"""Pad and validate the loadable sections produced by elf2dol (big-endian DOL)."""
import argparse
from pathlib import Path
import struct

HEADER_SIZE = 0x100
SECTION_COUNT = 18  # Seven text sections, eleven data sections.


def sections(data, aligned=False):
    if len(data) < HEADER_SIZE:
        raise ValueError("truncated DOL header")
    result = []
    for index in range(SECTION_COUNT):
        offset, address, size = (struct.unpack_from(">I", data, base + index * 4)[0]
                                 for base in (0, 0x48, 0x90))
        if not size:
            continue
        if offset < HEADER_SIZE or offset + size > len(data):
            raise ValueError(f"section {index}: payload outside file")
        if address + size > 0x100000000:
            raise ValueError(f"section {index}: address overflow")
        if aligned and (offset % 32 or size % 32):
            raise ValueError(f"section {index}: offset or size is not aligned to 32 bytes")
        result.append((index, offset, address, size))
    if not result:
        raise ValueError("DOL has no loadable sections")
    for field in (1, 2):
        ordered = sorted(result, key=lambda item: item[field])
        for left, right in zip(ordered, ordered[1:]):
            if left[field] + left[3] > right[field]:
                raise ValueError("overlapping DOL sections")
    return result


def pad(data):
    source_sections = sections(data)
    output = bytearray(data[:HEADER_SIZE])
    for index, offset, address, size in source_sections:
        padded_size = (size + 31) & ~31
        struct.pack_into(">I", output, index * 4, len(output))
        struct.pack_into(">I", output, 0x90 + index * 4, padded_size)
        output.extend(data[offset:offset + size])
        output.extend(bytes(padded_size - size))
    # Padding must not overwrite another section's initialized memory.
    sections(output, aligned=True)
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path)
    parser.add_argument("--pad", action="store_true")
    args = parser.parse_args()
    try:
        data = args.path.read_bytes()
        if args.pad:
            data = pad(data)
            args.path.write_bytes(data)
        sections(data, aligned=True)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{args.path}: {error}\n")
    print(f"{args.path}: all loadable section offsets and sizes are 32-byte aligned")


if __name__ == "__main__":
    main()
