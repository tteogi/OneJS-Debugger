#!/bin/bash
set -e

# Run every platform build script that's actionable on the current host.
# Use this for a quick local "build everything you can" smoke test before
# packaging. CI matrix-builds use the per-platform scripts directly.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

HOST="$(uname -s)"

run() {
    echo ""
    echo "########## $* ##########"
    "$@"
}

case "$HOST" in
    Darwin)
        run ./build.sh
        run ./build-ios.sh
        if [ -n "${NDK_ROOT:-${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}}" ] || \
           ls -d /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK 2>/dev/null | head -1 >/dev/null; then
            run ./build-android.sh
        else
            echo "(skip android — no NDK on PATH)"
        fi
        if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
            run ./build-windows.sh
        else
            echo "(skip windows — no mingw-w64 on PATH)"
        fi
        ;;
    Linux)
        run ./build-linux.sh
        if [ -n "${NDK_ROOT:-${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}}" ]; then
            run ./build-android.sh
        else
            echo "(skip android — no NDK on PATH)"
        fi
        if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
            run ./build-windows.sh
        else
            echo "(skip windows — no mingw-w64 on PATH)"
        fi
        ;;
    *)
        echo "Unsupported host: $HOST"
        exit 1
        ;;
esac

echo ""
echo "All available builds completed. See Plugins~/ for outputs."
