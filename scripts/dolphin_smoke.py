#!/usr/bin/env python3
"""Boot a freshly built DOL in Dolphin and reject executable/loader diagnostics.

Exit 124 from GNU timeout is expected for a game that keeps running.
Dolphin's own log is merged with stdout/stderr for troubleshooting.
"""
import argparse
import os
from pathlib import Path
import re
import subprocess
import sys

FATAL = [
    r"DolReader:.*(?:not aligned|invalid|failed|out of bounds|overlap)",
    r"(?:failed|unable|could not) to (?:load|boot|open|read).*(?:\.dol|DOL|executable)",
    r"(?:invalid|corrupt|malformed) (?:DOL|executable|section)",
    r"(?:unknown|invalid) (?:instruction|opcode)",
    r"(?:unmapped|unhandled) (?:memory access|exception)",
    r"(?:panic|fatal error).*?(?:DOL|PPC|executable|instruction|memory)",
]
PATTERNS = [re.compile(p, re.IGNORECASE) for p in FATAL]


def check(log: str) -> list[str]:
    return [line for line in log.splitlines()
            if any(pattern.search(line) for pattern in PATTERNS)]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("dol", type=Path)
    parser.add_argument("--seconds", type=int, default=20)
    parser.add_argument("--binary", default="/usr/games/dolphin-emu-nogui")
    parser.add_argument("--logs", type=Path, default=Path("dolphin-smoke"))
    parser.add_argument("--expect-marker", help="Require a UART boot marker from the emulated program")
    args = parser.parse_args()
    if not args.dol.is_file() or args.dol.stat().st_size < 256:
        parser.error("missing or truncated DOL")
    args.logs.mkdir(parents=True, exist_ok=True)
    user = args.logs.resolve() / "user"
    user.mkdir(exist_ok=True)
    # Non-GUI Dolphin only accepts the executable path. Configure backend and
    # logging in the isolated user directory rather than passing Qt GUI flags.
    config = user / "Config"
    config.mkdir(exist_ok=True)
    (config / "Dolphin.ini").write_text("[Core]\nGFXBackend = Null\nCPUCore = 0\nCPUThread = False\n[DSP]\nBackend = No Audio Output\n")
    (config / "Logger.ini").write_text(
        "[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 4\n"
        "[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\nOSREPORT = True\n")
    environment = dict(os.environ)
    # The Actions container's default HOME can be inaccessible to Dolphin/SDL.
    # Keep every user/config/cache path inside this job's writable workspace.
    home = args.logs.resolve() / "home"
    home.mkdir(exist_ok=True)
    for subdir in ("config", "data", "cache"):
        (home / subdir).mkdir(exist_ok=True)
    environment["HOME"] = str(home)
    environment["XDG_CONFIG_HOME"] = str(home / "config")
    environment["XDG_DATA_HOME"] = str(home / "data")
    environment["XDG_CACHE_HOME"] = str(home / "cache")
    environment["DOLPHIN_EMU_USERPATH"] = str(user)
    environment["SDL_AUDIODRIVER"] = "dummy"
    command = ["xvfb-run", "-a", "timeout", "--signal=TERM", "--kill-after=5s",
               f"{args.seconds}s", args.binary, "--exec", str(args.dol.resolve())]
    print("Launching:", " ".join(command), flush=True)
    completed = subprocess.run(command, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, errors="replace",
                               timeout=args.seconds + 30, env=environment)
    output = completed.stdout
    for path in (user / "Logs").rglob("*") if (user / "Logs").exists() else []:
        if path.is_file() and path.suffix.lower() in (".log", ".txt"):
            output += f"\n--- {path.name} ---\n" + path.read_text(errors="replace")
    (args.logs / "emulator.log").write_text(output)
    print(output[-12000:], flush=True)
    hits = check(output)
    if hits:
        print("DOL-specific emulator diagnostics:\n" + "\n".join(hits), file=sys.stderr)
        return 1
    if completed.returncode not in (0, 124):
        print(f"Dolphin exited abnormally: {completed.returncode}", file=sys.stderr)
        return 1
    # A silent process running until timeout proves nothing: require evidence
    # from Dolphin's enabled BOOT logging that it attempted to boot the DOL.
    if not re.search(r"(?:\[BOOT\]|Booting.*(?:DOL|\.dol)|Loading.*\.dol)", output, re.IGNORECASE):
        print("No Dolphin boot log detected; cannot confirm the DOL was loaded", file=sys.stderr)
        return 1
    if args.expect_marker and args.expect_marker not in output:
        print("Missing emulated-program boot marker: " + args.expect_marker, file=sys.stderr)
        return 1
    print("No DOL-specific loader or PPC errors detected.")
    print("Note: this is a smoke test, not proof of application correctness.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
