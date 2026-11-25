#!/usr/bin/env bash
# Builds dist/DeskWheel.app on a Mac (Apple silicon or Intel).
#   1. Install the .NET SDK (10 or newer) once:  brew install --cask dotnet-sdk   (or https://dotnet.microsoft.com/download)
#   2. Once, to build the video/audio engine that ships inside DeskWheel.app:  brew install cmake pkg-config nasm
#   3. In this folder:               ./build-mac.sh
# Then: open dist/DeskWheel.app  → an orange ring appears in the menu bar.
#
# Options:
#   --skip-tests   don't run the core tests first
#   --dmg          also make dist/DeskWheel-<version>.dmg (drag-to-Applications installer)
# For sharing DeskWheel with other people (so macOS doesn't block it), sign and notarize with your Apple
# Developer account. Set these before running (see docs/RELEASING.md):
#   WHEEL_SIGN_ID="Developer ID Application: Your Name (TEAMID)"
#   WHEEL_NOTARY_PROFILE="wheel-notary"   # made once with: xcrun notarytool store-credentials wheel-notary
set -euo pipefail
cd "$(dirname "$0")"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
command -v dotnet >/dev/null || { echo "The .NET SDK isn't installed. Run: brew install --cask dotnet-sdk"; exit 1; }
SDK_MAJOR=$(dotnet --version | cut -d. -f1)
if [ "$SDK_MAJOR" -lt 10 ]; then
  echo "DeskWheel needs the .NET 10 SDK or newer (found $(dotnet --version)). Run: brew upgrade --cask dotnet-sdk"
  exit 1
fi

case "$(uname -m)" in
  arm64) RID=osx-arm64 ;;
  *)     RID=osx-x64 ;;
esac

# PDF engine for this Mac (and for the tests).
bash scripts/get-pdfium.sh "$RID"

# Video and audio engine, built once and shipped inside DeskWheel.app (LGPL; people who install DeskWheel need nothing).
if ! bash scripts/build-ffmpeg.sh; then
  # Release builds (CI sets WHEEL_REQUIRE_FFMPEG=1) must not ship without video and audio.
  if [[ "${WHEEL_REQUIRE_FFMPEG:-0}" == 1 ]]; then echo "ERROR: the bundled ffmpeg didn't build."; exit 1; fi
  echo "warning: the bundled ffmpeg didn't build (see above). DeskWheel will still run, using an ffmpeg installed on this Mac if there is one."
fi

SKIP_TESTS=0; MAKE_DMG=0
for arg in "$@"; do
  case "$arg" in
    --skip-tests) SKIP_TESTS=1 ;;
    --dmg) MAKE_DMG=1 ;;
    *) echo "Unknown option: $arg"; exit 1 ;;
  esac
done

if [[ $SKIP_TESTS -eq 0 ]]; then
  echo "Running core tests..."
  dotnet run --project tests/Wheel.Core.Tests -c Release
fi

echo "Publishing for $RID..."
rm -rf "dist/publish-$RID" dist/DeskWheel.app
dotnet publish src/Wheel.App/Wheel.App.csproj -c Release -r "$RID" --self-contained true -o "dist/publish-$RID"

echo "Assembling DeskWheel.app..."
APP=dist/DeskWheel.app
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "dist/publish-$RID/." "$APP/Contents/MacOS/"
# Text recognition helper (Apple Vision). Needs Xcode's command-line tools; without them DeskWheel still
# works, and the Text options fall back to Tesseract if it's installed (brew install tesseract tesseract-lang).
if xcrun --find swiftc >/dev/null 2>&1; then
  echo "Building the text-recognition helper..."
  xcrun swiftc -O -target "$([ "$RID" = osx-arm64 ] && echo arm64 || echo x86_64)-apple-macos12" -o "$APP/Contents/MacOS/wheel-ocr" platform/mac/wheel-ocr.swift \
    || echo "warning: the text-recognition helper didn't build; Text options will be hidden unless Tesseract is installed."
else
  echo "note: swiftc not found (install with: xcode-select --install). Text options need it, or Tesseract."
fi

