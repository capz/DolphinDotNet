#!/usr/bin/env bash
# APT mirrors occasionally stall without emitting output (CI #1480: >2 h).
# Enforce bounds and record which operation failed instead of hiding progress
# behind apt-get -qq. Skip network access when hosted runner tools are ready.
set -euo pipefail

: "${RUNNER_TEMP:?RUNNER_TEMP is required}"
probe="$RUNNER_TEMP/dnd-ci-multilib-probe-$$"
trap 'rm -f "$probe"' EXIT

if command -v ccache >/dev/null 2>&1 &&
   printf 'int main(void) { return 0; }\n' | cc -m32 -x c - -o "$probe" >/dev/null 2>&1; then
    echo "ccache and 32-bit host compiler are already available; skipping APT."
    exit 0
fi

apt_options=(
    -o Acquire::Retries=1
    -o Acquire::http::Timeout=20
    -o Acquire::https::Timeout=20
    -o DPkg::Lock::Timeout=30
)
for attempt in 1 2; do
    echo "::group::Refresh APT package indexes (attempt $attempt/2)"
    if timeout --signal=TERM --kill-after=10s 90s sudo env DEBIAN_FRONTEND=noninteractive \
        apt-get "${apt_options[@]}" update; then
        echo "::endgroup::"
        echo "::group::Install ccache and gcc-multilib (attempt $attempt/2)"
        if timeout --signal=TERM --kill-after=10s 150s sudo env DEBIAN_FRONTEND=noninteractive \
            apt-get "${apt_options[@]}" install -y --no-install-recommends ccache gcc-multilib; then
            echo "::endgroup::"
            command -v ccache
            printf 'int main(void) { return 0; }\n' | cc -m32 -x c - -o "$probe"
            echo "Host compiler prerequisites verified."
            exit 0
        fi
    else
        echo "::endgroup::"
    fi
    echo "::warning::APT prerequisite installation attempt $attempt failed or timed out."
    if (( attempt == 1 )); then sleep 5; fi
done

echo "::error::Unable to install host compiler prerequisites within the bounded APT retries."
exit 1
