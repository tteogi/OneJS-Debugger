#!/bin/bash
set -e

# Build outputs → committed package artifacts.
#
# After running build*.sh (or build-all.sh) on each host you have access to,
# run this script on macOS to:
#   1. Zip Auxiliary~/onejs-debugger/Plugins~/  →  package's OnejsDebuggerPlugins~/onejs-debugger-plugins.zip
#   2. Zip OneJS/Plugins/                       →  package's DefaultPlugins~/onejs-plugins.zip  (rollback fallback)
#   3. Build standalone qjs_debug for the host  →  package's qjs_debug~/<host>/qjs_debug[.exe]
#
# The committed artifacts are what release CI bundles into the UPM tarball.
# Running this on multiple hosts and merging the qjs_debug~/* outputs gives
# you full cross-platform coverage.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$SCRIPT_DIR"

PKG_DIR="$REPO_ROOT/Packages/com.yten.onejs-debugger"
PLUGINS_SRC="$SCRIPT_DIR/Plugins~"
ONEJS_PLUGINS="$REPO_ROOT/OneJS/Plugins"

# --- 1. Zip the debugger native plugins ---
if [ ! -d "$PLUGINS_SRC" ] || [ -z "$(ls -A "$PLUGINS_SRC" 2>/dev/null)" ]; then
    echo "Error: $PLUGINS_SRC is empty. Run build*.sh first."
    exit 1
fi

DEST_ZIP="$PKG_DIR/OnejsDebuggerPlugins~/onejs-debugger-plugins.zip"
mkdir -p "$(dirname "$DEST_ZIP")"
rm -f "$DEST_ZIP"
( cd "$PLUGINS_SRC" && zip -r "$DEST_ZIP" . > /dev/null )
echo "  -> $DEST_ZIP"
unzip -l "$DEST_ZIP" | tail -n +2 | head -n -2 | awk '{print "     " $4}'

# --- 2. Snapshot OneJS's stock plugins (for rollback fallback) ---
if [ -d "$ONEJS_PLUGINS" ]; then
    DEFAULT_ZIP="$PKG_DIR/DefaultPlugins~/onejs-plugins.zip"
    mkdir -p "$(dirname "$DEFAULT_ZIP")"
    rm -f "$DEFAULT_ZIP"
    ( cd "$ONEJS_PLUGINS" && zip -r "$DEFAULT_ZIP" . > /dev/null )
    echo "  -> $DEFAULT_ZIP"
else
    echo "  (skip OneJS rollback snapshot — $ONEJS_PLUGINS not present)"
fi

# --- 3. Build standalone qjs_debug ---
NPROC="$(getconf _NPROCESSORS_ONLN 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)"
QJS_DBG_ROOT="$REPO_ROOT/QuickJS-Debugger"

QJS_DBG_BUILD="$QJS_DBG_ROOT/build"

build_qjs_debug_native() {
    local host="$1" ext="$2"
    local build_dir="$QJS_DBG_BUILD/$host"
    echo "  [qjs_debug/$host] configuring..."
    cmake -B "$build_dir" -S "$QJS_DBG_ROOT" -DCMAKE_BUILD_TYPE=Release > /dev/null
    cmake --build "$build_dir" --config Release --target qjs_debug -j"$NPROC"
    local bin
    bin=$(find "$build_dir" -type f -name "qjs_debug$ext" -path '*/debugger/*' -print -quit)
    [ -z "$bin" ] && bin=$(find "$build_dir" -type f -name "qjs_debug$ext" -print -quit)
    if [ -z "$bin" ]; then echo "Error: qjs_debug$ext not found after build"; exit 1; fi
    local out="$PKG_DIR/qjs_debug~/$host"
    mkdir -p "$out"
    cp "$bin" "$out/qjs_debug$ext"
    chmod +x "$out/qjs_debug$ext" 2>/dev/null || true
    echo "  -> $out/qjs_debug$ext"
}

build_qjs_debug_windows_cross() {
    local build_dir="$QJS_DBG_BUILD/windows"
    local toolchain="$QJS_DBG_BUILD/toolchain-windows.cmake"
    mkdir -p "$QJS_DBG_BUILD"
    cat > "$toolchain" <<'TOOLCHAIN'
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_C_COMPILER x86_64-w64-mingw32-gcc)
set(CMAKE_CXX_COMPILER x86_64-w64-mingw32-g++)
set(CMAKE_RC_COMPILER x86_64-w64-mingw32-windres)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
TOOLCHAIN
    echo "  [qjs_debug/windows] cross-compiling via mingw-w64..."
    cmake -B "$build_dir" -S "$QJS_DBG_ROOT" -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_TOOLCHAIN_FILE="$toolchain" > /dev/null
    cmake --build "$build_dir" --config Release --target qjs_debug -j"$NPROC"
    local bin
    bin=$(find "$build_dir" -type f -name "qjs_debug.exe" -path '*/debugger/*' -print -quit)
    [ -z "$bin" ] && bin=$(find "$build_dir" -type f -name "qjs_debug.exe" -print -quit)
    if [ -z "$bin" ]; then echo "Error: qjs_debug.exe not found after cross-build"; exit 1; fi
    local out="$PKG_DIR/qjs_debug~/windows"
    mkdir -p "$out"
    cp "$bin" "$out/qjs_debug.exe"
    x86_64-w64-mingw32-strip --strip-unneeded "$out/qjs_debug.exe" 2>/dev/null || true
    echo "  -> $out/qjs_debug.exe"
}

case "$(uname -s)" in
    Darwin)
        build_qjs_debug_native macos ""
        if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
            build_qjs_debug_windows_cross
        else
            echo "  (skip qjs_debug/windows — mingw-w64 not found; brew install mingw-w64)"
        fi
        ;;
    Linux)
        build_qjs_debug_native linux ""
        if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
            build_qjs_debug_windows_cross
        else
            echo "  (skip qjs_debug/windows — mingw-w64 not found; apt install mingw-w64)"
        fi
        ;;
    MINGW*|MSYS*|CYGWIN*)
        build_qjs_debug_native windows ".exe"
        ;;
    *)
        echo "  (skip qjs_debug — unknown host)"
        ;;
esac

echo ""
echo "DONE. Commit the changes under Packages/com.yten.onejs-debugger/."
