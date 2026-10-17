"""Native PowerPC address resolver tests plus a real host GCC DWARF fixture."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
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


class VariableLocations(unittest.TestCase):
    def test_debugger_telemetry_is_optional_and_requires_one_exact_symbol(self):
        source = dict(debuggerTelemetry=dict(version=1, size=8, nativeSymbol='dnd_debugger_telemetry'))
        mapped = {}
        module.attach_debugger_telemetry(mapped, source, '')
        self.assertNotIn('debuggerTelemetry', mapped)
        symbols = '1: 80002000 8 OBJECT GLOBAL DEFAULT 2 dnd_debugger_telemetry\n'
        module.attach_debugger_telemetry(mapped, source, symbols)
        self.assertEqual(dict(version=1, size=8, address='0x80002000'), mapped['debuggerTelemetry'])
        with self.assertRaisesRegex(ValueError, 'Ambiguous debugger telemetry'):
            module.attach_debugger_telemetry({}, source, symbols + '2: 80002008 8 OBJECT GLOBAL DEFAULT 2 dnd_debugger_telemetry\n')
        with self.assertRaisesRegex(ValueError, 'Invalid debugger telemetry'):
            module.attach_debugger_telemetry({}, source, symbols.replace(' 8 OBJECT', ' 16 OBJECT'))

    def test_inspection_only_binds_linked_object_symbols(self):
        source = dict(inspection=dict(version=1, pointerSize=4, objectHeaderSize=8, stringLengthOffset=8, stringDataOffset=12,
            types=[dict(name='Box', nativeSymbol='dnd_type_Box', kind='object', size=32, fields=[]),
                   dict(name='Discarded', nativeSymbol='dnd_type_Discarded', kind='object', size=8, fields=[])]))
        mapped = {}
        symbols = ('1: 80002000 48 OBJECT GLOBAL DEFAULT 2 dnd_type_Box\n2: 00000000 48 OBJECT GLOBAL DEFAULT UND dnd_type_Discarded\n'
                   '3: 80003000 48 OBJECT LOCAL DEFAULT 2 ResetInfo\n4: 80004000 48 OBJECT LOCAL DEFAULT 2 ResetInfo\n')
        module.attach_inspection(mapped, source, symbols)
        self.assertEqual(['Box'], [t['name'] for t in mapped['inspection']['types']])
        self.assertEqual('0x80002000', mapped['inspection']['types'][0]['address'])
        with self.assertRaisesRegex(ValueError, 'Ambiguous managed type'):
            module.attach_inspection({}, source, symbols + '3: 80003000 48 OBJECT GLOBAL DEFAULT 2 dnd_type_Box\n')
        source['inspection']['pointerSize'] = 8
        with self.assertRaisesRegex(ValueError, 'Unsupported managed inspection ABI'):
            module.attach_inspection({}, source, symbols)

    def test_native_dwarf_locations_and_unsupported_expressions(self):
        text = f"""
 <1><10>: Abbrev Number: 3 (DW_TAG_subprogram)
    <11> DW_AT_name : (indirect string, offset: 0x10): {SYMBOL}
    <12> DW_AT_low_pc : 0x80001000
    <13> DW_AT_frame_base : 1 byte block: 9c (DW_OP_call_frame_cfa)
 <2><20>: Abbrev Number: 4 (DW_TAG_formal_parameter)
    <21> DW_AT_name : a0
    <22> DW_AT_location : 2 byte block: 91 7c (DW_OP_fbreg: -4)
 <2><30>: Abbrev Number: 5 (DW_TAG_variable)
    <31> DW_AT_name : l0
    <32> DW_AT_location : 1 byte block: 53 (DW_OP_reg3 (r3))
 <2><40>: Abbrev Number: 5 (DW_TAG_variable)
    <41> DW_AT_name : l1
    <42> DW_AT_location : 3 byte block (DW_OP_fbreg: -8; DW_OP_deref)
 <2><50>: Abbrev Number: 0
