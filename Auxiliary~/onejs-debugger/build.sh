#!/bin/bash
set -e

# Build OnejsDebugger native plugin for the host (macOS).
# Output: Plugins~/macOS/libquickjs_unity.dylib

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BUILD_DIR="build-macos"
OUT_DIR="Plugins~/macOS"

rm -rf "$BUILD_DIR"
mkdir -p "$OUT_DIR"

cmake -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_OSX_ARCHITECTURES="arm64;x86_64"
cmake --build "$BUILD_DIR" -j"$(sysctl -n hw.ncpu)" --target quickjs_unity

cp "$BUILD_DIR/libquickjs_unity.dylib" "$OUT_DIR/"

# Strip on Release
strip -x "$OUT_DIR/libquickjs_unity.dylib" 2>/dev/null || true

echo ""
echo "DONE. $OUT_DIR/libquickjs_unity.dylib"
