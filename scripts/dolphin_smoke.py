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
import threading
from dolphin_gdb_probe import probe, resolve_symbols
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
    parser.add_argument("--gdb-port", type=int, default=0,
                        help="Capture guest PC/LR through Dolphin GDB stub")
    parser.add_argument("--elf", type=Path, help="Matching symbol-bearing ELF")
    parser.add_argument("--expect-symbol", action="append", default=[], help="Required guest function symbol")
    parser.add_argument("--expect-marker", help="Require a UART boot marker from the emulated program")
    parser.add_argument("--require-boot", action="store_true", help="Require Dolphin executable boot log")
    args = parser.parse_args()
    if not args.dol.is_file() or args.dol.stat().st_size < 256:
        parser.error("missing or truncated DOL")
    if args.expect_symbol and (not args.gdb_port or not args.elf):
        parser.error("--expect-symbol requires --gdb-port and --elf")
    expected = resolve_symbols(args.elf, args.expect_symbol) if args.expect_symbol else []
    args.logs.mkdir(parents=True, exist_ok=True)
    user = args.logs.resolve() / "user"
    user.mkdir(exist_ok=True)
    # Non-GUI Dolphin only accepts the executable path. Configure backend and
    # logging in the isolated user directory rather than passing Qt GUI flags.
    config = user / "Config"
    config.mkdir(exist_ok=True)
    (config / "Dolphin.ini").write_text("[Core]\nGFXBackend = Null\nCPUCore = 0\nCPUThread = False\n[DSP]\nBackend = No Audio Output\n[Interface]\nDebugModeEnabled = True\n" +
        (f"[General]\nGDBPort = {args.gdb_port}\n" if args.gdb_port else ""))
    (config / "Logger.ini").write_text(
        "[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 4\n"
        "[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\nOSREPORT = True\nOSREPORT_HLE = True\nEXI = True\n")
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
    guest_state = []
    if args.gdb_port:
        def capture_guest_state():
            guest_state.append(probe(args.gdb_port, expected, seconds=max(2, args.seconds - 2)))
        diagnostic = threading.Thread(target=capture_guest_state, daemon=True)
        diagnostic.start()
    completed = subprocess.run(command, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, errors="replace",
                               timeout=args.seconds + 30, env=environment)
    if args.gdb_port:
        diagnostic.join(timeout=1)
    output = completed.stdout + ("\n" + "\n".join(result.message for result in guest_state) if guest_state else "")
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
    if args.require_boot and not re.search(r"Booting from executable:.*\.dol", output, re.IGNORECASE):
        print("No Dolphin boot log detected; cannot confirm the DOL was loaded", file=sys.stderr)
        return 1
    if args.expect_marker and args.expect_marker not in output:
        print("Missing emulated-program boot marker: " + args.expect_marker, file=sys.stderr)
        return 1
    if args.expect_symbol and (not guest_state or not guest_state[0].success):
        print("Guest entry-point verification failed: " +
              (guest_state[0].message if guest_state else "GDB probe did not finish"), file=sys.stderr)
        return 1
    print("No DOL-specific loader or PPC errors detected.")
    print("Note: this is a smoke test, not proof of application correctness.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
