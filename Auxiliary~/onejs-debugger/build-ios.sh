#!/bin/bash
set -e

# Build OnejsDebugger native plugin for iOS (static .a, arm64 device).
# Output: Plugins~/iOS/libquickjs_unity.a
# Requires: Xcode command line tools, CMake 3.16+

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BUILD_DIR="build-ios"
OUT_DIR="Plugins~/iOS"

rm -rf "$BUILD_DIR"
mkdir -p "$OUT_DIR"

# Use CMake's built-in iOS support. STATIC because Unity iOS uses __Internal
# DllImport (the lib gets linked into the IL2CPP binary).
cmake -B "$BUILD_DIR" -G Xcode \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_SYSTEM_NAME=iOS \
    -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=13.0 \
    -DCMAKE_IOS_INSTALL_COMBINED=NO \
    -DCMAKE_XCODE_ATTRIBUTE_ONLY_ACTIVE_ARCH=NO

cmake --build "$BUILD_DIR" --config Release --target quickjs_unity

# CMake nests the output under Release-iphoneos/. Locate and copy.
A=$(find "$BUILD_DIR" -name 'libquickjs_unity.a' -path '*Release-iphoneos*' -print -quit)
if [ -z "$A" ]; then
    A=$(find "$BUILD_DIR" -name 'libquickjs_unity.a' -print -quit)
fi
[ -z "$A" ] && { echo "Error: libquickjs_unity.a not found"; exit 1; }

# Bundle dependent static libs into a single archive (libtool is the right
# tool on macOS — it merges archives in-place without symbol clashes).
QJS_A=$(find "$BUILD_DIR" -name 'libqjs.a' -print -quit)
DBG_A=$(find "$BUILD_DIR" -name 'libqjs_debug_lib.a' -print -quit)
[ -z "$QJS_A" ] && { echo "Error: libqjs.a not found"; exit 1; }
[ -z "$DBG_A" ] && { echo "Error: libqjs_debug_lib.a not found"; exit 1; }

libtool -static -o "$OUT_DIR/libquickjs_unity.a" "$A" "$QJS_A" "$DBG_A"

echo ""
echo "DONE. $OUT_DIR/libquickjs_unity.a (arm64)"
echo "NOTE: Unity iOS plugin meta should set Platform=iOS, CPU=ARM64."

rm -rf "$BUILD_DIR"
