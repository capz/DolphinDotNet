import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location(
    "dol_sections", Path(__file__).resolve().parents[1] / "scripts/dol_sections.py")
dol = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dol)


def fixture(size=33, second_address=0x80003200):
    data = bytearray(0x100)
    for index, address, payload in ((0, 0x80003100, b'T' * 32),
                                    (7, second_address, b'D' * size)):
        for base, value in ((0, len(data)), (0x48, address), (0x90, len(payload))):
            struct.pack_into('>I', data, base + index * 4, value)
        data.extend(payload)
    struct.pack_into('>III', data, 0xD8, 0x80004000, 0x101, 0x80003100)
    return data


class DolSectionsTests(unittest.TestCase):
    def test_padding_preserves_payload_addresses_bss_and_entry(self):
        for size in (1, 31, 32, 33, 63, 64):
            with self.subTest(size=size):
                source = fixture(size)
                result = dol.pad(source)
                self.assertEqual(result[0x48:0x90], source[0x48:0x90])
                self.assertEqual(result[0xD8:0x100], source[0xD8:0x100])
                for index, offset, address, padded_size in dol.sections(result, True):
                    payload_size = 32 if index == 0 else size
                    self.assertEqual(result[offset:offset + payload_size],
                                     (b'T' if index == 0 else b'D') * payload_size)
                    self.assertEqual(result[offset + payload_size:offset + padded_size],
                                     bytes(padded_size - payload_size))
                self.assertEqual(dol.pad(result), result)

    def test_unaligned_header_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'aligned'):
            dol.sections(fixture(), True)

    def test_truncated_payload_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'outside file'):
            dol.pad(fixture()[:-1])

    def test_padding_overlap_is_rejected(self):
        data = fixture(33)
        # Text follows data in memory, with insufficient space for data padding.
        struct.pack_into('>I', data, 0x48, 0x80003221)
        with self.assertRaisesRegex(ValueError, 'aligned|overlapping'):
            dol.pad(data)

    def test_entrypoint_must_be_in_text(self):
        data = fixture()
        struct.pack_into('>I', data, 0xE0, 0x80003200)
        with self.assertRaisesRegex(ValueError, 'entry point'):
            dol.pad(data)

    def test_nonzero_data_must_not_overlap_bss(self):
        data = fixture()
        struct.pack_into('>II', data, 0xD8, 0x80003200, 64)
        with self.assertRaisesRegex(ValueError, 'overlap BSS'):
            dol.pad(data)

    def test_bss_must_be_in_mem1(self):
        data = fixture()
        struct.pack_into('>II', data, 0xD8, 0x81800000, 64)
        with self.assertRaisesRegex(ValueError, 'BSS outside'):
            dol.pad(data)

    def test_short_header_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'header'):
            dol.pad(bytes(255))

    def test_offsets_are_realigned(self):
        data = fixture()
        data.insert(0x120, 0)
        struct.pack_into('>I', data, 7 * 4, 0x121)
        result = dol.pad(data)
        self.assertEqual(struct.unpack_from('>I', result, 7 * 4)[0], 0x120)
        self.assertEqual(result[0x120:0x141], b'D' * 33)

    def test_address_overflow_is_rejected(self):
        data = fixture()
        struct.pack_into('>I', data, 0x48 + 7 * 4, 0xFFFFFFF0)
        with self.assertRaisesRegex(ValueError, 'MEM1'):
            dol.pad(data)

    def test_file_overlap_is_rejected(self):
        data = fixture()
        struct.pack_into('>I', data, 7 * 4, 0x110)
        with self.assertRaisesRegex(ValueError, 'overlapping'):
            dol.pad(data)


if __name__ == '__main__':
    unittest.main()
