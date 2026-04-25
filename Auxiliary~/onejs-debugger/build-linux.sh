#!/bin/bash
set -e

# Build OnejsDebugger native plugin for Linux x86_64.
# Output: Plugins~/Linux/x86_64/libquickjs_unity.so
# Run on Ubuntu (apt: cmake build-essential).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BUILD_DIR="build-linux"
OUT_DIR="Plugins~/Linux/x86_64"

rm -rf "$BUILD_DIR"
mkdir -p "$OUT_DIR"

cmake -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE=Release
cmake --build "$BUILD_DIR" -j"$(nproc)" --target quickjs_unity

cp "$BUILD_DIR/libquickjs_unity.so" "$OUT_DIR/"
strip --strip-unneeded "$OUT_DIR/libquickjs_unity.so" 2>/dev/null || true

echo ""
echo "DONE. $OUT_DIR/libquickjs_unity.so"
