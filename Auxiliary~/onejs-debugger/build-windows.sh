#!/bin/bash
set -e

# Build OnejsDebugger native plugin for Windows x64 (cross-compile via MinGW).
# Output: Plugins~/Windows/x86_64/quickjs_unity.dll
# Requires: mingw-w64 (apt install mingw-w64 / brew install mingw-w64)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BUILD_DIR="build-windows"
OUT_DIR="Plugins~/Windows/x86_64"

rm -rf "$BUILD_DIR"
mkdir -p "$OUT_DIR"

# CMake toolchain inline
cat > "$BUILD_DIR-toolchain.cmake" <<'EOF'
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_C_COMPILER x86_64-w64-mingw32-gcc)
set(CMAKE_CXX_COMPILER x86_64-w64-mingw32-g++)
set(CMAKE_RC_COMPILER x86_64-w64-mingw32-windres)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
EOF

cmake -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_TOOLCHAIN_FILE="$BUILD_DIR-toolchain.cmake"
cmake --build "$BUILD_DIR" -j --target quickjs_unity

cp "$BUILD_DIR/quickjs_unity.dll" "$OUT_DIR/"
x86_64-w64-mingw32-strip --strip-unneeded "$OUT_DIR/quickjs_unity.dll" 2>/dev/null || true

echo ""
echo "DONE. $OUT_DIR/quickjs_unity.dll"
