#!/usr/bin/env python3
"""Experimental GameCube libogc2 EXI_Sync correction for Dolphin debugging.

libogc2's EXI_Sync uses _exiReg[7 + channel][0], an alias at
0xCC00688C for channel zero. On Dolphin 2503 the inlined EXI_ImmEx
poll remains busy indefinitely. Change it to the conventional
_exiReg[channel][3] EXI control register at 0xCC00680C.

This is a tightly guarded *experimental ELF copy* transformation,
not an SDK/archive modification. Every instruction and symbol is
verified; unknown library versions fail without changing the input.
"""
import argparse
from pathlib import Path
import struct


def function_offsets(data: bytes) -> dict[str, tuple[int, int]]:
    if data[:6] != b"\x7fELF\x01\x02":
        raise ValueError("Expected ELF32 big-endian executable")
    if struct.unpack_from(">H", data, 18)[0] != 20:
        raise ValueError("Expected PowerPC ELF (e_machine=20)")
    shoff = struct.unpack_from(">I", data, 32)[0]
    shentsize, shnum = struct.unpack_from(">HH", data, 46)
    if shentsize != 40 or not shnum:
        raise ValueError("Invalid ELF32 section headers")
    sections = [struct.unpack_from(">10I", data, shoff + i * shentsize) for i in range(shnum)]
    functions = {}
    for section in sections:
        if section[1] != 2:  # SHT_SYMTAB
            continue
        symoff, symsize, stridx, entrysize = section[4], section[5], section[6], section[9]
        if entrysize != 16 or stridx >= len(sections):
            raise ValueError("Malformed ELF symbol table")
        strings = sections[stridx]
        strdata = data[strings[4]:strings[4] + strings[5]]
        for pos in range(symoff, symoff + symsize, 16):
            name_idx, address, size, info, _, secidx = struct.unpack_from(">IIIBBH", data, pos)
            if info & 0x0F != 2 or secidx >= len(sections) or name_idx >= len(strdata):
                continue
            name = strdata[name_idx:].split(b"\0", 1)[0].decode("ascii", "replace")
            target = sections[secidx]
            if not target[3] <= address < target[3] + target[5]:
                continue
            offset = target[4] + address - target[3]
            if size and offset + size <= len(data):
                functions[name] = (offset, size)
    return functions


PATCHES = {
    "EXI_Sync": [
        (bytes.fromhex("39 03 00 07"), bytes.fromhex("39 03 00 00")),  # channel+7 -> channel
        (bytes.fromhex("39 4a 68 00"), bytes.fromhex("39 4a 68 0c")),  # CSR alias -> control
    ],
    "EXI_ImmEx": [
        (bytes.fromhex("39 4a 68 8c"), bytes.fromhex("39 4a 68 0c")),  # inlined EXI_Sync
    ],
}


def corrected_elf(contents: bytes) -> bytes:
    sections = function_offsets(contents)
    patched = bytearray(contents)
    for function, instructions in PATCHES.items():
        if function not in sections:
            raise ValueError(f"Missing required libogc2 function {function}")
        start, length = sections[function]
        for original, replacement in instructions:
            region = contents[start:start + length]
            matches = [start + i for i in range(0, len(region) - 3, 4)
                       if region[i:i + 4] == original]
            if len(matches) != 1:
                raise ValueError(f"{function}: expected exactly one {original.hex()} instruction, found {len(matches)}")
            patched[matches[0]:matches[0] + 4] = replacement
            print(f"{function}: ELF offset 0x{matches[0]:x} {original.hex()} -> {replacement.hex()}")
    return bytes(patched)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.input.resolve() == args.output.resolve():
        parser.error("The original ELF must remain untouched")
    corrected = corrected_elf(args.input.read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(corrected)


if __name__ == "__main__":
    main()