# App icon (made by scripts/icons/make_icon.py)
mkdir -p "$APP/Contents/Resources"
cp platform/icons/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Wheel.App/Wheel.App.csproj)

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Desk Wheel</string>
  <key>CFBundleDisplayName</key><string>Desk Wheel</string>
  <key>NSHumanReadableCopyright</key><string>© 2026 Muhammad Irfan Aslam · Free for individuals and small businesses · hello@irfanaslam.io</string>
  <key>CFBundleIdentifier</key><string>app.deskwheel.mac</string>
  <key>CFBundleExecutable</key><string>DeskWheel</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <!-- Menu-bar app: no Dock icon, no app menu -->
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <!-- Shown in the one-time "DeskWheel wants to control Finder" prompt (used to read the selected files) -->
  <key>NSAppleEventsUsageDescription</key><string>DeskWheel reads which files you selected in Finder, and asks Microsoft Word, Excel and PowerPoint to save your documents as PDF.</string>
</dict>
</plist>
PLIST

if [[ -n "${WHEEL_SIGN_ID:-}" ]]; then
  # Developer ID with the hardened runtime: every Mach-O file inside first, then the app.
  echo "Signing with: $WHEEL_SIGN_ID"
  ENT=platform/mac/Wheel.entitlements
  while IFS= read -r -d '' f; do
    if [[ "$f" != "$APP/Contents/MacOS/DeskWheel" ]] && file -b "$f" | grep -q "Mach-O"; then
      codesign --force --timestamp --options runtime --sign "$WHEEL_SIGN_ID" "$f"
    fi
  done < <(find "$APP/Contents/MacOS" -type f -print0)
  codesign --force --timestamp --options runtime --entitlements "$ENT" --sign "$WHEEL_SIGN_ID" "$APP/Contents/MacOS/DeskWheel"
  codesign --force --timestamp --options runtime --entitlements "$ENT" --sign "$WHEEL_SIGN_ID" "$APP"
  codesign --verify --strict --verbose=2 "$APP"

  if [[ -n "${WHEEL_NOTARY_PROFILE:-}" ]]; then
    echo "Notarizing (Apple checks the app; usually a few minutes)..."
    ditto -c -k --keepParent "$APP" dist/DeskWheel-notarize.zip
    xcrun notarytool submit dist/DeskWheel-notarize.zip --keychain-profile "$WHEEL_NOTARY_PROFILE" --wait
    xcrun stapler staple "$APP"
    rm -f dist/DeskWheel-notarize.zip
  fi
else
  # Ad-hoc signature so macOS can remember permissions for this build (fine on your own Mac).
  codesign --force --deep --sign - "$APP"
fi

if [[ $MAKE_DMG -eq 1 ]]; then
  DMG="dist/DeskWheel-${VERSION}.dmg"
  echo "Making $DMG..."
  STAGE=$(mktemp -d)
  cp -R "$APP" "$STAGE/"
  ln -s /Applications "$STAGE/Applications"
  cp LICENSE.md "$STAGE/License.txt"   # free for individuals and small businesses
  if [[ -z "${WHEEL_NOTARY_PROFILE:-}" ]]; then
    # Not notarized by Apple (that needs the paid Apple Developer Program): macOS asks once before the
    # first open. These steps are shown right in the DMG window.
    cat > "$STAGE/First time opening Desk Wheel.txt" <<'TXT'
Opening Desk Wheel for the first time
=====================================

Desk Wheel is free and isn't registered with Apple's paid developer programme,
so macOS asks you to confirm it once:

1. Drag Desk Wheel into the Applications folder.
2. Open it from Applications. macOS says it "can't be opened" or "Apple could
   not verify" it. Click Done (not Move to Bin).
3. Open System Settings > Privacy & Security, scroll down, and click
   "Open Anyway" next to Desk Wheel. Confirm with your password or Touch ID.

That's it: from then on it opens normally, and updates install from inside
the app.

Questions: hello@irfanaslam.io  ·  https://irfanaslam.io
TXT
  fi
  rm -f "$DMG"
  hdiutil create -volname "Desk Wheel ${VERSION}" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
  rm -rf "$STAGE"
  if [[ -n "${WHEEL_SIGN_ID:-}" ]]; then
    codesign --force --timestamp --sign "$WHEEL_SIGN_ID" "$DMG"
    if [[ -n "${WHEEL_NOTARY_PROFILE:-}" ]]; then
      xcrun notarytool submit "$DMG" --keychain-profile "$WHEEL_NOTARY_PROFILE" --wait
      xcrun stapler staple "$DMG"
    fi
  fi
  echo "Installer: $DMG"
fi

echo ""
echo "Done: $APP"
echo "Run:  open $APP"
echo "An orange button appears at the right edge of your screen: drop a file on it."
