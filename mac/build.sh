#!/bin/bash
# Builds a universal (arm64 + x86_64) "Claude Launcher.app" into ~/Applications.
set -euo pipefail
cd "$(dirname "$0")"

# The SwiftUI macro plugins ship with Xcode, not the Command Line Tools.
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode-beta.app/Contents/Developer}"

APP="$HOME/Applications/Claude Launcher.app"
BUILD="build"
mkdir -p "$BUILD"

for arch in arm64 x86_64; do
    xcrun swiftc -swift-version 5 -parse-as-library -O \
        -target "$arch-apple-macos14.0" \
        -framework SwiftUI -framework AppKit -framework ApplicationServices \
        ClaudeLauncher.swift -o "$BUILD/ClaudeLauncher-$arch"
done
lipo -create "$BUILD/ClaudeLauncher-arm64" "$BUILD/ClaudeLauncher-x86_64" -output "$BUILD/ClaudeLauncher"

# App icon from ../assets/icon.png
if [ ! -f "$BUILD/AppIcon.icns" ] || [ ../assets/icon.png -nt "$BUILD/AppIcon.icns" ]; then
    ICONSET="$BUILD/AppIcon.iconset"
    rm -rf "$ICONSET"
    mkdir -p "$ICONSET"
    for size in 16 32 128 256 512; do
        sips -z $size $size ../assets/icon.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
        sips -z $((size * 2)) $((size * 2)) ../assets/icon.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
    done
    iconutil -c icns "$ICONSET" -o "$BUILD/AppIcon.icns"
fi

STAGE="$BUILD/Claude Launcher.app"
rm -rf "$STAGE"
mkdir -p "$STAGE/Contents/MacOS" "$STAGE/Contents/Resources"
cp "$BUILD/ClaudeLauncher" "$STAGE/Contents/MacOS/ClaudeLauncher"
cp Info.plist "$STAGE/Contents/Info.plist"
cp "$BUILD/AppIcon.icns" "$STAGE/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$STAGE"

mkdir -p "$HOME/Applications"
rm -rf "$APP"
cp -R "$STAGE" "$APP"
echo "Built: $APP"
