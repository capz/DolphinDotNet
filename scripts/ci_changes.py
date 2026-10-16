#!/usr/bin/env python3
"""Select CI job groups from the complete PR diff; unknown inputs run all checks."""
import json
import os
from pathlib import Path
import re
import subprocess

GROUPS = ("debugger", "rider", "managed", "ide", "portable", "gamecube", "dolphin")
BUILD = {"managed", "ide", "gamecube", "dolphin"}


def select(paths):
    selected = set()
    for path in paths:
        if path.startswith("docs/") or path in {"README.md", "SECURITY.md", "LICENSE"}:
            continue
        if path.startswith("plugins/rider/"):
            selected.add("rider")
        elif path.startswith("tests/DebuggerTests/") or path == "tests/test_debug_adapter.py":
            selected.add("debugger")
        elif path.startswith(("tools/DolphinDotNet.Debugger/", "tools/DolphinDotNet.Debugger.Probe/", "tools/DolphinDotNet.DebugAdapter/")) or path == "scripts/dnd_dap_probe.py":
            selected.update({"debugger", "rider", "gamecube", "dolphin"})
        elif path in {"scripts/dolphin_smoke.py", "scripts/dolphin_gdb_probe.py", "tests/test_dolphin_smoke.py"}:
            selected.update({"gamecube", "dolphin"})
        elif path.startswith("tests/DolphinLaunchProbe/") or path == "tests/build_workflow_test.py":
            selected.add("ide")
        elif path.startswith(("source/", "include/", "vendor/")) or path in {"Makefile", "tests/Makefile"}:
            selected.update(BUILD | {"portable"})
        elif path.startswith(("src/", "sdk/", "samples/", "tools/DolphinDotNet.Compiler/",
                              "tools/DolphinDotNet.Compiler.Tests/", "tools/DolphinDotNet.Build/",
                              "tools/DolphinDotNet.ApiCompat/", "tools/DolphinDotNet.Assets/",
                              "tools/DolphinDotNet.Imaging/", "tools/DolphinDotNet.SaveImage/", "tools/gx-bindings/")):
            selected.update(BUILD)
        else:
            # Includes CI configuration/selector, unknown tests, toolchain files,
            # new directories and shared scripts. Extend rules only after auditing consumers.
            selected.update(GROUPS)
    # Dolphin always consumes artifacts built in this run; never stale cached DOLs.
    if "dolphin" in selected:
        selected.add("gamecube")
    return selected


def changed_paths(event):
    pr = event["pull_request"]
    base, head = pr["base"]["sha"], pr["head"]["sha"]
    if not all(isinstance(sha, str) and re.fullmatch(r"[0-9a-fA-F]{40,64}", sha) for sha in (base, head)):
        raise ValueError("Invalid comparison commit")
    # Three-dot compares the PR head against its merge base. No rename detection
    # means both old and new paths are considered, including deleted dependencies.
    raw = subprocess.check_output(["git", "diff", "--name-only", "--no-renames", "-z", f"{base}...{head}", "--"])
    return [path.decode("utf-8", errors="strict") for path in raw.split(b"\0") if path]


def selection(event_name, event):
    if event_name != "pull_request":
        return set(GROUPS), "Full validation for main/manual/other events"
    try:
        paths = changed_paths(event)
        return select(paths), f"Selected from {len(paths)} changed PR paths"
    except (KeyError, TypeError, ValueError, OSError, subprocess.SubprocessError) as error:
        return set(GROUPS), f"Comparison unavailable; run everything ({type(error).__name__})"


def main():
    try:
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
        selected, reason = selection(os.environ.get("GITHUB_EVENT_NAME", ""), event)
    except (KeyError, ValueError, OSError):
        selected, reason = set(GROUPS), "Event unavailable; run everything"
    lines = [f"{group}={str(group in selected).lower()}" for group in GROUPS]
    print(reason)
    print("\n".join(lines))
    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a") as stream:
            stream.write("\n".join(lines) + "\n")
    if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(summary, "a") as stream:
            stream.write("### CI selection\n\n" + reason + "\n\n| Group | Run |\n|---|---|\n")
            for group in GROUPS:
                stream.write(f"| {group} | {'yes' if group in selected else 'skip'} |\n")


if __name__ == "__main__":
    main()
