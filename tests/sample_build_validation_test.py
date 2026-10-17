"""Reject stale or incomplete Rider outputs in the shared sample artifact check."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from validate_sample_build import validate_debug_maps


class DebugArtifactValidation(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name)
        (self.output / 'Demo.elf').write_bytes(b'ELF fixture')
        source = json.dumps({'schemaVersion': 2, 'methods': []}).encode()
        (self.output / 'Demo.dnd-debug.json').write_bytes(source)
        self.addresses = {
            'schemaVersion': 1,
            'elfSha256': hashlib.sha256(b'ELF fixture').hexdigest(),
            'sourceMapSha256': hashlib.sha256(source).hexdigest(),
            'methods': [{'nativeStart': '0x80001000', 'nativeSize': 32, 'breakpoints': [{
                'document': r'C:\checkout\samples\Demo\Program.cs',
                'line': 10, 'addresses': ['0x80001004']}]}]}
        self.write_map()

    def write_map(self):
        (self.output / 'Demo.dnd-addresses.json').write_text(json.dumps(self.addresses))

    def test_valid_windows_source_path(self):
        self.assertEqual(1, validate_debug_maps(self.output, 'Demo'))

    def test_stale_elf_and_source_maps_are_rejected(self):
        for name in ('Demo.elf', 'Demo.dnd-debug.json'):
            with self.subTest(name=name):
                path = self.output / name
                original = path.read_bytes()
                path.write_bytes(original + b' ')
                with self.assertRaisesRegex(ValueError, 'does not match'):
                    validate_debug_maps(self.output, 'Demo')
                path.write_bytes(original)

    def test_dependency_only_breakpoints_do_not_validate_a_sample(self):
        self.addresses['methods'][0]['breakpoints'][0]['document'] = '/src/library/Program.cs'
        self.write_map()
        with self.assertRaisesRegex(ValueError, 'No verified breakpoint'):
            validate_debug_maps(self.output, 'Demo')

    def test_missing_map_and_invalid_native_addresses_are_rejected(self):
        for pc in ('0x80001003', '0x80002000'):
            with self.subTest(pc=pc):
                self.addresses['methods'][0]['breakpoints'][0]['addresses'] = [pc]
                self.write_map()
                with self.assertRaisesRegex(ValueError, 'native method extent'):
                    validate_debug_maps(self.output, 'Demo')
        (self.output / 'Demo.dnd-addresses.json').unlink()
        with self.assertRaises(FileNotFoundError):
            validate_debug_maps(self.output, 'Demo')


if __name__ == '__main__':
    unittest.main()
