"""Ensure the release contains one ZIP of pre-built GameCube sample DOLs."""

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
    def test_one_zip_contains_all_samples_with_unique_dol_names(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = root / "Game.dol"
            second = root / "Pong.dol"
            first.write_bytes(b"GameCube sample payload 1")
            second.write_bytes(b"GameCube sample payload 2")
            staged = root / "staged"

            files = packager.stage_samples(staged, [f"GreenMen={first}", f"Pong={second}"])
            self.assertEqual([staged / "GreenMen.dol", staged / "Pong.dol"], files)
            output = root / "DolphinDotNet-samples.zip"
            self.assertEqual(
                output, packager.bundle_samples(output, staged, ["GreenMen", "Pong"])
            )
            self.assertEqual([output], list(root.glob("*.zip")))
            with ZipFile(output) as archive:
                self.assertEqual(["GreenMen.dol", "Pong.dol"], archive.namelist())
                self.assertEqual(first.read_bytes(), archive.read("GreenMen.dol"))
                self.assertEqual(second.read_bytes(), archive.read("Pong.dol"))

    def test_missing_or_empty_dol_does_not_create_partial_staging(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "valid.dol"
            source.write_bytes(b"valid")
            empty = root / "empty.dol"
            empty.touch()
            for invalid in (root / "missing.dol", empty, root / "bad.elf"):
                with self.subTest(invalid=invalid):
                    out = root / "staged"
                    with self.assertRaises(ValueError):
                        packager.stage_samples(out, [f"Valid={source}", f"Invalid={invalid}"])
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
                        packager.stage_samples(root / "staged", samples)

    def test_combined_archive_requires_exact_nonempty_sample_set(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            staged = root / "staged"
            staged.mkdir()
            (staged / "Pong.dol").write_bytes(b"Pong")
            (staged / "Snake.dol").write_bytes(b"Snake")
            out = root / "release.zip"

            for expected in (["Pong"], ["Pong", "Snake", "Fox"], ["Pong", "Pong"]):
                with self.subTest(expected=expected):
                    with self.assertRaises(ValueError):
                        packager.bundle_samples(out, staged, expected)
                    self.assertFalse(out.exists())

            (staged / "Snake.dol").write_bytes(b"")
            with self.assertRaises(ValueError):
                packager.bundle_samples(out, staged, ["Pong", "Snake"])
            self.assertFalse(out.exists())


if __name__ == "__main__":
    unittest.main()
