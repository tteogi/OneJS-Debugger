#!/bin/bash
set -e

# Zips OneJS/Plugins/ → Packages/com.yten.onejs-debugger/DefaultPlugins~/onejs-plugins.zip
#
# Run this whenever the OneJS submodule is updated so the rollback zip stays
# in sync with the version of OneJS bundled in the project.
#
# Usage:
#   ./snapshot-default-plugins.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

ONEJS_PLUGINS="$REPO_ROOT/OneJS/Plugins"
DEST_ZIP="$REPO_ROOT/Packages/com.yten.onejs-debugger/DefaultPlugins~/onejs-plugins.zip"

if [ ! -d "$ONEJS_PLUGINS" ]; then
    echo "Error: OneJS/Plugins not found at $ONEJS_PLUGINS"
    echo "Make sure the OneJS submodule is initialised: git submodule update --init OneJS"
    exit 1
fi

mkdir -p "$(dirname "$DEST_ZIP")"
rm -f "$DEST_ZIP"
( cd "$ONEJS_PLUGINS" && zip -r "$DEST_ZIP" . > /dev/null )

echo "Snapshotted OneJS plugins → $DEST_ZIP"
unzip -l "$DEST_ZIP" | tail -n +2 | head -n -2 | awk '{print "  " $4}'
