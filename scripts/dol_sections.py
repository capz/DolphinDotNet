"""Pad and validate the loadable sections produced by elf2dol (big-endian DOL)."""
import argparse
from pathlib import Path
import struct

HEADER_SIZE = 0x100
MEM1_START = 0x80000000
MEM1_END = 0x81800000
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
        if not (MEM1_START <= address < MEM1_END) or address + size > MEM1_END:
            raise ValueError(f"section {index}: outside GameCube MEM1")
        if address % 32:
            raise ValueError(f"section {index}: load address is not 32-byte aligned")
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
    bss_address, bss_size, entry = struct.unpack_from(">III", data, 0xD8)
    if bss_size:
        if bss_address < MEM1_START or bss_address + bss_size > MEM1_END:
            raise ValueError("BSS outside GameCube MEM1")
        for index, _, address, size in result:
            # The DOL converter sometimes rounds the final initialized data
            # up over the beginning of BSS. That overlap is safe *only* if
            # the overlapped bytes are zero and belong entirely to padding.
            if address < bss_address + bss_size and bss_address < address + size:
                start = max(bss_address, address) - address
                stop = min(bss_address + bss_size, address + size) - address
                file_offset = next(item[1] for item in result if item[0] == index)
                if any(data[file_offset + start:file_offset + stop]):
                    raise ValueError(f"section {index}: nonzero initialized bytes overlap BSS")
    if not any(index < 7 and address <= entry < address + size
               for index, _, address, size in result):
        raise ValueError("entry point outside executable text sections")
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
