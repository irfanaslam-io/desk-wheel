# Building and testing Desk Wheel

For contributors. To *use* Desk Wheel, download it from the [latest release](https://github.com/irfanaslam-io/desk-wheel/releases/latest) instead.

## Build and run on a Mac

```bash
brew install --cask dotnet-sdk          # once
brew install cmake pkg-config nasm      # once: tools to build the video/audio engine that ships inside DeskWheel.app
./build-mac.sh
open dist/DeskWheel.app
```

The first build also compiles FFmpeg (10–20 minutes, then it's reused) into `engines/ffmpeg/`, and it goes inside DeskWheel.app. **People who install Desk Wheel need nothing else**: no Homebrew, no ffmpeg, no PDF tools.

An orange ring appears in the menu bar and an orange button at the edge of the screen. macOS asks once for **Screen Recording** (for screenshots) and once whether Desk Wheel may control Finder (to read your selection). **Input Monitoring** is only needed if you turn on the optional keyboard gesture.

Each rebuild gets a new ad-hoc signature, so macOS may ask for these permissions again after rebuilding. A signed release ([RELEASING.md](RELEASING.md)) doesn't have this problem.

## Build and run on Windows

```powershell
winget install Microsoft.DotNet.SDK.10      # once
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\dist\DeskWheel-win-x64\DeskWheel.exe
```

`build.ps1` downloads an LGPL build of FFmpeg and ships it beside DeskWheel.exe (and in the installer), so nothing else needs installing.

The tray icon's menu has: on/off, work in all apps, start at login, add to "Send to", and quit.

Or push to GitHub: `.github/workflows/build.yml` tests the core, then builds `DeskWheel.app` (Apple silicon) and `DeskWheel.exe` (x64 and ARM64).

## Design system

All windows share one design system (`src/Wheel.App/Ui.cs`): neutral palettes for light and dark, one type scale, one set of buttons (primary, secondary, ghost, icon), Desk Wheel's own line icons, and the brand orange kept for the main action and selection. Settings is laid out like a system preferences window (sections on the left, grouped rows with descriptions on the right); the editor has an icon toolbar with shortcuts in tooltips and a status bar; cards show a status icon and a close button.

## How it's built

One .NET 10 codebase. Only `Platform/` differs per OS.

```
src/Wheel.Core          option catalogue, naming rules, ffmpeg / archive / subtitle engines (no UI, fully tested)
src/Wheel.App           Avalonia UI shared by both: wheel, toasts, menu-bar/tray, jobs, SkiaSharp image engine
  Platform/MacPlatform.cs      CoreGraphics event tap, Finder check, pbcopy, open -R, LaunchAgent
  Platform/WindowsPlatform.cs  WH_KEYBOARD_LL hook, Explorer check, Win32 clipboard, Run key
tests/Wheel.Core.Tests  dependency-free test runner (dotnet run)
```

1. The platform layer sees a wheel key go down. It never swallows keys, and it only posts to the UI thread.
2. If the mouse button is held and Finder or Explorer is in front, the wheel opens at the pointer without taking focus.
3. The wheel window is the drop target, so the files you're dragging are delivered to it. Only options valid for those files are shown.
4. On drop the wheel answers **Copy**, never Move, so your original is never touched. The job runs in the background, and a card in the screen corner says when it's done. Click the card to reveal the result.
5. Output names: `photo.jpg`, `photo (1).jpg`, `photo (compressed).jpg`. Nothing is ever overwritten.

**Images:**
- SkiaSharp (part of Avalonia) reads and writes PNG, JPG, WebP and ICO the same way on both systems, and fixes phone-photo rotation.
- HEIC, TIFF, GIF and BMP go through macOS's built-in `sips` on Mac, or ffmpeg on Windows.

## What has been verified

- **Core:** 85 tests pass with .NET 10.
  - Option rules, including which formats each platform can offer.
  - Output naming.
  - SRT/VTT and ZIP/TAR.GZ/GZ round trips, including zip-slip protection.
  - Every ffmpeg recipe run against real generated media.
  - PDF page numbers, merge/split/rotate, passwords and compression on real PDFs; QR codes made and read back (English and Arabic).
  - The FFmpeg build script, run end to end (on Linux, which shares everything except Apple's encoders).
  - Clipboard history storage, bulk rename (including undo and case-only renames), watched-folder rules, paging and pins.
- **App:** compiles with no warnings (on Linux, against the real Avalonia packages). The clipboard and rename windows were rendered off-screen and checked. Mac- and Windows-only parts (clipboard access, hotkeys, the Vision helper, recording) need a run on each system.

## Test checklist

Mac (✓ = confirmed):
- [x] Drop a file on the button and convert it (JPG → WebP)
- [x] Click the button; the centre steps Screenshot → Convert → Tools → Actions
- [x] Markdown → Read
- [ ] Screenshots: Area, Window, Full, 5 s; the card's Copy, Edit, Text, Pin, drag-out
- [ ] Editor: every tool, Beautify, Arabic text, Undo/Redo, Save, Copy
- [ ] Text (OCR) in English and Arabic; scanned PDF → TXT
- [ ] PDF: Merge, Split, Rotate, pages → PNG/JPG
- [ ] Office → PDF with Microsoft Office (Word, Excel, PowerPoint), with LibreOffice, and DOCX → TXT with neither
- [ ] History: thumbnails, search, double-click, drag-out
- [ ] Clipboard history: text, image, files; ⌥⇧V; pin; a 1Password copy is not recorded
- [ ] Actions → Copy (files) and Image; paste in Finder and in a chat
- [ ] Record (needs ffmpeg), Colour, QR, Make QR
- [ ] Size…, Cut out (macOS 14+), Icons; PDF Numbers, Compress, Lock/Unlock
- [ ] Rename with Undo; a watched folder; pinning a segment; More › paging
- [ ] Settings: screenshot folder, JPG quality, output folder, ffmpeg path
- [ ] A folder selection; a second display

Windows (not run yet):
- [ ] Everything above in File Explorer, on Windows 10 22H2 and Windows 11, on a second monitor with different scaling
- [ ] The installer, start at sign-in, uninstall

