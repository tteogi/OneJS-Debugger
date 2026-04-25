#!/bin/bash
set -e

# Build OnejsDebugger native plugin for Android (arm64-v8a, armeabi-v7a, x86_64).
# Output: Plugins~/Android/<abi>/libquickjs_unity.so
# Requires: Android NDK (set NDK_ROOT, ANDROID_NDK_HOME, or ANDROID_NDK_ROOT)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

API_LEVEL=21
ABIS=("arm64-v8a" "armeabi-v7a" "x86_64")
OUT_BASE="Plugins~/Android"

# Locate NDK
NDK="${NDK_ROOT:-${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}}"
if [ -z "$NDK" ]; then
    for CANDIDATE in \
        "/Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK" \
        "$HOME/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK" \
        "$HOME/Library/Android/sdk/ndk/*"; do
        FOUND=$(ls -d $CANDIDATE 2>/dev/null | tail -1)
        if [ -n "$FOUND" ]; then
            NDK="$FOUND"
            break
        fi
    done
fi
if [ -z "$NDK" ] || [ ! -d "$NDK" ]; then
    echo "Error: Android NDK not found."
    echo "Set NDK_ROOT, ANDROID_NDK_HOME, or ANDROID_NDK_ROOT."
    exit 1
fi
echo "Using NDK: $NDK"

TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake"
if [ ! -f "$TOOLCHAIN_FILE" ]; then
    echo "Error: $TOOLCHAIN_FILE not found"; exit 1
fi

for ABI in "${ABIS[@]}"; do
    BUILD_DIR="build-android-$ABI"
    OUT_DIR="$OUT_BASE/$ABI"
    mkdir -p "$OUT_DIR"
    rm -rf "$BUILD_DIR"

    echo ""
    echo "=== Building $ABI ==="
    cmake -B "$BUILD_DIR" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN_FILE" \
        -DANDROID_ABI="$ABI" \
        -DANDROID_PLATFORM="android-$API_LEVEL" \
        -DANDROID_STL=c++_static
    cmake --build "$BUILD_DIR" -j"$(getconf _NPROCESSORS_ONLN || echo 4)" --target quickjs_unity

    cp "$BUILD_DIR/libquickjs_unity.so" "$OUT_DIR/"
    "$NDK/toolchains/llvm/prebuilt/$(case "$(uname -s)" in
        Darwin) echo darwin-x86_64 ;;
        Linux) echo linux-x86_64 ;;
        *) echo "" ;;
    esac)/bin/llvm-strip" --strip-unneeded "$OUT_DIR/libquickjs_unity.so" 2>/dev/null || true

    rm -rf "$BUILD_DIR"
done

echo ""
echo "DONE. $OUT_BASE/<abi>/libquickjs_unity.so"