"""
        result = module.parse_variable_locations(text)[SYMBOL]
        self.assertEqual(dict(kind='cfaOffset', offset=-4), result['a0'])
        self.assertEqual(dict(kind='register', register=3), result['l0'])
        self.assertEqual('unavailable', result['l1']['kind'])

    def test_scope_crossing_same_source_line_is_not_guessed(self):
        source = json.loads(json.dumps(MAP))
        source['methods'][0]['variables'] = [dict(nativeName='l0', name='first', type='System.Int32',
            group='Locals', storageBytes=4, scopeStart=5, scopeEnd=6)]
        mapped = module.resolve(source, LINES, SYMBOLS)
        module.attach_variables(mapped, source, '')
        value = mapped['methods'][0]['variables'][0]
        self.assertEqual([], value['visibleAddresses'])
        self.assertEqual('unavailable', value['location']['kind'])

    def test_location_lists_split_views_and_missing_or_ambiguous_ranges(self):
        source = json.loads(json.dumps(MAP))
        source['methods'][0]['variables'] = [dict(nativeName='a0', name='value', type='System.Int32',
            group='Arguments', storageBytes=4, scopeStart=0, scopeEnd=100)]
        info = f"""
 <1><10>: Abbrev Number: 3 (DW_TAG_subprogram)
    <11> DW_AT_name : {SYMBOL}
    <12> DW_AT_low_pc : 0x80001000
    <13> DW_AT_frame_base : 1 byte block: 9c (DW_OP_call_frame_cfa)
 <2><20>: Abbrev Number: 4 (DW_TAG_formal_parameter)
    <21> DW_AT_name : a0
    <22> DW_AT_location : 0x10 (location list)
"""
        locations = """
    00000010 v0000000 v0000001 views at 00000000 for:
             80001000 80001004 (DW_OP_reg3 (r3))
    00000018 v0000000 v0000001 views at 00000002 for:
             80001008 80001010 (DW_OP_fbreg: -8)
    00000020 v0000000 v0000001 views at 00000004 for:
             8000100c 80001010 (DW_OP_reg4 (r4))
    00000028 <End of list>
