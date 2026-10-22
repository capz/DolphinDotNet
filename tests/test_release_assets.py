"""Exercise release asset selection for independently selected extension builds."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('release_assets', Path(__file__).resolve().parents[1] / 'scripts/release_assets.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseAssetsTests(unittest.TestCase):
    def test_extension_only_release_and_empty_selection(self):
        with self.assertRaisesRegex(ValueError, 'At least one'):
            release.collect_assets()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            rider, vs = root / 'rider', root / 'vs'
            rider.mkdir(); vs.mkdir()
            plugin, extension = rider / 'plugin.zip', vs / 'plugin.vsix'
            plugin.write_bytes(b'rider'); extension.write_bytes(b'vs')
            self.assertEqual([plugin], release.collect_assets(rider_dir=rider))
            self.assertEqual([extension], release.collect_assets(visual_studio_dir=vs))
            self.assertEqual([plugin, extension], release.collect_assets(rider_dir=rider, visual_studio_dir=vs))

    def test_all_extension_selection_combinations(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            samples = root / 'samples.zip'
            samples.write_bytes(b'samples')
            rider = root / 'rider'
            vs = root / 'vs'
            rider.mkdir(); vs.mkdir()
            plugin = rider / 'plugin.zip'
            extension = vs / 'plugin.vsix'
            plugin.write_bytes(b'rider'); extension.write_bytes(b'vs')
            for want_rider in (False, True):
                for want_vs in (False, True):
                    with self.subTest(rider=want_rider, vs=want_vs):
                        expected = [samples] + ([plugin] if want_rider else []) + ([extension] if want_vs else [])
                        self.assertEqual(expected, release.collect_assets(samples, rider if want_rider else None, vs if want_vs else None))

    def test_selected_missing_empty_or_ambiguous_package_fails(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            samples = root / 'samples.zip'
            samples.write_bytes(b'samples')
            for suffix, keyword in (('.zip', 'rider_dir'), ('.vsix', 'visual_studio_dir')):
                folder = root / suffix[1:]
                folder.mkdir()
                with self.assertRaises(ValueError):
                    release.collect_assets(samples, **{keyword: folder})
                first = folder / ('one' + suffix)
                first.touch()
                with self.assertRaises(ValueError):
                    release.collect_assets(samples, **{keyword: folder})
                first.write_bytes(b'one')
                (folder / ('two' + suffix)).write_bytes(b'two')
                with self.assertRaises(ValueError):
                    release.collect_assets(samples, **{keyword: folder})

    def test_missing_samples_or_duplicate_asset_names_fail(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            samples = root / 'samples.zip'
            with self.assertRaises(ValueError):
                release.collect_assets(samples)
            samples.write_bytes(b'samples')
            rider = root / 'rider'
            rider.mkdir()
            (rider / samples.name).write_bytes(b'plugin')
            with self.assertRaises(ValueError):
                release.collect_assets(samples, rider)


if __name__ == '__main__':
    unittest.main()
