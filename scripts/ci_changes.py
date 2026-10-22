#!/usr/bin/env python3
"""Conservative, component-scoped CI selection from the entire PR diff.

An unrecognized/deleted path fails *open* to full validation. Matrix groups
are actual tests, not just managed builds of external sample projects.
"""
import json
import os
from pathlib import Path
import re
import subprocess

MATRIX = ("native", "compiler", "storage", "network", "audio", "graphics", "assets", "sdk", "gamecube")
GROUPS = MATRIX + ("debugger", "ide")
ALL = set(GROUPS)

# Shared runtime and compiler changes fan out to their downstream consumers.
RUNTIME = {"native", "compiler", "storage", "network", "audio", "graphics", "gamecube"}
API = {"compiler", "storage", "network", "audio", "graphics", "assets", "sdk", "gamecube"}
COMPILER = {"compiler", "storage", "network", "audio", "graphics", "gamecube", "sdk"}
DOMAIN = {
    "audio": {"native", "audio", "gamecube"},
    "network": {"native", "network", "gamecube"},
    "socket": {"native", "network", "gamecube"},
    "storage": {"native", "storage", "gamecube"},
    "fs": {"native", "storage", "gamecube"},
    "disc": {"native", "storage", "gamecube"},
    "fst": {"native", "storage", "gamecube"},
    "card": {"native", "storage", "gamecube"},
    "aram": {"native", "storage", "graphics", "gamecube"},
    "texture": {"native", "graphics", "gamecube"},
    "graphics": {"native", "graphics", "gamecube"},
    "draw": {"native", "graphics", "gamecube"},
    "model": {"native", "graphics", "assets", "gamecube"},
    "png": {"native", "graphics", "assets", "gamecube"},
    "input": {"native", "storage", "gamecube"},
    "hardware": {"native", "storage", "gamecube"},
    "debug": {"native", "debugger", "gamecube", "ide"},
}
TEST_DOMAINS = {
    "compiler": "compiler", "aot": "compiler", "controlflow": "compiler",
    "genericsharing": "compiler", "numeric": "compiler",
    "string": "compiler", "wishlist": "compiler", "exactnumber": "compiler",
    "storage": "storage", "saveimage": "assets", "disc": "storage",
    "aram": "storage", "library": "storage", "connection": "storage",
    "input": "storage", "timing": "network", "async": "network",
    "automatic": "network", "network": "network", "audio": "audio",
    "pong": "audio", "snake": "storage", "kart": "audio",
    "greenmen": "audio", "fox": "audio", "graphics": "graphics",
    "model": "graphics", "effect": "graphics", "tev": "graphics",
    "sampledownload": "network", "samplerecords": "storage",
}

