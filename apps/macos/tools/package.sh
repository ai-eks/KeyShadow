#!/bin/bash
set -euo pipefail
MACOS_DIR="$(cd "$(dirname "$0")/.." && pwd)"
REPO_DIR="$(cd "$MACOS_DIR/../.." && pwd)"
VERSION="$(tr -d '\r\n' < "$REPO_DIR/VERSION")"
OUTPUT_DIR="$REPO_DIR/dist/packages"
APP="$REPO_DIR/dist/macos/键影.app"
IDENTITY="${KEYSHADOW_SIGN_IDENTITY:--}"
NOTARIZE="${KEYSHADOW_NOTARIZE:-0}"

if [[ "$NOTARIZE" == "1" && ( "$IDENTITY" == "-" || -z "${KEYSHADOW_NOTARY_PROFILE:-}" ) ]]; then
    echo 'Notarization requires a Developer ID identity and KEYSHADOW_NOTARY_PROFILE.' >&2
    exit 1
fi

"$MACOS_DIR/tools/build.sh"
mkdir -p "$OUTPUT_DIR"
STAGING="$(mktemp -d "$OUTPUT_DIR/.macos.XXXXXX")"
trap 'rm -rf "$STAGING"' EXIT
NAME="KeyShadow-$VERSION-macOS-universal"
DMG="$STAGING/$NAME.dmg"

notarize() {
    xcrun notarytool submit "$1" "${NOTARY_OPTIONS[@]}" --wait --timeout 20m \
        --output-format json > "$STAGING/notary-result.json"
    if [[ "$(plutil -extract status raw -o - "$STAGING/notary-result.json")" != Accepted ]]; then
        cat "$STAGING/notary-result.json" >&2
        echo 'Apple did not accept the notarization submission.' >&2
        exit 1
    fi
}

if [[ "$NOTARIZE" == "1" ]]; then
    NOTARY_OPTIONS=(--keychain-profile "$KEYSHADOW_NOTARY_PROFILE")
    if [[ -n "${KEYSHADOW_SIGNING_KEYCHAIN:-}" ]]; then
        NOTARY_OPTIONS+=(--keychain "$KEYSHADOW_SIGNING_KEYCHAIN")
    fi
    # This temporary ZIP is only for notarization; distribute the DMG below.
    ditto -c -k --sequesterRsrc --keepParent "$APP" "$STAGING/notary.zip"
    notarize "$STAGING/notary.zip"
    xcrun stapler staple "$APP"
    xcrun stapler validate "$APP"
    spctl --assess --type execute --verbose=2 "$APP"
fi

mkdir -p "$STAGING/root"
ditto "$APP" "$STAGING/root/键影.app"
ln -s /Applications "$STAGING/root/Applications"
sed 's|](\.\./\.\./README\.md#通用操作)|](通用操作.md)|g' "$REPO_DIR/docs/macos/使用说明.md" > "$STAGING/root/使用说明.md"
sed -n '/^## 通用操作$/,/^## 文档$/p' "$REPO_DIR/README.md" \
    | sed '1s/^## /# /; /^## 文档$/d; s/^### /## /; s|](docs/双拼方案\.md)|](双拼方案.md)|g' > "$STAGING/root/通用操作.md"
cp "$REPO_DIR/docs/双拼方案.md" "$STAGING/root/双拼方案.md"
hdiutil create -volname "键影 $VERSION" -srcfolder "$STAGING/root" -fs HFS+ -format UDZO "$DMG"
if [[ "$IDENTITY" != "-" ]]; then
    codesign --force --timestamp --sign "$IDENTITY" "$DMG"
    codesign --verify --verbose=2 "$DMG"
fi
if [[ "$NOTARIZE" == "1" ]]; then
    notarize "$DMG"
    xcrun stapler staple "$DMG"
    xcrun stapler validate "$DMG"
fi

# Validate the distributable, including symlinks and the embedded universal app.
hdiutil verify "$DMG"
mkdir "$STAGING/mount"
hdiutil attach "$DMG" -readonly -nobrowse -mountpoint "$STAGING/mount" >/dev/null
trap 'hdiutil detach "$STAGING/mount" >/dev/null 2>&1 || true; rm -rf "$STAGING"' EXIT
codesign --verify --deep --strict "$STAGING/mount/键影.app"
architectures="$(lipo -archs "$STAGING/mount/键影.app/Contents/MacOS/KeyShadow")"
case "$architectures" in
    'arm64 x86_64'|'x86_64 arm64') ;;
    *) echo "Expected a universal app, got: $architectures" >&2; exit 1 ;;
esac
test "$(readlink "$STAGING/mount/Applications")" = /Applications
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$STAGING/mount/键影.app/Contents/Info.plist")" = "$VERSION"
hdiutil detach "$STAGING/mount" >/dev/null
trap 'rm -rf "$STAGING"' EXIT

mv -f "$DMG" "$OUTPUT_DIR/"
cd "$OUTPUT_DIR"
shasum -a 256 "$NAME.dmg" > "$NAME.dmg.sha256"
shasum -a 256 -c "$NAME.dmg.sha256"
echo "Packaged: $OUTPUT_DIR/$NAME.dmg"
