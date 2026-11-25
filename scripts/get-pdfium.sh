#!/usr/bin/env bash
# Downloads the PDFium library (Google's PDF engine, prebuilt by bblanchon/pdfium-binaries)
# for one platform into engines/pdfium/<rid>/. Skips if it's already there.
#   scripts/get-pdfium.sh osx-arm64 | osx-x64 | linux-x64 | win-x64 | win-arm64
set -euo pipefail
cd "$(dirname "$0")/.."

RID="${1:?usage: get-pdfium.sh <rid>}"
case "$RID" in
  osx-arm64) ASSET=pdfium-mac-arm64.tgz;  LIB=lib/libpdfium.dylib ;;
  osx-x64)   ASSET=pdfium-mac-x64.tgz;    LIB=lib/libpdfium.dylib ;;
  linux-x64) ASSET=pdfium-linux-x64.tgz;  LIB=lib/libpdfium.so ;;
  win-x64)   ASSET=pdfium-win-x64.tgz;    LIB=bin/pdfium.dll ;;
  win-arm64) ASSET=pdfium-win-arm64.tgz;  LIB=bin/pdfium.dll ;;
  *) echo "Unknown runtime: $RID"; exit 1 ;;
esac

DEST="engines/pdfium/$RID"
if [[ -f "$DEST/$(basename "$LIB")" ]]; then
  echo "PDFium for $RID: already downloaded"
  exit 0
fi

echo "Downloading PDFium for $RID..."
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
curl -fsSL "https://github.com/bblanchon/pdfium-binaries/releases/latest/download/$ASSET" -o "$TMP/pdfium.tgz"
tar -xzf "$TMP/pdfium.tgz" -C "$TMP"
mkdir -p "$DEST"
cp "$TMP/$LIB" "$DEST/"
cp "$TMP/LICENSE" "$DEST/PDFium-LICENSE.txt" 2>/dev/null || true
echo "PDFium for $RID: $DEST/$(basename "$LIB")"
