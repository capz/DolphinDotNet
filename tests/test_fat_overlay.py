"""Pinned-source and fail-closed preparation checks; no network or compiler."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('fat_overlay', ROOT / 'scripts/prepare_fat_overlay.py')
OVERLAY = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(OVERLAY)


class FatOverlayPreparationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='dnd-fat-overlay-test-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / 'upstream'
        for folder in ('include', 'source', 'fatfs/source'):
            (self.source / folder).mkdir(parents=True, exist_ok=True)
        for name in ('COPYING', 'include/dvm.h', 'include/fat.h',
                     'source/dvm_debug.h', 'source/fat_driver.h'):
            (self.source / name).write_text('pinned fixture\n')
        for module in OVERLAY.DVM_MODULES + OVERLAY.FAT_MODULES:
            (self.source / 'source' / (module + '.c')).write_text('/* pinned C */\n')
        for name in ('ffunicode.c', 'diskio.h'):
            (self.source / 'fatfs/source' / name).write_text('/* pinned FatFs */\n')
        (self.source / 'fatfs/source/ffconf.h').write_text('#define FF_PATH_DEPTH\t10\n')
        (self.source / 'fatfs/source/ff.h').write_text(
            '\tFR_INVALID_PARAMETER\t/* (19) Given parameter is invalid */\n')
        (self.source / 'fatfs/source/ff.c').write_text(
            '\t\t\t\tif (++fs->xcwds2.depth >= sizeof fs->xcwds2.tbl / sizeof fs->xcwds2.tbl[0]) {\t/* Is it too deep path? */\n'
            '\t\t\t\t\tres = FR_NOT_ENOUGH_CORE; break;\n')
        (self.source / 'source/fat_driver.c').write_text(
            '\t\t[FR_NOT_ENOUGH_CORE]     = ENOMEM,\n'
            '\t\t[FR_INVALID_PARAMETER]   = EINVAL,\n')

    def expected_git(self, folder, *arguments):
        if arguments[0] == 'status':
            return ''
        return OVERLAY.FATFS_REVISION if Path(folder).name == 'fatfs' else OVERLAY.LIBDVM_REVISION

    def prepare(self, output=None):
        with patch.object(OVERLAY, 'git', side_effect=self.expected_git):
            return OVERLAY.prepare(self.source, output or self.root / 'prepared')

    def test_matching_driver_and_fatfs_prepared_without_changing_checkout(self):
        originals = {p.relative_to(self.source): p.read_bytes() for p in self.source.rglob('*') if p.is_file()}
        result = self.prepare()
        self.assertIn('#define FF_PATH_DEPTH\t64', (result / 'fatfs/source/ffconf.h').read_text())
        self.assertIn('res = FR_PATH_TOO_DEEP;', (result / 'fatfs/source/ff.c').read_text())
        self.assertIn('[FR_PATH_TOO_DEEP]       = ENAMETOOLONG', (result / 'source/fat_driver.c').read_text())
        self.assertIn('[FR_NOT_ENOUGH_CORE]     = ENOMEM', (result / 'source/fat_driver.c').read_text())
        for name, contents in originals.items():
            self.assertEqual((self.source / name).read_bytes(), contents)

    def test_wrong_revision_rejected_before_output_is_replaced(self):
        output = self.root / 'prepared'; output.mkdir(); (output / 'keep').write_text('previous valid output')
        with patch.object(OVERLAY, 'git', return_value='wrong'), self.assertRaises(ValueError):
            OVERLAY.prepare(self.source, output)
        self.assertEqual((output / 'keep').read_text(), 'previous valid output')

    def test_tracked_changes_in_either_checkout_are_rejected(self):
        for dirty in (self.source, self.source / 'fatfs'):
            def git(folder, *arguments):
                if arguments[0] == 'status' and Path(folder) == dirty:
                    return ' M source/ff.c'
                return self.expected_git(folder, *arguments)
            with self.subTest(folder=dirty), patch.object(OVERLAY, 'git', side_effect=git), self.assertRaises(ValueError):
                OVERLAY.prepare(self.source, self.root / 'prepared')

    def test_unexpected_patch_context_fails_closed(self):
        (self.source / 'fatfs/source/ffconf.h').write_text('#define FF_PATH_DEPTH\t12\n')
        with self.assertRaises(ValueError):
            self.prepare()
        self.assertEqual((self.source / 'fatfs/source/ffconf.h').read_text(), '#define FF_PATH_DEPTH\t12\n')

    def test_untracked_source_is_not_copied_or_compiled(self):
        (self.source / 'source/untracked.c').write_text('#error must not compile\n')
        result = self.prepare()
        self.assertFalse((result / 'source/untracked.c').exists())

    def test_output_cannot_overlap_input_checkout(self):
        for output in (self.source, self.source / 'build', self.source.parent):
            with self.subTest(output=output), self.assertRaises(ValueError):
                self.prepare(output)
        self.assertTrue((self.source / 'COPYING').is_file())


if __name__ == '__main__':
    unittest.main()
