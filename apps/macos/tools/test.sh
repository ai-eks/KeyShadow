#!/bin/bash
set -euo pipefail
MACOS_DIR="$(cd "$(dirname "$0")/.." && pwd)"
python3 "$MACOS_DIR/tools/sync-pinyin.py" --check
swift test --package-path "$MACOS_DIR" --build-system native
if [ "${1:-}" = "--desktop" ] || [ "${1:-}" = "--privacy" ]; then
    "$MACOS_DIR/tools/build.sh"
    if [ "${1:-}" = "--privacy" ]; then
        FIXTURE_DIR="$(mktemp -d "$MACOS_DIR/.build/privacy.XXXXXX")"
        trap 'rm -rf "$FIXTURE_DIR"' EXIT
        mkdir -p "$FIXTURE_DIR/PrivacyFixture.app/Contents/MacOS"
        cp "$MACOS_DIR/assets/Info.plist" "$FIXTURE_DIR/PrivacyFixture.app/Contents/Info.plist"
        swiftc "$MACOS_DIR/Tests/PrivacyFixture/main.swift" -o "$FIXTURE_DIR/PrivacyFixture.app/Contents/MacOS/PrivacyFixture"
        /usr/libexec/PlistBuddy -c 'Set :CFBundleExecutable PrivacyFixture' "$FIXTURE_DIR/PrivacyFixture.app/Contents/Info.plist"
        /usr/libexec/PlistBuddy -c 'Set :CFBundleIdentifier uy.aix.keyshadow.PrivacyTests' "$FIXTURE_DIR/PrivacyFixture.app/Contents/Info.plist"
        /usr/libexec/PlistBuddy -c 'Set :LSMultipleInstancesProhibited false' "$FIXTURE_DIR/PrivacyFixture.app/Contents/Info.plist"
        codesign --force --sign - "$FIXTURE_DIR/PrivacyFixture.app"
        "$MACOS_DIR/../../dist/macos/键影.app/Contents/MacOS/KeyShadow" --smoke-test --privacy-test \
            --fixture-app "$FIXTURE_DIR/PrivacyFixture.app" --output "$MACOS_DIR/.build/screenshots"
    else
        "$MACOS_DIR/../../dist/macos/键影.app/Contents/MacOS/KeyShadow" --smoke-test --output "$MACOS_DIR/.build/screenshots"
    fi
fi
