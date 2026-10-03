#!/bin/bash
set -euo pipefail
MACOS_DIR="$(cd "$(dirname "$0")/.." && pwd)"
REPO_DIR="$(cd "$MACOS_DIR/../.." && pwd)"
VERSION="$(tr -d '\r\n' < "$REPO_DIR/VERSION")"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo 'VERSION must contain a three-part numeric version.' >&2
    exit 1
fi
OUTPUT_DIR="$REPO_DIR/dist/macos"
mkdir -p "$OUTPUT_DIR" "$MACOS_DIR/.build"
python3 "$MACOS_DIR/tools/sync-pinyin.py" --check

# Native SwiftPM works with either Xcode or Command Line Tools. Build each slice
# separately so the resource accessor remains identical for both architectures.
for ARCH in arm64 x86_64; do
    swift build --package-path "$MACOS_DIR" --build-system native -c release --arch "$ARCH"
done
ARM_DIR="$(swift build --package-path "$MACOS_DIR" --build-system native -c release --arch arm64 --show-bin-path)"
INTEL_DIR="$(swift build --package-path "$MACOS_DIR" --build-system native -c release --arch x86_64 --show-bin-path)"
STAGING="$(mktemp -d "$OUTPUT_DIR/.package.XXXXXX")"
trap 'rm -rf "$STAGING"' EXIT
APP="$STAGING/键影.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create "$ARM_DIR/KeyShadow" "$INTEL_DIR/KeyShadow" -output "$APP/Contents/MacOS/KeyShadow"
cp "$MACOS_DIR/assets/Info.plist" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
cp "$REPO_DIR/shared/branding/logo.png" "$APP/Contents/Resources/logo.png"
cp -R "$ARM_DIR/KeyShadow_KeyShadowCore.bundle" "$APP/Contents/Resources/"

ICONSET="$STAGING/AppIcon.iconset"
mkdir -p "$ICONSET"
for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$REPO_DIR/shared/branding/logo.png" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE * 2))
    sips -z "$DOUBLE" "$DOUBLE" "$REPO_DIR/shared/branding/logo.png" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
plutil -lint "$APP/Contents/Info.plist"
SIGN_OPTIONS=(--options runtime)
if [[ "${KEYSHADOW_SIGN_IDENTITY:--}" != "-" ]]; then SIGN_OPTIONS+=(--timestamp); fi
codesign --force --sign "${KEYSHADOW_SIGN_IDENTITY:--}" "${SIGN_OPTIONS[@]}" "$APP"
codesign --verify --deep --strict "$APP"
# Only replace this script's named build output after a successful staged build.
if [ -d "$OUTPUT_DIR/键影.app" ]; then rm -rf "$OUTPUT_DIR/键影.app"; fi
mv "$APP" "$OUTPUT_DIR/键影.app"
echo "Built: $OUTPUT_DIR/键影.app"
lipo -archs "$OUTPUT_DIR/键影.app/Contents/MacOS/KeyShadow"
