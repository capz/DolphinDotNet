"""Execute the linked PowerPC bootstrap/return path under Unicorn 2.1.4.

Stops at the receiver CRT entry; peripheral shutdown, CRT and physical cache
coherency still need Dolphin/hardware acceptance. No cache instruction is mocked.
Unicorn is a test-only dependency; it is not linked into the launcher.
"""
import argparse
from pathlib import Path
import struct
import sys

from unicorn import Uc, UC_ARCH_PPC, UC_MODE_PPC32, UC_MODE_BIG_ENDIAN, UC_HOOK_BLOCK
from unicorn.ppc_const import (UC_PPC_REG_1, UC_PPC_REG_2, UC_PPC_REG_3,
                               UC_PPC_REG_4, UC_PPC_REG_5, UC_PPC_REG_6,
                               UC_PPC_REG_13, UC_PPC_REG_MSR, UC_PPC_REG_PC)

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from pack_resident import layout


def segments(elf):
    phoff = struct.unpack_from('>I', elf, 28)[0]
    stride, count = struct.unpack_from('>HH', elf, 42)
    for index in range(count):
        kind, offset, address, _, files, memory, _, _ = struct.unpack_from(
            '>8I', elf, phoff + stride * index)
        if kind == 1 and memory:
            yield address, elf[offset:offset + files], memory


def check(boot, receiver, archive):
    limits = layout()
    base, end, stub = (limits[name] for name in (
        'DND_RESIDENT_BASE', 'DND_RESIDENT_END', 'DND_RETURN_STUB'))
    entry = struct.unpack_from('>I', receiver, 24)[0]
    boot_entry = struct.unpack_from('>I', boot, 24)[0]
    cpu = Uc(UC_ARCH_PPC, UC_MODE_PPC32 | UC_MODE_BIG_ENDIAN)
    cpu.mem_map(0x80000000, end - 0x80000000)
    for address, data, memory in segments(boot):
        cpu.mem_write(address, data + bytes(memory - len(data)))
    arrived = [False, False]

    def block(cpu, address, size, unused):
        del size, unused
        if base <= address < end:
            arrived[0] = True
        if arrived[0] and address == entry:
            arrived[1] = True
            cpu.emu_stop()

    cpu.hook_add(UC_HOOK_BLOCK, block)
    for run in range(4):
        arrived[:] = [False, False]
        if run:
            # Simulate an application overwriting all receiver code, globals,
            # BSS, old stacks and the entire original bootstrap. Only the low
            # reload stub and high resident reservation belong to the loader.
            cpu.mem_write(0x80003100, bytes([0x90 + run]) * (base - 0x80003100))
            for word in (0x30, 0x34, 0x38, 0xec, 0xf4):
                cpu.mem_write(0x80000000 + word, b'\xff' * 4)
        cpu.reg_write(UC_PPC_REG_1, 0xdeadbee0)  # No usable old stack.
        cpu.reg_write(UC_PPC_REG_2, 0xbad00000)
        cpu.reg_write(UC_PPC_REG_13, 0xbad10000)
        cpu.reg_write(UC_PPC_REG_MSR, 0x8000)  # EE initially enabled.
        cpu.emu_start(boot_entry if run == 0 else stub, 0, count=20000000)
        assert arrived == [True, True], hex(cpu.reg_read(UC_PPC_REG_PC))
        assert cpu.reg_read(UC_PPC_REG_1) == 0xdeadbee0
        assert not cpu.reg_read(UC_PPC_REG_MSR) & 0x8000
        for reg in (UC_PPC_REG_3, UC_PPC_REG_4, UC_PPC_REG_5, UC_PPC_REG_6):
            assert cpu.reg_read(reg) == 0
        for address, data, memory in segments(receiver):
            assert cpu.mem_read(address, memory) == data + bytes(memory - len(data)), hex(address)
        assert cpu.mem_read(base, len(archive)) == archive
        assert cpu.mem_read(stub + 4, 8) == b'STUBHAXX'
        for word in (0x34, 0x38, 0xec):
            assert struct.unpack('>I', cpu.mem_read(0x80000000 + word, 4))[0] == base
        for word in (0x30, 0xf4):
            assert cpu.mem_read(0x80000000 + word, 4) == bytes(4)
    print('Linked PowerPC bootstrap plus 3 returns: original load bytes, zero BSS, '
          'immutable resident image, no old stack/SDA, interrupt mask and arena ceiling passed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--boot', type=Path, default=Path('build/DolphinDotNet.Launcher.elf'))
    parser.add_argument('--receiver', type=Path, default=Path('build/receiver.elf'))
    parser.add_argument('--archive', type=Path, default=Path('build/resident.bin'))
    args = parser.parse_args()
    check(args.boot.read_bytes(), args.receiver.read_bytes(), args.archive.read_bytes())