def select(paths):
    selected = set()
    for path in paths:
        if path.startswith("docs/") or path in {"README.md", "SECURITY.md", "LICENSE"}:
            continue
        if path in {"eng/debugger-source.json", "eng/prepare_debugger.py", "eng/check_debugger.py"}:
            selected.update({"debugger", "sdk", "ide"})
        elif path.startswith(("source/debug_", "include/dnd_debug")) or path == "scripts/dnd_debug_addresses.py":
            selected.update({"native", "debugger", "gamecube", "ide"})
        elif path.startswith(("sdk/", "templates/")) or path in {"eng/pack.py", "eng/Package.props"}:
            selected.update({"sdk", "compiler", "ide", "gamecube"})
        elif path.startswith("tools/DolphinDotNet.Compiler/") or path.startswith("tools/DolphinDotNet.Compiler.Tests/"):
            selected.update(COMPILER)
        elif path.startswith("tools/DolphinDotNet.Build/") or path.startswith("tests/DolphinLaunchProbe/"):
            selected.update({"sdk", "ide", "gamecube"})
        elif path.startswith(("tools/DolphinDotNet.Assets/", "tools/DolphinDotNet.Imaging/",
                              "tools/DolphinDotNet.SaveImage/", "tools/DolphinDotNet.Effects/")):
            selected.update({"assets", "graphics", "audio", "sdk"})
        elif path.startswith("src/DolphinDotNet.Core/"):
            selected.update(API | {"native"})
        elif path.startswith("src/DolphinDotNet.GameCube/"):
            lower = path.lower()
            matched = [values for name, values in DOMAIN.items() if name in lower]
            selected.update(set().union(*matched) | {"compiler", "sdk"} if matched else API)
        elif path.startswith(("source/", "include/")):
            lower = path.lower()
            if any(name in lower for name in ("managed_runtime", "async_runtime", "numeric",
                                               "text.c", "bcl", "stw", "worker", "gc_", "main.c")):
                selected.update(RUNTIME)
            else:
                matched = [values for name, values in DOMAIN.items() if name in lower]
                selected.update(set().union(*matched) if matched else RUNTIME)
        elif path.startswith("vendor/") or path in {"Makefile", "tests/Makefile"}:
            selected.update({"native", "assets", "graphics", "gamecube"})
        elif path.startswith("samples/"):
            # Covers the old path in PRs that remove local examples.
            selected.update({"ide", "sdk", "gamecube"})
        elif path.startswith("tests/HelloGameCube/") or path.startswith("tests/NativeConsumerFixture/"):
            selected.update({"compiler", "gamecube", "sdk"})
        elif path.startswith("tests/fixtures/"):
            selected.update({"graphics", "assets", "sdk"})
        elif path.startswith("tests/"):
            relative = path[6:].lower().replace("_", "")
            if relative.startswith(("testci", "citest", "testworkflow", "testsample",
                                    "testdebuggerdependency")):
                selected.update(ALL)
            elif relative.startswith(("testdolphin", "dolphin")):
                selected.update({"gamecube", "ide", "debugger"})
            else:
                matched = [group for name, group in TEST_DOMAINS.items() if name in relative]
                if matched:
                    selected.update(matched)
                    if any(part in relative for part in ("host.c", "native.c", "gamecube.c", "sdk/")):
                        selected.update({"native", "gamecube"})
                else:
                    selected.update(ALL)
        elif path.startswith(".github/workflows/") or path in {"scripts/ci_changes.py", "scripts/ci_test_suite.py"}:
            selected.update(ALL)
        elif path.startswith("scripts/"):
            if "model" in path or "png" in path:
                selected.update({"assets", "graphics", "gamecube"})
            elif "network" in path:
                selected.update({"network", "gamecube"})
            elif "debug" in path:
                selected.update({"debugger", "ide", "gamecube"})
            elif path in {"scripts/validate_sample_build.py", "scripts/package_sample_dols.py"}:
                selected.update({"ide", "gamecube"})
            else:
                selected.update(ALL)
        else:
            selected.update(ALL)
    return selected

def changed_paths(event):
    pr = event["pull_request"]
    base, head = pr["base"]["sha"], pr["head"]["sha"]
    if not all(isinstance(sha, str) and re.fullmatch(r"[0-9a-fA-F]{40,64}", sha) for sha in (base, head)):
        raise ValueError("Invalid comparison commit")
    raw = subprocess.check_output(["git", "diff", "--name-only", "--no-renames",
                                   "-z", f"{base}...{head}", "--"])
    return [p.decode("utf-8") for p in raw.split(b"\0") if p]

def selection(event_name, event):
    if event_name != "pull_request":
        return set(ALL), "Full validation for main/manual/other events"
    try:
        paths = changed_paths(event)
        return select(paths), f"Selected from {len(paths)} changed PR paths"
    except (KeyError, TypeError, ValueError, OSError, subprocess.SubprocessError) as exc:
        return set(ALL), f"Diff unavailable; run all tests ({type(exc).__name__})"

def main():
    try:
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
        selected, reason = selection(os.environ.get("GITHUB_EVENT_NAME", ""), event)
    except (KeyError, ValueError, OSError):
        selected, reason = set(ALL), "Event unavailable; run all tests"
    outputs = {key: str(key in selected).lower() for key in ("debugger", "ide")}
    outputs["framework"] = str(bool(set(MATRIX) & selected)).lower()
    outputs["matrix"] = json.dumps([key for key in MATRIX if key in selected], separators=(",", ":"))
    print(reason, json.dumps(outputs, sort_keys=True))
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as f:
            for key, value in outputs.items():
                f.write(f"{key}={value}\n")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as f:
            f.write("### Component-scoped CI\n\n" + reason + "\n\n")
            f.write("| Area | Selected |\n|---|---|\n")
            for key in GROUPS:
                f.write(f"| {key} | {'yes' if key in selected else 'no'} |\n")

if __name__ == "__main__":
    main()
