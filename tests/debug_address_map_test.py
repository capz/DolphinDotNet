"""Native PowerPC address resolver tests plus a real host GCC DWARF fixture."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('debug_addresses', root / 'scripts/dnd_debug_addresses.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
SYMBOL = 'dnd_value_Demo_Program_Main_abc'
MAP = {'schemaVersion': 2, 'methods': [{
    'symbol': SYMBOL, 'assembly': 'Demo', 'methodToken': 100663297,
    'sourceLocations': [
        {'document': r'C:\project\Program.cs', 'startLine': 7, 'ilOffset': 2},
        {'document': r'C:\project\Program.cs', 'startLine': 8, 'ilOffset': 5},
        {'document': r'C:\project\Program.cs', 'startLine': 8, 'ilOffset': 6}]}]}
SYMBOLS = f' 123: 80001000 64 FUNC GLOBAL DEFAULT 2 {SYMBOL}\n'
LINES = """C:/project/Program.cs:
File name Line number Starting address View Stmt
Program.cs 7 0x80001000 x
Program.cs 7 0x80001008 x
Program.cs 8 0x8000100c x
Program.cs 8 0x8000100c 1 x
Program.cs 8 0x80001100 x
Program.cs - 0x80001040
"""


class AddressResolution(unittest.TestCase):
    def test_deduplication_multiple_pcs_and_native_function_bounds(self):
        points = module.resolve(MAP, LINES, SYMBOLS)['methods'][0]['breakpoints']
        self.assertEqual(points[0]['addresses'], ['0x80001000', '0x80001008'])
        self.assertEqual(points[1]['addresses'], ['0x8000100c'])
        self.assertEqual(points[1]['ilOffsets'], [5, 6])

    def test_invalid_addresses_and_end_sequence_ignored(self):
        self.assertEqual(module.parse_lines(
            'Program.cs 7 0\nProgram.cs 7 0x80001001\nProgram.cs - 0x8000100c\n'), [])

    def test_incompatible_sidecar_and_missing_elf_symbols_fail(self):
        with self.assertRaisesRegex(ValueError, 'schemaVersion 2'):
            module.resolve({**MAP, 'schemaVersion': 1}, LINES, SYMBOLS)
        with self.assertRaisesRegex(ValueError, 'No generated native function'):
            module.resolve(MAP, LINES, '')

    def test_ambiguous_source_names_rejected(self):
        ambiguous = json.loads(json.dumps(MAP))
        ambiguous['methods'][0]['sourceLocations'].append(
            {'document': '/other/Program.cs', 'startLine': 7, 'ilOffset': 10})
        with self.assertRaisesRegex(ValueError, 'Ambiguous DWARF basename'):
            module.resolve(ambiguous, LINES.replace('C:/project/Program.cs:\n', ''), SYMBOLS)

    def test_native_method_extent(self):
        method = module.resolve(MAP, LINES, SYMBOLS)['methods'][0]
        self.assertEqual((method['nativeStart'], method['nativeSize']), ('0x80001000', 64))

    def test_powerpc_cfi_prologue_body_and_epilogue(self):
        frames = """Contents of the .debug_frame section:
00000000 00000010 ffffffff CIE "" cf=1 df=-4 ra=65
   LOC   CFA  r31 ra
00000000 r1+0 u s
00000014 00000030 00000000 FDE cie=00000000 pc=80001000..80001030
   LOC   CFA    r31 ra
80001000 r1+0   u   s
80001004 r1+32  u   s
8000100c r31+32 c-4 c+4
80001028 r1+0   s   s
00000048 00000010 00000000 FDE cie=00000000 pc=80001100..80001110
"""
        rows = module.parse_unwind(frames)
        self.assertEqual(len(rows), 5)
        self.assertEqual(rows[0]['registers']['65']['kind'], 'same')
        self.assertEqual(rows[2]['cfaRegister'], 31)
        self.assertEqual(rows[2]['registers']['65'], {'kind': 'memory', 'offset': 4})
        self.assertEqual(rows[2]['end'], '0x80001028')
        self.assertEqual(rows[4]['cfaOffset'], 0)  # CIE inheritance for leaf FDE

    def test_unsupported_cfi_never_extends_previous_rule(self):
        frames = """00000000 00000010 ffffffff CIE "" cf=1 df=-4 ra=65
   LOC CFA ra
00000000 r1+0 s
00000014 00000030 00000000 FDE cie=00000000 pc=80001000..80001010
   LOC CFA ra
80001000 r1+0 s
80001008 exp exp
"""
        rows = module.parse_unwind(frames)
        self.assertEqual(rows[0]['end'], '0x80001008')
        self.assertFalse(rows[1]['supported'])

    def test_register_saved_lr_and_unknown_cie(self):
        frames = """00000000 00000010 ffffffff CIE "" cf=1 df=-4 ra=65
   CFA ra
   r1+0 s
00000014 00000030 00000000 FDE cie=00000000 pc=80001000..80001010
   LOC CFA ra
80001000 r1+0 r0
00000048 00000010 00000000 FDE cie=000000ff pc=80002000..80002010
   LOC CFA ra
80002000 r1+0 s
"""
        rows = module.parse_unwind(frames)
        self.assertEqual(rows[0]['registers']['65'], {'kind': 'register', 'register': 0})
        self.assertFalse(rows[1]['supported'])

    def test_non_powerpc_cie_is_not_guessed(self):
        rows = module.parse_unwind("""00000000 00000010 00000000 CIE "zR" cf=1 df=-8 ra=16
   LOC CFA ra
00000000 rsp+8 c-8
00000014 00000010 00000000 FDE cie=00000000 pc=00001000..00001010
""")
        self.assertFalse(rows[0]['supported'])

    def test_real_gcc_dwarf_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            source, elf = folder / 'fixture.c', folder / 'fixture.elf'
            source.write_text('int ' + SYMBOL + '(void) {\n'
                              '#line 7 "/project/Program.cs"\n'
                              ' volatile int value = 1;\n'
                              '#line 8 "/project/Program.cs"\n'
                              ' value += 1; return value;\n'
                              '}\nint main(void) {return ' + SYMBOL + '();}\n')
            subprocess.run(['cc', '-Og', '-g3', '-gdwarf-4', '-falign-functions=16',
                            str(source), '-o', str(elf)], check=True)
            line_dump = subprocess.check_output(
                ['readelf', '--debug-dump=decodedline', str(elf)], text=True)
            symbol_dump = subprocess.check_output(['readelf', '-Ws', str(elf)], text=True)
            data = json.loads(json.dumps(MAP))
            for item in data['methods'][0]['sourceLocations']:
                item['document'] = '/project/Program.cs'
            self.assertTrue(module.resolve(data, line_dump, symbol_dump)['methods'][0]['breakpoints'])


if __name__ == '__main__':
    unittest.main()
