#!/bin/bash
# Builds "Claude Launcher.app" into ~/Applications.
set -euo pipefail
cd "$(dirname "$0")"

APP="$HOME/Applications/Claude Launcher.app"
BUILD="build"
mkdir -p "$BUILD"

DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer xcrun swiftc -swift-version 5 -parse-as-library -O \
    -target arm64-apple-macos14.0 \
    -framework SwiftUI -framework AppKit -framework ApplicationServices \
    ClaudeLauncher.swift -o "$BUILD/ClaudeLauncher"

STAGE="$BUILD/Claude Launcher.app"
rm -rf "$STAGE"
mkdir -p "$STAGE/Contents/MacOS" "$STAGE/Contents/Resources"
cp "$BUILD/ClaudeLauncher" "$STAGE/Contents/MacOS/ClaudeLauncher"
cp Info.plist "$STAGE/Contents/Info.plist"
if [ -f /Applications/Claude.app/Contents/Resources/electron.icns ]; then
    cp /Applications/Claude.app/Contents/Resources/electron.icns "$STAGE/Contents/Resources/AppIcon.icns"
fi
codesign --force --deep --sign - "$STAGE"

mkdir -p "$HOME/Applications"
rm -rf "$APP"
cp -R "$STAGE" "$APP"
echo "Built: $APP"
