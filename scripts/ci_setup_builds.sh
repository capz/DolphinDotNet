#!/usr/bin/env bash
set -euo pipefail
: "${GITHUB_WORKSPACE:?}" "${GITHUB_ENV:?}" "${GITHUB_PATH:?}" "${RUNNER_TEMP:?}"
dotnet build tools/DolphinDotNet.Compiler -c Release
printf 'DND_COMPILER_DLL=%s/tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll\n' "$GITHUB_WORKSPACE" >> "$GITHUB_ENV"
mkdir -p "$RUNNER_TEMP/dnd-ci-bin"
# Keep GCC as the real compiler, avoiding recursive PATH lookup through cc.
command -v cc > "$RUNNER_TEMP/dnd-real-cc"
printf 'DND_CC_REAL=%s\n' "$(cat "$RUNNER_TEMP/dnd-real-cc")" >> "$GITHUB_ENV"
cat > "$RUNNER_TEMP/dnd-ci-bin/cc" <<'WRAPPER'
#!/usr/bin/env bash
exec python3 "$GITHUB_WORKSPACE/scripts/ci_cc.py" "$@"
WRAPPER
chmod +x "$RUNNER_TEMP/dnd-ci-bin/cc"
printf '%s/dnd-ci-bin\n' "$RUNNER_TEMP" >> "$GITHUB_PATH"
