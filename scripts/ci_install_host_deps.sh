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

# Use independent Ubuntu mirrors instead of the hosted runner's Azure
# mirror+file source. CI #1481 confirmed intermittent package throttling.
# Do not mutate system APT configuration or consume unrelated repositories.
. /etc/os-release
codename="${UBUNTU_CODENAME:-${VERSION_CODENAME:-}}"
if [[ "${ID:-}" != "ubuntu" || -z "$codename" ]]; then
    echo "::error::The managed host toolchain bootstrap expects an Ubuntu runner."
    exit 1
fi
sources="$RUNNER_TEMP/dnd-ci-ubuntu-sources-$$.list"
trap 'rm -f "$probe" "$sources"' EXIT

apt_options=(
    -o "Dir::Etc::sourcelist=$sources"
    -o "Dir::Etc::sourceparts=-"
    -o Acquire::Retries=0
    -o Acquire::http::Timeout=15
    -o Acquire::https::Timeout=15
    -o DPkg::Lock::Timeout=30
)
# Switch endpoints on retry instead of downloading from the same slow mirror.
mirrors=("http://archive.ubuntu.com/ubuntu" "http://us.archive.ubuntu.com/ubuntu")
for i in "${!mirrors[@]}"; do
    attempt=$((i+1))
    mirror="${mirrors[$i]}"
    printf 'deb [arch=amd64] %s %s main restricted universe multiverse\n' "$mirror" "$codename" > "$sources"
    printf 'deb [arch=amd64] %s %s-updates main restricted universe multiverse\n' "$mirror" "$codename" >> "$sources"
    printf 'deb [arch=amd64] http://security.ubuntu.com/ubuntu %s-security main restricted universe multiverse\n' "$codename" >> "$sources"
    echo "::notice::Using Ubuntu package mirror $mirror (attempt $attempt/2)."
    echo "::group::Refresh APT package indexes (attempt $attempt/2)"
    if timeout --signal=TERM --kill-after=5s 70s sudo env DEBIAN_FRONTEND=noninteractive \
        apt-get "${apt_options[@]}" update; then
        echo "::endgroup::"
        echo "::group::Install ccache and gcc-multilib (attempt $attempt/2)"
        if timeout --signal=TERM --kill-after=5s 170s sudo env DEBIAN_FRONTEND=noninteractive \
            apt-get "${apt_options[@]}" install -y --no-install-recommends ccache gcc-multilib; then
            echo "::endgroup::"
            command -v ccache
            printf 'int main(void) { return 0; }\n' | cc -m32 -x c - -o "$probe"
            echo "Host compiler prerequisites verified."
            exit 0
        fi
        echo "::endgroup::"
    else
        echo "::endgroup::"
    fi
    echo "::warning::APT mirror $mirror failed or exceeded its bounded download time."
    if (( attempt == 1 )); then sleep 5; fi
done

echo "::error::Unable to install host compiler prerequisites from either Ubuntu mirror."
exit 1
