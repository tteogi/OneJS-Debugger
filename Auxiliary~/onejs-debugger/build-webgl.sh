#!/bin/bash
set -e

# Generate OnejsDebugger WebGL stub plugin.
# Output: Plugins~/WebGL/OnejsDebuggerWebGL.jslib
#
# On WebGL, Unity runs JavaScript directly in the browser — QuickJS is not
# loaded and a WebSocket debugger server cannot be hosted from WASM.
# This jslib provides silent no-op stubs for the debugger DllImport symbols
# so the package compiles for WebGL without runtime errors.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SCRIPT_DIR/Plugins~/WebGL"
mkdir -p "$OUT_DIR"

cat > "$OUT_DIR/OnejsDebuggerWebGL.jslib" <<'JSLIB'
/**
 * OnejsDebugger WebGL stubs
 *
 * On WebGL, JS executes in the browser engine, not QuickJS.
 * Debugger functions are no-ops so builds succeed without runtime errors.
 * Use Chrome/Firefox DevTools for WebGL JS debugging instead.
 */
var OnejsDebuggerWebGLLib = {
    qjs_start_debugger: function(ctx, port) {
        console.warn("[OnejsDebugger] qjs_start_debugger: no-op on WebGL (use browser DevTools)");
        return 0;
    },
    qjs_stop_debugger: function(ctx) {},
    qjs_wait_debugger: function(ctx, timeout_ms) { return 0; },
    qjs_set_breakpoint: function(ctx, filePtr, line, conditionPtr) { return 0; },
    qjs_debugger_is_attached: function(ctx) { return 0; },
    qjs_register_script: function(ctx, urlPtr, sourcePtr) { return 0; }
};
mergeInto(LibraryManager.library, OnejsDebuggerWebGLLib);
JSLIB

echo ""
echo "DONE. $OUT_DIR/OnejsDebuggerWebGL.jslib"
