"""Check IDE solution metadata and fresh parallel SDK builds without Docker."""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOLUTION_FOLDER = "2150E333-8FDC-42A3-9474-1A3956D46DE8"
SDK_CSHARP = "9A19103F-16F7-4668-BE54-9A1E7A4F7556"


def check_solution():
    entries = re.findall(
        r'^Project\("\{([^}]+)\}"\) = "[^"]+", "([^"]+)", "\{([^}]+)\}"',
        (ROOT / "DolphinDotNet.sln").read_text(), re.MULTILINE)
    assert entries, "Solution has no projects"
    projects = set()
    for kind, path, identity in entries:
        if path.endswith(".csproj"):
            assert kind == SDK_CSHARP, (path, kind)
            assert ROOT.joinpath(*path.split("\\")).is_file(), path
            projects.add(path)
        else:
            # Unknown folder types can hide all nested projects in an IDE even
            # when command-line MSBuild still builds the physical project files.
            assert kind == SOLUTION_FOLDER, (path, kind)
    selection = json.loads((ROOT / "TextureStrategy.slnf").read_text())["solution"]
    assert selection["path"] == "DolphinDotNet.sln", selection
    assert selection["projects"] and set(selection["projects"]) <= projects, selection


def check_builds(configuration):
    # Each entry point starts without bin/obj, so a previous build cannot hide
    # missing host-tool dependencies. Include spaces in the source path as well.
    with tempfile.TemporaryDirectory(prefix="dnd solution ") as temporary:
        for entry in ("DolphinDotNet.sln", "TextureStrategy.slnf"):
            checkout = Path(temporary) / Path(entry).stem / entry.rsplit(".", 1)[-1]
            shutil.copytree(ROOT, checkout, ignore=shutil.ignore_patterns(
                ".git", ".idea", ".vs", "bin", "obj", "build", "out", "__pycache__",
                "*.log", "DolphinDotNet.local.props"))
            # The driver validates both input DLLs before rejecting this backend.
            # Reaching DND001 proves BuildDol ran after both host tools, without
            # requiring Docker or a console toolchain on the CI runner.
            result = subprocess.run(
                ["dotnet", "build", entry, "--nologo", "-m:10", "-c", configuration,
                 "-p:DndBuildBackend=Invalid"], cwd=checkout, text=True,
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=300)
            expected = "DndBuildBackend must be Auto, Native or Docker."
            if result.returncode == 0 or "DND001" not in result.stdout or expected not in result.stdout:
                raise AssertionError(f"{entry}: host tools did not run correctly:\n{result.stdout}")
            for name in ("DolphinDotNet.Build", "DolphinDotNet.Compiler"):
                assert (checkout / "tools" / name / "bin" / configuration / "net8.0" / (name + ".dll")).is_file(), name
            print(f"{entry}: fresh parallel {configuration} build reached the native driver", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    arguments = parser.parse_args()
    check_solution()
    check_builds(arguments.configuration)
