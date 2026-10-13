"""Static ELF/DOL cross-check of PowerPC boot image; no emulator required."""
import argparse
import struct
from pathlib import Path

from dol_sections import sections, MEM1_START, MEM1_END

def inspect(elf_bytes, dol_bytes):
    if elf_bytes[:4] != b"\x7fELF" or elf_bytes[4] != 1 or elf_bytes[5] != 2:
        raise ValueError("expected 32-bit big-endian ELF")
    machine = struct.unpack_from(">H", elf_bytes, 18)[0]
    if machine != 20:
        raise ValueError(f"expected PowerPC ELF (machine 20), got {machine}")
    entry = struct.unpack_from(">I", elf_bytes, 24)[0]
    dol_entry = struct.unpack_from(">I", dol_bytes, 0xE0)[0]
    if entry != dol_entry:
        raise ValueError("ELF and DOL entry points differ")
    phoff = struct.unpack_from(">I", elf_bytes, 28)[0]
    phentsize, phnum = struct.unpack_from(">HH", elf_bytes, 42)
    if phentsize < 32 or phoff + phnum * phentsize > len(elf_bytes):
        raise ValueError("invalid program header table")
    segments = []
    for i in range(phnum):
        typ, offset, vaddr, paddr, filesz, memsz, flags, align = struct.unpack_from(
            ">IIIIIIII", elf_bytes, phoff + i * phentsize)
        if typ != 1:
            continue
        if memsz < filesz or offset + filesz > len(elf_bytes):
            raise ValueError("invalid PT_LOAD segment")
        if not (MEM1_START <= vaddr < MEM1_END) or vaddr + memsz > MEM1_END:
            raise ValueError("ELF segment outside MEM1")
        segments.append((offset, vaddr, filesz, memsz, flags))
    if not segments:
        raise ValueError("no PT_LOAD segments")
    dsections = sections(dol_bytes, aligned=True)
    # ELF's loadable bytes must appear at exactly their linked addresses,
    # while DOL conversion may add zero-filled alignment padding.
    for offset, vaddr, filesz, memsz, flags in segments:
        if filesz == 0:
            continue
        match = [s for s in dsections if s[2] <= vaddr and
                 vaddr + filesz <= s[2] + s[3]]
        if not match:
            raise ValueError(f"ELF PT_LOAD 0x{vaddr:08x} missing from DOL")
        index, dol_offset, dol_addr, _ = match[0]
        actual = dol_bytes[dol_offset + vaddr - dol_addr:
                           dol_offset + vaddr - dol_addr + filesz]
        if actual != elf_bytes[offset:offset + filesz]:
            raise ValueError(f"ELF/DOL payload mismatch at 0x{vaddr:08x}")
    bss_addr, bss_size = struct.unpack_from(">II", dol_bytes, 0xD8)
    for _, vaddr, filesz, memsz, _ in segments:
        if memsz > filesz:
            extra_start, extra_end = vaddr + filesz, vaddr + memsz
            if bss_size and not (bss_addr <= extra_start and extra_end <= bss_addr + bss_size):
                raise ValueError("ELF zero-fill region outside DOL BSS")
    return len(segments), entry

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("elf", type=Path)
    ap.add_argument("dol", type=Path)
    args = ap.parse_args()
    n, entry = inspect(args.elf.read_bytes(), args.dol.read_bytes())
    print(f"Verified {n} loadable ELF segments against DOL, entry 0x{entry:08x}")

if __name__ == "__main__":
    main()