"""
        mapped = module.resolve(source, LINES, SYMBOLS)
        module.attach_variables(mapped, source, info, locations)
        values = {pc: v['location'] for v in mapped['methods'][0]['variables'] for pc in v['visibleAddresses']}
        self.assertEqual(dict(kind='register', register=3), values['0x80001000'])
        self.assertEqual(dict(kind='cfaOffset', offset=-8), values['0x80001008'])
        self.assertEqual('unavailable', values['0x8000100c']['kind'])
        # A range never leaks across the terminating entry or fills a gap.
        self.assertEqual(3, len(module.parse_location_lists(locations)[0x10]))
        self.assertEqual({}, module.parse_location_lists('80001000 80001004 (DW_OP_reg3 (r3))'))

    def test_real_gcc_volatile_slot_has_a_frame_relative_location(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            c = root / 'value.c'
            c.write_text('int ' + SYMBOL + '(volatile int a0) { volatile int l0 = a0 + 1; return l0; }')
            subprocess.run(['cc', '-Og', '-gdwarf-4', '-fno-omit-frame-pointer', '-c', str(c), '-o', str(root / 'value.o')], check=True)
            dump = subprocess.check_output(['readelf', '--wide', '--debug-dump=info', str(root / 'value.o')], text=True)
            values = module.parse_variable_locations(dump)[SYMBOL]
            self.assertEqual('unavailable', values['a0']['kind'])  # x86-64 register annotations are not PPC register locations
            self.assertEqual('cfaOffset', values['l0']['kind'])


class AddressResolution(unittest.TestCase):
    def test_cli_preserves_long_windows_pdb_paths_in_real_dwarf(self):
        document = (r'D:\DolphinDotNet-feat-debugger-reliability-inspection-20261016'
                    r'\samples\Snake\Program.cs')
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            c = directory / 'windows.c'
            c.write_text('#line 10 ' + json.dumps(document) + '\n'
                         f'__attribute__((aligned(4))) int {SYMBOL}(volatile int a) {{\n'
                         ' volatile int b=a+1;\n return b;\n}\n'
                         f'int main(void) {{return {SYMBOL}(2);}}\n')
            binary = directory / 'windows'
            subprocess.run(['cc', '-O0', '-gdwarf-4', '-fno-omit-frame-pointer',
                            str(c), '-o', str(binary)], check=True)
            source = json.loads(json.dumps(MAP))
            source['methods'][0]['sourceLocations'] = [
                dict(document=document, startLine=line, ilOffset=i)
                for i, line in enumerate((10, 11, 12))]
            source_path = directory / 'source.json'
            source_path.write_text(json.dumps(source))
            addresses = directory / 'addresses.json'
            result = subprocess.run([sys.executable, str(root / 'scripts/dnd_debug_addresses.py'),
                                     '--elf', str(binary), '--source-map', str(source_path),
                                     '--output', str(addresses), '--require-source', document],
                                    capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            points = json.loads(addresses.read_text())['methods'][0]['breakpoints']
            self.assertTrue(points)
            self.assertTrue(all(p['document'] == document and p['addresses'] for p in points))

    def test_equal_pc_views_keep_only_the_row_with_instruction_extent(self):
        # Reduced from CI run 38930010375: line 9 has an empty range at
        # 0x80005168, while the final line-10 view owns the instruction.
        rows = module.parse_lines('''/__w/samples/ManagedGameCube/Program.cs:
Program.cs 9 0x80005160 x
Program.cs 9 0x80005168
Program.cs 10 0x80005168 1 x
Program.cs 10 0x80005168 2 x
./generated_program.c:[++]
generated_program.c 1 0x80005168 3 x
/__w/samples/ManagedGameCube/Program.cs:
Program.cs 10 0x80005168 4
./generated_program.c:[++]
generated_program.c 2 0x8000516c x
generated_program.c - 0x80005170
''')
        self.assertEqual([r for r in rows if r[2] == 0x80005168],
                         [('/__w/samples/managedgamecube/program.cs', 10, 0x80005168)])
        self.assertNotIn(('/__w/samples/managedgamecube/program.cs', 9, 0x80005168), rows)

    def test_hidden_row_and_end_sequence_supersede_empty_source_views(self):
        rows = module.parse_lines('''/project/Program.cs:
Program.cs 7 0x80001000 x
generated_program.c 1 0x80001000 1 x
generated_program.c 2 0x80001004 x
Program.cs 8 0x80001008 x
Program.cs - 0x80001008
Program.cs 9 0x80001008 x
Program.cs - 0x8000100c
''')
        self.assertFalse(any(line in (7, 8) and file.endswith('program.cs') for file, line, _ in rows))
        self.assertIn(('/project/program.cs', 9, 0x80001008), rows)

    def test_overlapping_sequences_retain_real_ambiguity(self):
        rows = module.parse_lines('''/project/Program.cs:
Program.cs 7 0x80001000 x
Program.cs - 0x80001004
Program.cs 8 0x80001000 x
Program.cs - 0x80001004
''')
        self.assertEqual([line for _, line, _ in rows], [7, 8])

    def test_unterminated_row_has_no_proven_instruction_extent(self):
        self.assertEqual(module.parse_lines('Program.cs 7 0x80001000 x\n'), [])

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

    def test_linker_discarded_methods_have_no_invented_addresses(self):
        dead = SYMBOL + '_discarded'
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            c = directory / 'gc.c'
            c.write_text(f'int {SYMBOL}(void) {{return 1;}}\n'
                         f'int {dead}(void) {{return 2;}}\n'
                         f'int main(void) {{return {SYMBOL}();}}\n')
            binary = directory / 'gc'
            subprocess.run(['cc', '-O0', '-ffunction-sections', '-Wl,--gc-sections',
                            str(c), '-o', str(binary)], check=True)
            symbols = subprocess.check_output(['readelf', '-Ws', str(binary)], text=True)
            self.assertIn(SYMBOL, module.parse_symbols(symbols))
            self.assertNotIn(dead, module.parse_symbols(symbols))
            source = json.loads(json.dumps(MAP))
            source['methods'].append({**source['methods'][0], 'symbol': dead, 'methodToken': 100663298})
            mapped = module.resolve(source, '', symbols)
            live, discarded = mapped['methods']
            self.assertIn('nativeStart', live)
            self.assertEqual(dead, discarded['symbol'])
            self.assertEqual([], discarded['breakpoints'])
            self.assertNotIn('nativeStart', discarded)
            self.assertNotIn('nativeSize', discarded)
            self.assertIn('absent', discarded['unavailableReason'])

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
