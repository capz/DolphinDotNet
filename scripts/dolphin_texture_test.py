#!/usr/bin/env python3
"""Boot the actual TextureStrategy DOL and validate sustained software-rendered output."""
import argparse
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import time
from dolphin_shader_test import run_ffmpeg
from dolphin_smoke import check, collect_logs

WIDTH, HEIGHT = 640, 480
FRAME_BYTES = WIDTH * HEIGHT * 3
MIN_FRAMES = 60


def validate_board(rgb):
    if len(rgb) != FRAME_BYTES:
        raise ValueError("Incomplete RGB frame")
    # Sample interiors of 32 cells above the performance panel and below the
    # startup console. Projection matches Camera(55,4/3) at (6,6,23).
    scale = 240 / (23 * math.tan(math.radians(55 / 2)))
    matches = 0
    for cy in range(6, 10):
        for cx in range(2, 10):
            x = round(320 + (cx + .375 - 6) * scale)
            y = round(240 - (cy + .375 - 6) * scale)
            at = (y * WIDTH + x) * 3
            actual = rgb[at:at + 3]
            bright = (cx * 3 + cy) & 1
            expected = (45 + cx * 12, 65 + cy * 11, 184) if bright else (18 + cx * 8, 26 + cy * 8, 84)
            if max(abs(a - b) for a, b in zip(actual, expected)) <= 24:
                matches += 1
    if matches < 28:
        raise AssertionError(f"Texture board mismatch: {matches}/32 expected cell colors")
    return matches


def validate_capture(raw):
    if len(raw) % FRAME_BYTES:
        raise ValueError("Incomplete decoded RGB frame")
    count = len(raw) // FRAME_BYTES
    if count < MIN_FRAMES:
        raise AssertionError(f"Only {count} rendered frames; require at least {MIN_FRAMES}")
    # Ignore startup clears, but require a full second of consecutive good
    # output at the end, rather than accepting one lucky frame.
    scores = [validate_board(raw[i * FRAME_BYTES:(i + 1) * FRAME_BYTES])
              for i in range(count - 30, count)]
    return dict(renderedFrames=count, validatedTailFrames=len(scores),
                minimumMatchingCells=min(scores))


def decode_texture_capture(capture, log, streaming=False):
    if streaming:
        return run_ffmpeg(["-i", str(capture), "-vf", "fps=3,scale=640:480:flags=neighbor",
                           "-frames:v", "360", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"], log)
    # Read the actual tail, so a later exception/error screen cannot hide behind
    # a successful initial burst. Bound decoded memory to two seconds.
    return run_ffmpeg(["-sseof", "-2", "-i", str(capture), "-vf", "scale=640:480:flags=neighbor",
                       "-frames:v", "120", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"], log)


def run(args):
    root = args.logs.resolve()
    if root.exists() and any(root.iterdir()):
        raise ValueError("Use a fresh capture directory")
    if not args.dol.is_file() or args.dol.stat().st_size < 256:
        raise ValueError("Missing or truncated DOL")
    config = root / "user" / "Config"
    config.mkdir(parents=True)
    (config / "Dolphin.ini").write_text(
        "[Core]\nGFXBackend = Software\nCPUCore = 1\nCPUThread = False\n"
        "[DSP]\nBackend = No Audio Output\n"
        "[Movie]\nDumpFrames = True\nDumpFramesSilent = True\n")
    (config / "GFX.ini").write_text(
        "[Settings]\nDumpFormat = avi\nDumpCodec = ffv1\nDumpEncoder = ffv1\nDumpPixelFormat = bgr0\n")
    (config / "Logger.ini").write_text(
        "[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 3\n"
        "[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\n")
    environment = dict(os.environ)
    environment.update(DOLPHIN_EMU_USERPATH=str(root / "user"),
                       SDL_AUDIODRIVER="dummy", LP_NUM_THREADS="2")
    for key, name in (("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache"), ("XDG_DATA_HOME", "data")):
        path = root / name
        path.mkdir(exist_ok=True)
        environment[key] = str(path)
    stdout = root / "stdout.log"
    before_stop = ""
    midpoint_bytes = None
    final_bytes = 0
    frames_path = root / "user" / "Dump" / "Frames"
    with stdout.open("w") as log:
        process = subprocess.Popen(
            ["xvfb-run", "-a", args.binary, "--user", str(root / "user"), "--exec", str(args.dol.resolve())],
            env=environment, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            started = time.monotonic()
            deadline = started + args.seconds
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise RuntimeError(f"Dolphin exited early: {process.returncode}")
                before_stop = collect_logs(stdout, root / "user")
                if check(before_stop) or re.search(r"Stop.*Shutting down|Shutdown complete", before_stop, re.I):
                    raise RuntimeError("Guest fault or emulation stopped before the capture deadline")
                final_bytes = sum(p.stat().st_size for p in frames_path.glob("*.avi"))
                if midpoint_bytes is None and time.monotonic() - started >= args.seconds / 2:
                    midpoint_bytes = final_bytes
                time.sleep(.2)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    pass
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
            (root / "emulator.log").write_text(collect_logs(stdout, root / "user"))
    if midpoint_bytes is None or final_bytes <= midpoint_bytes:
        raise RuntimeError("Capture stopped growing during the second half of the soak")
    if not re.search(r"Booting from executable:.*\.dol", before_stop, re.I):
        raise RuntimeError("No executable boot evidence")
    captures = sorted((root / "user" / "Dump" / "Frames").glob("*.avi"))
    if len(captures) != 1:
        raise RuntimeError(f"Expected one AVI capture, found {len(captures)}")
    raw = decode_texture_capture(captures[0], root / "decode.log", args.streaming)
    selected = raw[-FRAME_BYTES:]
    run_ffmpeg(["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "640x480", "-i", "pipe:0",
                "-frames:v", "1", "-y", str(root / "frame.png")],
               root / "encode.log", input=selected)
    if args.streaming:
        from dolphin_streaming_check import validate_stream, telemetry, RenderMismatch
        states = []
        for offset in range(0, len(raw), FRAME_BYTES):
            try:
                states.append(telemetry(raw[offset:offset + FRAME_BYTES]))
            except AssertionError:
                continue
        (root / "diagnostics.json").write_text(json.dumps(states, indent=2) + "\n")
        try:
            proof = validate_stream(raw, args.mode)
        except RenderMismatch as error:
            run_ffmpeg(["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "640x480", "-i", "pipe:0",
                        "-frames:v", "1", "-y", str(root / "mismatch.png")],
                       root / "mismatch-encode.log", input=error.rgb)
            (root / "mismatch.json").write_text(json.dumps(error.state, indent=2) + "\n")
            raise
    else:
        proof = validate_capture(raw)
    proof.update(mode=args.mode, soakSeconds=args.seconds,
                 secondHalfCaptureBytes=final_bytes - midpoint_bytes)
    (root / "results.json").write_text(json.dumps(proof, indent=2) + "\n")
    print(json.dumps(proof))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dol", type=Path)
    parser.add_argument("--streaming", action="store_true")
    parser.add_argument("--mode", choices=["pages", "atlas"], required=True)
    parser.add_argument("--binary", default="/usr/games/dolphin-emu-nogui")
    parser.add_argument("--logs", type=Path, default=Path("dolphin-texture"))
    parser.add_argument("--seconds", type=float, default=45)
    args = parser.parse_args()
    if not 5 <= args.seconds <= 120:
        parser.error("--seconds must be between 5 and 120")
    run(args)
