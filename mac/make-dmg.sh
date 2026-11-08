#!/bin/bash
# Packages build/"Claude Launcher.app" into build/ClaudeLauncher.dmg (drag to Applications).
set -euo pipefail
cd "$(dirname "$0")"

./build.sh
DMG_ROOT="build/dmg"
rm -rf "$DMG_ROOT" build/ClaudeLauncher.dmg
mkdir -p "$DMG_ROOT"
cp -R "build/Claude Launcher.app" "$DMG_ROOT/"
ln -s /Applications "$DMG_ROOT/Applications"
hdiutil create -volname "Claude Launcher" -srcfolder "$DMG_ROOT" -ov -format UDZO build/ClaudeLauncher.dmg >/dev/null
echo "Built: $(pwd)/build/ClaudeLauncher.dmg"
