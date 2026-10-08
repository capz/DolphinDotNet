#!/usr/bin/env bash
set -euo pipefail
dol="${1:?Usage: test-dolphin.sh path/to/program.dol}"
test -s "$dol"
mkdir -p emulator-logs
dol="$(realpath "$dol")"
QT_QPA_PLATFORM=offscreen /usr/games/dolphin-emu --version > emulator-logs/dolphin-version.txt 2>&1
# --batch closes the emulator when emulation ends. A timeout is a failure,
# not a pass: a DOL that hangs must not satisfy the smoke test.
set +e
timeout --signal=TERM --kill-after=10s 90s \
  xvfb-run -a -s '-screen 0 1280x720x24' \
  env LIBGL_ALWAYS_SOFTWARE=1 QT_QPA_PLATFORM=xcb /usr/games/dolphin-emu --batch --exec="$dol" > emulator-logs/stdout.log 2> emulator-logs/stderr.log
status=$?
set -e
cat emulator-logs/stdout.log
cat emulator-logs/stderr.log >&2
printf '%s\n' "$status" > emulator-logs/exit-code.txt
if [[ $status -eq 124 || $status -eq 137 ]]; then
  echo "FAIL: Dolphin did not terminate within 90 seconds" >&2
  exit 1
fi
if [[ $status -ne 0 ]]; then
  echo "FAIL: Dolphin exited with status $status" >&2
  exit 1
fi
echo "PASS: Dolphin launched the DOL and terminated successfully"
