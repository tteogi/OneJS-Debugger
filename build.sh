#!/bin/bash
set -e

# One-shot build + package script.
#
# Runs on the current host and produces everything needed to commit:
#   1. Native plugin (libquickjs_unity / quickjs_unity.dll) for the host
#   2. Standalone qjs_debug binary for the host
#   3. OnejsDebuggerPlugins~/onejs-debugger-plugins.zip  (all built platforms so far)
#   4. DefaultPlugins~/onejs-plugins.zip                 (OneJS rollback fallback)
#
# Usage:
#   ./build.sh              — build native plugin + package
#   ./build.sh --skip-build — skip native plugin build, re-package existing Plugins~ only
#
# After this script finishes, commit Packages/com.yten.onejs-debugger/ and tag to release.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_DIR="$REPO_ROOT/Auxiliary~/onejs-debugger"

SKIP_BUILD=0
for arg in "$@"; do
    [ "$arg" = "--skip-build" ] && SKIP_BUILD=1
done

echo "=================================================="
echo " OneJS-Debugger — full build"
echo " Host: $(uname -s -m)"
echo "=================================================="

# ------------------------------------------------------------------
# 1. Build native plugin for this host
# ------------------------------------------------------------------
if [ "$SKIP_BUILD" = "0" ]; then
    echo ""
    echo "--- [1/2] Building native plugin ---"
    cd "$PLUGIN_DIR"
    ./build-all.sh
else
    echo ""
    echo "--- [1/2] Skipping native plugin build (--skip-build) ---"
fi

# ------------------------------------------------------------------
# 2. Package: zip plugins, snapshot OneJS defaults, build qjs_debug
# ------------------------------------------------------------------
echo ""
echo "--- [2/2] Packaging committed artifacts ---"
cd "$PLUGIN_DIR"
./package-local.sh

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
echo ""
echo "=================================================="
echo " BUILD COMPLETE"
echo ""
echo " Commit these files and tag to release:"
PKG="$REPO_ROOT/Packages/com.yten.onejs-debugger"
git -C "$REPO_ROOT" status --short -- "$PKG" 2>/dev/null | sed 's/^/   /' || true
echo ""
echo " Then:"
echo "   git add Packages/com.yten.onejs-debugger/"
echo "   git commit -m 'chore: update native artifacts'"
echo "   git tag v<version> && git push --tags"
echo "=================================================="
