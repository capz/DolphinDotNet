"""Portable tests of pristine image packing and memory ownership across runs."""
from pathlib import Path
import struct
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from pack_resident import layout, pack
from wire_test import fixture


class ResidentTests(unittest.TestCase):
    def test_archive_contains_original_load_bytes_and_bss_plan(self):
        elf = bytearray(fixture())
        elf[128:160] = bytes(range(32))
        packed = pack(elf, b'\x60\0\0\0')
        limits = layout()
        entry, count = struct.unpack_from('>2I', packed, limits['DND_RESIDENT_PLAN'])
        self.assertEqual((entry, count), (0x80004000, 1))
        offset, address, files, memory = struct.unpack_from(
            '>4I', packed, limits['DND_RESIDENT_PLAN'] + 8)
        self.assertEqual((offset, address, files, memory), (0, entry, 32, 64))
        self.assertEqual(packed[limits['DND_RESIDENT_IMAGE']:], elf[128:160])
        self.assertEqual(len(packed), limits['DND_RESIDENT_IMAGE'] + files)

    def test_invalid_images_and_layout_fail_build(self):
        for length in range(84):
            with self.assertRaises(ValueError):
                pack(fixture()[:length], b'1234')
        mutations = [(16, '>H', 3), (18, '>H', 21), (24, '>I', 0x80004020),
                     (28, '>I', 0xffffffff), (44, '>H', 65), (52, '>I', 2),
                     (56, '>I', 0xffffffff), (68, '>I', 65),
                     (72, '>I', 0xffffffff), (76, '>I', 0), (80, '>I', 3)]
        for at, kind, value in mutations:
            elf = bytearray(fixture())
            struct.pack_into(kind, elf, at, value)
            with self.subTest(at=at), self.assertRaises(ValueError):
                pack(elf, b'1234')
        for stub in (b'', b'123', bytes(708)):
            with self.assertRaises(ValueError):
                pack(fixture(), stub)

    def test_snapshot_cannot_exceed_reserved_region(self):
        elf = bytearray(fixture())
        size = 2 * 1024 * 1024
        elf.extend(bytes(size))
        struct.pack_into('>II', elf, 68, size, size)
        with self.assertRaisesRegex(ValueError, 'reservation'):
            pack(elf, b'1234')

    def test_resident_overlap_including_bss_and_entry_is_rejected(self):
        for address, memory, accepted in [(0x815fffc0, 64, True),
                                           (0x815fffc0, 65, False),
                                           (0x81600000, 64, False)]:
            elf = bytearray(fixture())
            for at in (24, 60, 64):
                struct.pack_into('>I', elf, at, address)
            struct.pack_into('>I', elf, 72, memory)
            if accepted:
                pack(elf, b'1234')
            else:
                with self.assertRaises(ValueError):
                    pack(elf, b'1234')


if __name__ == '__main__':
    unittest.main()
