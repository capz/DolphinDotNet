#!/usr/bin/env python3
"""Boot a freshly built DOL in Dolphin and reject executable/loader diagnostics.

Exit 124 denotes the hard deadline for a game that keeps running.
Dolphin's own log is merged with stdout/stderr for troubleshooting.
"""
import argparse
import os
from pathlib import Path
import re
import signal
import subprocess
import threading
import time
from dolphin_gdb_probe import ProbeResult, probe, probe_memory, probe_source, resolve_source, resolve_symbols, resolve_memory_expectations
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


def collect_logs(stdout, user):
    output=stdout.read_text(errors='replace') if stdout.exists() else ''
    for path in sorted((user/'Logs').rglob('*')) if (user/'Logs').exists() else []:
        if path.is_file() and path.suffix.lower() in ('.log','.txt'):
            output+=f'\n--- {path.name} ---\n'+path.read_text(errors='replace')
    return output


def run_emulator(command, environment, stdout, seconds, completed=None, minimum_seconds=0):
    """Stop only after explicit evidence, process failure or the hard deadline.

    completed returns True when all assertions/required log markers are ready,
    False after a probe failure, and None while evidence is still pending.
    """
    started=time.monotonic();intentional=False;status=None
    with stdout.open('w') as log:
        process=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT,
                                 env=environment,start_new_session=True)
        try:
            while True:
                status=process.poll()
                if status is not None: break
                elapsed=time.monotonic()-started
                evidence=completed() if completed else None
                if evidence is False or (evidence is True and elapsed>=minimum_seconds):
                    intentional=True;status=0;break
                if elapsed>=seconds:
                    intentional=True;status=124;break
                time.sleep(.05)
        finally:
            observed=process.poll()
            if observed is not None:
                intentional=False
            else:
                os.killpg(process.pid,signal.SIGTERM)
                try: process.wait(timeout=5)
                except subprocess.TimeoutExpired: pass
            # xvfb-run can exit before its Dolphin child handles SIGTERM.
            try: os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait()
    return status if intentional else process.returncode


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("dol", type=Path)
    parser.add_argument("--disc", type=Path, help="Insert a default ISO while booting the DOL")
    parser.add_argument("--seconds", type=int, default=20)
    parser.add_argument("--minimum-seconds", type=float, default=0,
                        help="Retain a soak period after proof (for gameplay/audio checks)")
    parser.add_argument("--binary", default="/usr/games/dolphin-emu-nogui")
    parser.add_argument("--logs", type=Path, default=Path("dolphin-smoke"))
    parser.add_argument("--cpu-core", choices=[0, 1], type=int, default=0,
                        help="Dolphin CPU core (0=interpreter, 1=JIT64)")
    parser.add_argument("--gdb-port", type=int, default=0,
                        help="Capture guest PC/LR through Dolphin GDB stub")
    parser.add_argument("--elf", type=Path, help="Matching symbol-bearing ELF")
    parser.add_argument("--expect-symbol", action="append", default=[], help="Required guest function symbol")
    parser.add_argument("--source-map", type=Path, help="Matching Portable PDB source map")
    parser.add_argument("--address-map", type=Path, help="PowerPC address map")
    parser.add_argument("--stack-steps", action="store_true", help="Verify DWARF managed stack and Step Over/Out")
    parser.add_argument("--inspect-objects", action="store_true", help="Verify class fields, references and string previews through DAP")
    parser.add_argument("--debugger-telemetry", action="store_true", help="Verify the adapter's disconnect report in guest RAM")
    parser.add_argument("--dap-adapter", type=Path, help="Built .NET DAP adapter DLL for source checks")
    parser.add_argument("--dotnet-probe", type=Path, help="Published .NET probe DLL for source checks")
    parser.add_argument("--expect-source", action="append", default=[], help="C# FILE:LINE in stop order")
    parser.add_argument("--expect-word", action="append", default=[], help="Require guest RAM SYMBOL=0xVALUE")
    parser.add_argument("--expect-text", action="append", default=[], help="Require text in guest RAM SYMBOL=TEXT")
    parser.add_argument("--memory-after-symbol", help="Read guest RAM at this completion breakpoint")
    parser.add_argument("--expect-marker", help="Require a UART boot marker from the emulated program")
    parser.add_argument("--require-boot", action="store_true", help="Require Dolphin executable boot log")
    args = parser.parse_args()
    if args.seconds<=0 or not 0<=args.minimum_seconds<=args.seconds:
        parser.error('require 0 <= --minimum-seconds <= --seconds and a positive deadline')
    if args.inspect_objects and not args.dap_adapter:
        parser.error("--inspect-objects requires --dap-adapter")
    if args.debugger_telemetry and not args.dap_adapter:
        parser.error("--debugger-telemetry requires --dap-adapter")
    if not args.dol.is_file() or args.dol.stat().st_size < 256:
        parser.error("missing or truncated DOL")
    requires_gdb = bool(args.expect_symbol or args.expect_word or args.expect_text or args.expect_source)
    if requires_gdb and (not args.gdb_port or not args.elf):
        parser.error("Guest entry checks require --gdb-port and --elf")
    if args.expect_source and (args.expect_symbol or args.expect_word or args.expect_text):
        parser.error("Use either C# breakpoints, ELF symbols, or memory checks")
    if args.expect_source and (not args.source_map or not args.address_map):
        parser.error("--expect-source requires --source-map and --address-map")
    if args.expect_symbol and (args.expect_word or args.expect_text):
        parser.error("Choose ELF breakpoints or guest-memory checks, not both")
    if args.memory_after_symbol and not (args.expect_word or args.expect_text):
        parser.error("--memory-after-symbol requires a guest-memory expectation")
    if args.stack_steps and not (args.dotnet_probe or args.dap_adapter):
        parser.error("--stack-steps requires --dotnet-probe or --dap-adapter")
    if args.dap_adapter and (args.dotnet_probe or not args.expect_source):
        parser.error("--dap-adapter requires --expect-source and excludes --dotnet-probe")
    if args.dotnet_probe and not args.expect_source:
        parser.error("--dotnet-probe requires --expect-source")
    expected = resolve_symbols(args.elf, args.expect_symbol) if args.expect_symbol else []
    source_points = resolve_source(args.elf, args.source_map, args.address_map, args.expect_source) if args.expect_source else []
    memory_expected = resolve_memory_expectations(args.elf, args.expect_word, args.expect_text) if (args.expect_word or args.expect_text) else []
    completion = resolve_symbols(args.elf, [args.memory_after_symbol])[0] if args.memory_after_symbol else None
    if args.disc and (not args.disc.is_file() or any(c in str(args.disc.resolve()) for c in "\r\n")):
        parser.error("--disc requires an existing ISO path without newlines")
    args.logs.mkdir(parents=True, exist_ok=True)
    user = args.logs.resolve() / "user"
    user.mkdir(exist_ok=True)
    # Non-GUI Dolphin only accepts the executable path. Configure backend and
    # logging in the isolated user directory rather than passing Qt GUI flags.
    config = user / "Config"
    config.mkdir(exist_ok=True)
    (config / "Dolphin.ini").write_text(f"[Core]\nGFXBackend = Null\nCPUCore = {args.cpu_core}\nCPUThread = False\n" +
        (f"DefaultISO = {args.disc.resolve()}\n" if args.disc else "") + "[DSP]\nBackend = No Audio Output\n[Interface]\nDebugModeEnabled = True\n" +
        (f"[General]\nGDBPort = {args.gdb_port}\n" if args.gdb_port else ""))
    (config / "Logger.ini").write_text(
        "[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 4\n"
        "[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\nOSREPORT = True\nOSREPORT_HLE = True\nEXI = True\nGDB_STUB = True\n")
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
    command = ["xvfb-run", "-a", args.binary, "--exec", str(args.dol.resolve())]
    print("Launching:", " ".join(command), flush=True)
    guest_state = []
    probe_finished=threading.Event()
    if args.gdb_port:
        def capture_guest_state_inner():
            if args.dap_adapter:
                try:
                    result = subprocess.run([sys.executable, str(args.dap_adapter.parent / "dnd_dap_probe.py"),
                        "--adapter", str(args.dap_adapter), "--port", str(args.gdb_port),
                        "--elf", str(args.elf), "--source-map", str(args.source_map),
                        "--address-map", str(args.address_map),
                        *(["--stack"] if args.stack_steps else []), *(["--inspection"] if args.inspect_objects else []),
                        *(["--telemetry"] if args.debugger_telemetry else []), *args.expect_source],
                        capture_output=True, text=True, timeout=max(2, args.seconds - 2))
                    guest_state.append(ProbeResult(result.returncode == 0, result.stdout + result.stderr))
                except (OSError, subprocess.TimeoutExpired) as error:
                    guest_state.append(ProbeResult(False, str(error)))
                return
            if args.dotnet_probe:
                try:
                    result = subprocess.run(["dotnet", str(args.dotnet_probe), str(args.gdb_port),
                        str(args.elf), str(args.source_map), str(args.address_map), *args.expect_source,
                        *(["--stack-steps"] if args.stack_steps else [])],
                        capture_output=True, text=True, timeout=max(2, args.seconds - 2))
                    guest_state.append(ProbeResult(result.returncode == 0, result.stdout + result.stderr))
                except (OSError, subprocess.TimeoutExpired) as error:
                    guest_state.append(ProbeResult(False, str(error)))
                return
            guest_state.append(probe_memory(args.gdb_port, memory_expected, seconds=max(2, args.seconds - 2), stop_symbol=completion)
                               if memory_expected else
                               probe_source(args.gdb_port, source_points, seconds=max(2, args.seconds - 2))
                               if source_points else
                               probe(args.gdb_port, expected, seconds=max(2, args.seconds - 2)))
        def capture_guest_state():
            try: capture_guest_state_inner()
            except Exception as error: guest_state.append(ProbeResult(False,str(error)))
            finally: probe_finished.set()
        diagnostic = threading.Thread(target=capture_guest_state, daemon=True)
        diagnostic.start()
    stdout=args.logs/'stdout.log'
    def evidence_complete():
        if not requires_gdb or not probe_finished.is_set(): return None
        if not guest_state or not guest_state[0].success: return False
        output=collect_logs(stdout,user)
        if args.require_boot and not re.search(r'Booting from executable:.*\.dol',output,re.IGNORECASE):return None
        if args.expect_marker and args.expect_marker not in output:return None
        return True
    started=time.monotonic()
    returncode=run_emulator(command,environment,stdout,args.seconds,evidence_complete,args.minimum_seconds)
    print(f'DND_DOLPHIN_ELAPSED_SECONDS={time.monotonic()-started:.3f}',flush=True)
    if args.gdb_port:
        diagnostic.join(timeout=1)
    output = collect_logs(stdout,user) + ("\n" + "\n".join(result.message for result in guest_state) if guest_state else "")
    (args.logs / "emulator.log").write_text(output)
    print(output[-12000:], flush=True)
    hits = check(output)
    if hits:
        print("DOL-specific emulator diagnostics:\n" + "\n".join(hits), file=sys.stderr)
        return 1
    if returncode not in (0, 124):
        print(f"Dolphin exited abnormally: {returncode}", file=sys.stderr)
        return 1
    # A silent process running until timeout proves nothing: require evidence
    # from Dolphin's enabled BOOT logging that it attempted to boot the DOL.
    if args.require_boot and not re.search(r"Booting from executable:.*\.dol", output, re.IGNORECASE):
        print("No Dolphin boot log detected; cannot confirm the DOL was loaded", file=sys.stderr)
        return 1
    if args.expect_marker and args.expect_marker not in output:
        print("Missing emulated-program boot marker: " + args.expect_marker, file=sys.stderr)
        return 1
    if requires_gdb and (not guest_state or not guest_state[0].success):
        print("Guest entry-point verification failed: " +
              (guest_state[0].message if guest_state else "GDB probe did not finish"), file=sys.stderr)
        return 1
    print("No DOL-specific loader or PPC errors detected.")
    if requires_gdb:
        print("Verified guest execution using emulator-observed PC and entry evidence.")
    else:
        print("Note: loader-only smoke does not prove entry into main().")
    return 0


if __name__ == "__main__":
    sys.exit(main())
