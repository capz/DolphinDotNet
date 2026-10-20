"""Ensure CI release packaging reuses existing sample binaries safely."""

import importlib.util
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "scripts/package_sample_dols.py"
spec = importlib.util.spec_from_file_location("package_sample_dols", MODULE_PATH)
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class SampleReleaseTests(unittest.TestCase):
    def test_one_zip_per_sample_with_predictable_dol_name(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = root / "Game.dol"
            second = root / "Pong.dol"
            first.write_bytes(b"GameCube sample payload 1")
            second.write_bytes(b"GameCube sample payload 2")
            output = root / "archives"

            archives = packager.package_samples(
                output, [f"GreenMen={first}", f"Pong={second}"]
            )
            self.assertEqual([output / "GreenMen.zip", output / "Pong.zip"], archives)
            for name, payload in (("GreenMen", first), ("Pong", second)):
                with ZipFile(output / f"{name}.zip") as archive:
                    self.assertEqual([f"{name}.dol"], archive.namelist())
                    self.assertEqual(payload.read_bytes(), archive.read(f"{name}.dol"))

    def test_missing_or_empty_dol_does_not_create_partial_archives(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "valid.dol"
            source.write_bytes(b"valid")
            empty = root / "empty.dol"
            empty.touch()
            for invalid in (root / "missing.dol", empty, root / "bad.elf"):
                with self.subTest(invalid=invalid):
                    out = root / "archives"
                    with self.assertRaises(ValueError):
                        packager.package_samples(out, [f"Valid={source}", f"Invalid={invalid}"])
                    self.assertFalse(out.exists())

    def test_duplicate_and_unsafe_sample_names_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "Game.dol"
            source.write_bytes(b"valid")
            for samples in ([], [f"Pong={source}", f"Pong={source}"],
                            [f"../outside={source}"], [f"No extension={source}"],
                            [f"InvalidOnly{source}"]):
                with self.subTest(samples=samples):
                    with self.assertRaises(ValueError):
                        packager.package_samples(root / "archives", samples)


if __name__ == "__main__":
    unittest.main()
