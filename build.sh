#!/bin/zsh
# Baut "LLM Duell.app" als Universal-App (Apple Silicon + Intel).
#
#   ./build.sh            → dist/LLM Duell.app
#   ./build.sh --dmg      → zusätzlich dist/LLM-Duell-<Version>.dmg
#
# Optionale Umgebungsvariablen:
#   VERSION=1.2.0                    Versionsnummer (Standard: 1.0.0)
#   SIGN_IDENTITY="Developer ID Application: …"
#                                    Mit Developer-ID signieren statt ad hoc
#   NOTARY_PROFILE=<profil>          DMG notarisieren (xcrun notarytool store-credentials)
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${VERSION:-1.0.0}"
NAME="LLM Duell"
APP="dist/$NAME.app"
MAKE_DMG=false
[[ "${1:-}" == "--dmg" ]] && MAKE_DMG=true

if [[ ! -f Resources/AppIcon.icns ]]; then
    echo "→ Erzeuge App-Icon"
    TMP=$(mktemp -d)
    swift Scripts/make_icon.swift "$TMP/AppIcon.iconset"
    iconutil -c icns "$TMP/AppIcon.iconset" -o Resources/AppIcon.icns
    rm -rf "$TMP"
fi

# Neuere SDKs deklarieren @State als Makro, dessen Plugin nur Xcode mitbringt.
# Ohne Xcode nehmen wir daher das neueste SDK, das noch ohne dieses Makro auskommt.
if [[ "$(xcode-select -p)" == *CommandLineTools* ]]; then
    for sdk in $(ls -d /Library/Developer/CommandLineTools/SDKs/MacOSX[0-9]*.*.sdk | sort -rV); do
        iface="$sdk/System/Library/Frameworks/SwiftUICore.framework/Versions/A/Modules/SwiftUICore.swiftmodule/arm64e-apple-macos.swiftinterface"
        if [[ -f "$iface" ]] && ! grep -q "macro State" "$iface"; then
            export SDKROOT="$sdk"
            echo "→ Verwende SDK $(basename "$sdk")"
            break
        fi
    done
fi

# Je nach SwiftPM-Version landen beide Architekturen im selben Ausgabeordner,
# daher wird jedes Binary direkt nach dem Bauen weggesichert.
BINARIES=()
mkdir -p .build/universal
for arch in arm64 x86_64; do
    echo "→ Kompiliere für $arch"
    swift build -c release --triple "$arch-apple-macosx15.0"
    cp "$(swift build -c release --triple "$arch-apple-macosx15.0" --show-bin-path)/LLMDuell" ".build/universal/LLMDuell-$arch"
    BINARIES+=(".build/universal/LLMDuell-$arch")
done

echo "→ Erstelle $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create "${BINARIES[@]}" -output "$APP/Contents/MacOS/LLMDuell"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>$NAME</string>
    <key>CFBundleDisplayName</key><string>$NAME</string>
    <key>CFBundleIdentifier</key><string>local.llmduell</string>
    <key>CFBundleExecutable</key><string>LLMDuell</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleDevelopmentRegion</key><string>de</string>
    <key>LSMinimumSystemVersion</key><string>15.0</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
    <key>NSHumanReadableCopyright</key><string>© $(date +%Y) Sandro Leidmann · GPL-3.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSAppTransportSecurity</key>
    <dict>
        <key>NSAllowsLocalNetworking</key><true/>
    </dict>
</dict>
</plist>
PLIST

if [[ -n "${SIGN_IDENTITY:-}" ]]; then
    echo "→ Signiere mit $SIGN_IDENTITY"
    codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$APP"
else
    codesign --force --sign - "$APP" >/dev/null
fi
echo "✓ App: $PWD/$APP ($(lipo -archs "$APP/Contents/MacOS/LLMDuell"))"

if $MAKE_DMG; then
    DMG="dist/LLM-Duell-$VERSION.dmg"
    echo "→ Erstelle $DMG"
    STAGE=$(mktemp -d)
    cp -R "$APP" "$STAGE/"
    ln -s /Applications "$STAGE/Programme"
    rm -f "$DMG"
    hdiutil create -volname "$NAME $VERSION" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG" >/dev/null
    rm -rf "$STAGE"
    if [[ -n "${SIGN_IDENTITY:-}" ]]; then
        codesign --force --timestamp --sign "$SIGN_IDENTITY" "$DMG"
    fi
    if [[ -n "${NOTARY_PROFILE:-}" ]]; then
        echo "→ Notarisiere (kann einige Minuten dauern)"
        xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
        xcrun stapler staple "$DMG"
    fi
    echo "✓ DMG: $PWD/$DMG ($(du -h "$DMG" | cut -f1))"
    shasum -a 256 "$DMG"
fi
