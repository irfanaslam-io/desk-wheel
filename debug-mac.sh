#!/usr/bin/env bash
# One command to build DeskWheel, run it in debug mode, and collect everything needed to diagnose a problem.
#   ./debug-mac.sh
# Then: drag a file onto the orange button at the screen edge and drop it on a segment.
# When done (or if DeskWheel stops), press Ctrl+C. Send the file wheel-debug.txt it creates.
set -uo pipefail
cd "$(dirname "$0")"
OUT="$PWD/wheel-debug.txt"
START=$(date +%s)

echo "== Stopping any running DeskWheel =="
pkill -x DeskWheel 2>/dev/null && sleep 1

echo "== Building =="
./build-mac.sh --skip-tests || { echo "Build failed: paste the errors above."; exit 1; }

{
  echo "== DeskWheel debug run: $(date) =="
  echo "macOS $(sw_vers -productVersion) ($(uname -m)), .NET SDK $(dotnet --version)"
} > "$OUT"

echo ""
echo "== DeskWheel is running in debug mode =="
echo "Now: drag a file from Finder onto the orange button at the right edge of the screen,"
echo "     keep dragging onto a segment (e.g. JPG) and let go."
echo "Also try: select a file in Finder and click the orange button (or press Option+Space)."
echo "Screenshots: Option+Shift+1 (area), +2 (window), +3 (full screen), or right-click the orange button."
echo "Press Ctrl+C here when finished."
echo ""

trap 'true' INT   # Ctrl+C stops DeskWheel but lets this script finish collecting
./dist/DeskWheel.app/Contents/MacOS/DeskWheel --debug 2>&1 | tee -a "$OUT"
CODE=${PIPESTATUS[0]}
trap - INT

{
  echo ""
  echo "== DeskWheel exited with code $CODE =="
  echo ""
  echo "== DeskWheel log =="
  cat "$HOME/Library/Application Support/DeskWheel/log.txt" 2>/dev/null || echo "(no log file)"
  echo ""
  echo "== macOS crash report (if DeskWheel crashed) =="
  REPORT=$(ls -t "$HOME"/Library/Logs/DiagnosticReports/DeskWheel*.ips 2>/dev/null | head -1)
  if [[ -n "$REPORT" && $(stat -f %m "$REPORT") -ge $START ]]; then
    echo "$REPORT"
    head -c 15000 "$REPORT"
  else
    echo "(none from this run)"
  fi
} >> "$OUT"

echo ""
echo "Saved everything to: $OUT"
echo "Send me that file (or paste its contents)."
