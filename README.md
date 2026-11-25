# Desk Wheel

**Drop it. Turn it. Done.**

Free, offline file tools at your pointer: convert files, take and mark up screenshots, record the screen, copy text from anything on screen, keep a clipboard history, and read Markdown beautifully. Drop a file on the orange button, or click it. Results are saved beside the original, which is never changed. No uploads, no account, no limits.

## How to use

1. **Drop a file on the orange button** at the edge of your screen. The wheel opens around it. Keep dragging onto a segment, such as JPG, and let go.
2. **Or select files and click the button**, or press **⌥ Space** on Mac / **Ctrl+Shift+Space** on Windows. Then click a segment.
3. **Click the wheel's centre** (or right-click anywhere on it) to go to the next wheel: Screenshot → Convert → Tools → Actions → Screenshot. Convert, Tools and Actions work on the files selected in Finder / Explorer. Esc or clicking outside the wheel closes it.
4. **Select a folder** to work on the files inside it: a folder of photos gets the image conversions and tools for every photo in it (not sub-folders). ZIP and the actions use the folder itself.
5. **Drag the button** to move it anywhere; the position is remembered.

## Screenshots

| Capture | Mac | Windows |
| --- | --- | --- |
| Area | ⌥⇧1 | Alt+Shift+1 |
| Window | ⌥⇧2 | Alt+Shift+2 |
| Full screen (the one under the pointer) | ⌥⇧3 | Alt+Shift+3 |

Easiest: **click the orange button**. The wheel opens on **Screenshot**: Area, Window, Full, Text, Clipboard, Record, Colour, then **More ›** for QR, Make QR, 5 s, Files, History and Settings. Wheels with more than eight choices have a **More ›** segment; while dragging a file, rest on it for a moment to turn the page. **Right-click any segment to pin it** to the front of its wheel (★); right-click again to unpin. **Click the centre** to step through Screenshot → Convert → Tools → Actions; dots under the name show where you are. Right-clicking the button captures an area straight away.

- Screenshots are saved to **Pictures → Desk Wheel Screenshots** and copied to the clipboard.
- A card appears bottom-left with **Copy**, **Edit**, **Text**, **Pin** (keeps it floating on top), **Desk Wheel** (convert it) and **Show**. You can also drag the thumbnail into any app.
- **Mac:** uses the system's own selection screen (as ⌘⇧4: Space switches to window mode, Esc cancels). Needs the **Screen Recording** permission once.
- **Windows:** area screenshots open Desk Wheel's own **mark-up overlay** (below); window uses Windows' snipping overlay; full screen is captured directly.

### Mark up while you capture (area screenshots)

Drag over the area, and a toolbar appears right beside it, like Flameshot: **Pencil (P)**, **Line (L)**, **Arrow (A)**, **Box (R)**, **Circle (C)**, **Highlight (H)**, **Text (T)**, **numbered Steps (N)**, **Blur (B)**, **Redact (X)** and **Move (M)**. The colour dot (or keys **1–7**, or right-click) changes colour, the **mouse wheel** sets the line thickness, and **Ctrl+Z / Ctrl+Y** undo and redo.

The selection can be adjusted at any time, even after drawing: drag its **edges or corners** to resize, use **Move** (or hold **Space** and drag) to move it, or nudge it with the **arrow keys** (Shift: 10 px). Marks stay where you drew them on the screen; only the frame moves. Before anything is drawn, dragging outside the selection starts a new one.

Finish with **Enter** (save, and copy if that's on), **Ctrl+C** (copy and save), **Ctrl+S** (save), **Ctrl+P** (pin it on screen) or **Ctrl+E** (open in the full editor for crop and Beautify). **Esc** cancels. Blur and Redact are burned into the saved pixels, so what's hidden is really gone.

On by default on Windows. On a Mac it's off, so macOS's own picker is used; turn on **Settings › Screenshots › Mark up area screenshots right away** to use it there too.

## Clipboard history

Everything you copy (text, links, images, files) is remembered, so you can paste something from earlier.

- Open it with **⌥⇧V** (Mac) / **Alt+Shift+V** (Windows), a **right-click on the orange button**, or **Clipboard** on the Screenshot wheel.
- Type to search; **↑ ↓** and **Enter**, or click, put an item back on the clipboard; then paste as usual. **⌘1…9** / **Ctrl+1…9** pick one of the first nine.
- **☆** pins an item: it stays at the top and survives **Clear**. **✕** removes one.
- The last 300 items (60 images) are kept, on this computer only, in Desk Wheel's settings folder. Copies that password managers mark as private (1Password, Bitwarden, KeePass and others) are never recorded. Turn it off in Settings.

**Copy files:** on the Actions wheel, **Copy** puts the selected files on the clipboard, as Finder's / Explorer's Copy does: paste them into another folder, a chat or an email. **Image** copies the picture itself, ready to paste into a message or document.

## Screen tools

- **Record:** drag over part of the screen; a small **● 0:12 Stop** bar appears beside it. Saved as MP4 in the screenshots folder (drop it on the button for GIF or a smaller MP4). Needs ffmpeg. Choose Record again, or the menu item, to stop. At most 15 minutes.
- **Colour:** the screen freezes and a magnifier follows the pointer; click to copy the colour as HEX (the card also shows RGB and HSL).
- **QR:** drag over a QR code or barcode anywhere on screen; what it says is copied. Links can be opened from the card.
- **Make QR:** copy a link or some text, then choose Make QR: the code appears on the screenshot card, ready to copy, pin or drag out.

## Copying text from the screen (OCR)

Reads text in English and Arabic, on your computer. Nothing is uploaded.

- **Screenshot wheel → Text:** drag over anything on screen (a video, an image, a locked PDF, an error message) and its text is copied to the clipboard. Nothing is saved to your screenshots folder.
- **Screenshot card → Text:** copies the text in the screenshot you just took.
- **Tools → Text** on an image: copies its text. With several images, or a folder of them, a `.txt` is saved beside each.
- **Convert → TXT** on a scanned PDF (pages that are pictures) now reads the pages instead of giving an empty file.

Engines:
- **Mac:** Apple Vision, the same engine as Live Text. `build-mac.sh` builds a small helper for it, which needs Xcode's command-line tools (`xcode-select --install`). Which languages Vision reads depends on the macOS version; run `./debug-mac.sh` to see them listed in the log.
- **Windows:** the OCR built into Windows 10 and 11. For Arabic, add Arabic in Settings → Time & language → Language.
- **Either:** if neither is available, Tesseract is used when installed (`brew install tesseract tesseract-lang`).

## Reading Markdown files

Select a `.md` file (a README, notes, docs) and click the button, then the centre once: **Convert → Read** opens it as a formatted page in your browser, the way GitHub shows a README.

- GitHub's own reading style, in light or dark to match your system.
- A contents list beside the text, which follows you as you scroll.
- Coloured code with a **Copy** button, tables, task lists, and GitHub alerts (`> [!NOTE]`).
- Arabic paragraphs read right to left.
- Images next to the file show up. **Print / PDF** saves it as a PDF.
- Works offline. Scripts inside the file are removed before the page is shown.

**Convert → HTML** saves the formatted page beside the file, to share or keep. **Read** is also on the Tools wheel.

## Editing screenshots

Click **Edit** on the screenshot card, or choose **Edit** on the Tools wheel for any image.

| Tool | Key | What it does |
| --- | --- | --- |
| Arrow | A | Drag from tail to tip |
| Box | R | Outline an area |
| Highlight | H | See-through colour over an area |
| Text | T | Click, type, press Enter |
| Steps | N | Click to drop numbered markers 1, 2, 3… |
| Blur | B | Pixelates an area so it can't be read |
| Redact | X | Solid black box burned into the pixels: the content is gone, not just covered |
| Crop | C | Drag the area to keep |

- Five colours.
- **Undo/Redo:** ⌘Z / ⇧⌘Z (Ctrl on Windows).
- **Copy** puts the result on the clipboard.
- **Save** writes `name (edited).png` and never touches the original. **Done** saves and closes.

### Beautify

In the editor, click **Beautify** (or press F) to put the screenshot on a colourful background with padding, rounded corners and a soft shadow, ready for posts and slides. Click again for the next style: Sunset, Ocean, Grape, Mint, Graphite, Paper, then off. It's part of undo like any other change, and you can keep marking up while it's on.

## More file tools

| Tool | Where | What it does |
| --- | --- | --- |
| Size… | Tools (images) | Type 1920x1080 (fits inside, keeps proportions), 1080 (width), x720 (height) or 25% |
| Cut out | Tools (images, Mac 14+) | Removes the background, keeps the subject, as PNG (Apple Vision) |
| Icons | Tools (images) | App icon set: PNGs 16–1024 px, an ICO, and on Mac an ICNS |
| Numbers | Tools (PDF) | Page numbers "1 / 12" at the bottom of every page |
| Compress | Tools (PDF) | Smaller PDF: pictures re-saved at print quality (about 150 DPI), text and drawings untouched |
| Lock / Unlock | Tools (PDF) | Add or remove a password, AES-256 |
| Rename | Tools (any files, or a folder's files) | Bulk rename with a live preview: `{name}`, `{n}`, `{date}`, `{folder}`, find and replace, letter case. Never overwrites; Undo puts names back |

**Watched folders** (Settings): pick a folder and what to do, for example *Images → WebP*. Every new file that arrives there is converted automatically, beside the original. Desk Wheel's own results aren't processed again, and files still downloading are waited for.

## History

**History** on the Screenshot wheel (or in the menu-bar/tray menu) lists recent screenshots, converted files and edited copies, newest first, with thumbnails and a search box.

- Double-click a row to open it; drag a row into any app; **Copy** puts an image on the clipboard; **Show** finds it in Finder / Explorer.
- Files you've deleted or moved drop out of the list. **Clear history** forgets the list; it never deletes files.
- Only names and paths are kept (in `history.jsonl` beside the settings), never file contents.

## Office files

Word, Excel and PowerPoint files (and OpenDocument, RTF, TXT, CSV) get a Convert wheel:

| File | Converts to |
| --- | --- |
| DOCX, DOC, ODT, RTF, TXT | PDF, DOCX, TXT, HTML, ODT |
| XLSX, XLS, ODS, CSV | PDF, XLSX, CSV (first sheet), ODS |
| PPTX, PPT, ODP | PDF, PPTX, ODP |

Desk Wheel uses whichever of these is on the computer, in this order:

1. **Microsoft Office** (Word, Excel, PowerPoint). Most people already have it, so there's nothing to install, and the PDF looks exactly as it does in Office. On Windows, Office does every format in the table except TXT; on a Mac, Office does the PDFs. Files are opened read-only with macros off, and Office stays open if you were already using it. The first time on a Mac, macOS asks whether Desk Wheel may control Word, Excel or PowerPoint: click OK.
2. **LibreOffice** (free, libreoffice.org): everything in the table, and the fallback when Office can't open a file.
3. On a Mac with neither, the built-in `textutil` still converts word-processor files to DOCX, TXT, HTML and ODT.

## Settings

Open **Settings…** from the menu-bar/tray menu, or from the **Settings** segment on the Screenshot wheel. Changes apply at once.

- **Screenshots:** folder, PNG or JPG, copy to clipboard, show the preview card, window shadow (Mac).
- **Converted files:** beside the original, or into one folder you choose; JPG/WebP quality.
- **Video and audio:** shows which ffmpeg is used; choose a specific one, or find it automatically.
- **General:** on/off, floating button, start at login.
- **Advanced:** the drag + key gesture, and a button to open the settings/log folder.

Desk Wheel needs no keyboard permission. The first time you use the button's click or the shortcut, macOS asks once whether Desk Wheel may control Finder; that's how it reads your selection.

An optional "drag a file, tap ⇧" gesture can be turned on in the menu-bar menu. It needs the Input Monitoring permission.

### What's on each wheel

| Desk Wheel | What's on it |
| --- | --- |
| Screenshot | Area, Window, Full, Text, 5 s, Files, History, Settings |
| Convert | Images: PNG, JPG, WebP, HEIC (Mac), AVIF, ICO, TIFF, GIF, BMP · PDF → PNG / JPG (every page) / TXT (scanned PDFs too) · Video: MP4, WebM, MKV, MOV, GIF, MP3, M4A · Audio: MP3, M4A, WAV, FLAC, OGG, Opus · ZIP ↔ TAR.GZ, Extract · SRT ↔ VTT, TXT · Office: PDF, DOCX, XLSX, PPTX, CSV… · Markdown: Read, HTML |
| Tools | Images: Compress, 50%, Rotate, No EXIF, Edit, Text, Images → PDF · PDF: Merge, Split, Rotate · Video: Compress, 50%, Mute, Frame · Audio: Normalize, Mono, Compress · Markdown: Read · ZIP any selection |
| Actions | Copy path, Open terminal here, Reveal, Copy to Desktop, Copy name |

With the optional keyboard gesture on, Shift / ⌘ (Ctrl) / ⌃ (Alt) also switch to Convert / Tools / Actions. Esc closes the wheel.

## Build and run on a Mac

```bash
brew install --cask dotnet-sdk          # once
brew install cmake pkg-config nasm      # once: tools to build the video/audio engine that ships inside DeskWheel.app
./build-mac.sh
open dist/DeskWheel.app
```

The first build also compiles FFmpeg (10–20 minutes, then it's reused) into `engines/ffmpeg/`, and it goes inside DeskWheel.app. **People who install Desk Wheel need nothing else**: no Homebrew, no ffmpeg, no PDF tools.

An orange ring appears in the menu bar and an orange button at the edge of the screen. macOS asks once for **Screen Recording** (for screenshots) and once whether Desk Wheel may control Finder (to read your selection). **Input Monitoring** is only needed if you turn on the optional keyboard gesture.

Each rebuild gets a new ad-hoc signature, so macOS may ask for these permissions again after rebuilding. A signed release (below) doesn't have this problem.

## Releasing (sharing Desk Wheel with other people)

Windows and macOS only fully trust apps signed by someone they recognise. Desk Wheel uses the free way on each:

| | How people get it | Trusted? | Cost |
| --- | --- | --- | --- |
| **Windows** | **Microsoft Store** (main route) | Yes: Microsoft signs it. No Smart App Control or SmartScreen prompts; the Store installs updates | Free |
| Windows | Installer from the website | Blocked by Smart App Control unless signed with a code-signing certificate | Certificate ~US$130–300/yr (optional) |
| **macOS** | DMG from the website | macOS asks once (System Settings › Privacy & Security › Open Anyway); the DMG explains how | Free |
| macOS | DMG, notarized | Opens with no prompt | Apple Developer Program, US$99/yr (optional, later) |

### Microsoft Store (free, fully trusted)

1. **Register** at [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/home) with a personal Microsoft account: free for individuals, with an ID-and-selfie check.
2. In **Partner Center**, the app **Desk Wheel** is reserved. Its identity is built into `build.ps1`, so nothing needs setting:

   | Product identity | Value |
   | --- | --- |
   | Package/Identity/Name | `irfanaslam.io.DeskWheel` |
   | Package/Identity/Publisher | `CN=E17FA43C-D005-44B0-BA35-1A38B8D60FD6` |
   | Package/Properties/PublisherDisplayName | `irfanaslam.io` |
   | Package family name | `irfanaslam.io.DeskWheel_69qks97mj3kmy` |
   | Store ID | `9PF534B6ZW4Z` |

3. Store page: [apps.microsoft.com/detail/9PF534B6ZW4Z](https://apps.microsoft.com/detail/9PF534B6ZW4Z) (live once the first submission is certified); in Windows, `ms-windows-store://pdp/?productid=9PF534B6ZW4Z` opens it in the Store app. To build for another identity, set `WHEEL_MSIX_NAME`, `WHEEL_MSIX_PUBLISHER` and `WHEEL_MSIX_PUBLISHER_NAME` (or the Actions variables `MSIX_NAME`, `MSIX_PUBLISHER`, `MSIX_PUBLISHER_NAME`).
4. Build: GitHub Actions produces `DeskWheel-<version>-win-x64.msix` and `-win-arm64.msix` (artifacts *DeskWheel-win-…-store*), or run `.\build.ps1 -Msix` on a PC with the Windows SDK (`winget install Microsoft.WindowsSDK.10.0.26100`).
5. In Partner Center, start a submission: upload both `.msix` files under *Packages*, then fill in the Store listing (description, screenshots, the icon from `platform/icons`), age rating and the privacy policy link (the app can send anonymous usage counts when allowed, see Privacy below). Microsoft certifies and signs it, usually within a few days.
6. For each new version: bump `<Version>`, build, and upload the new `.msix` files in a new submission. People get it automatically from the Store.

The Store version is the same app with a few Store-style differences: the Store installs updates (Desk Wheel's own updater is off), "start when I sign in" is a Windows startup task (Settings › Apps › Startup), and the Explorer *Send to* entry isn't offered. To try a package locally, turn on **Developer Mode** and run `Add-AppxPackage -Register dist\msix-win-x64\AppxManifest.xml`.

### Website downloads

`./build-mac.sh --dmg` makes the DMG; `.\build.ps1 -Installer` makes `dist\DeskWheel-<version>-win-x64-setup.exe` (Inno Setup: `winget install JRSoftware.InnoSetup`), which installs for the current user without an administrator prompt.

**Windows signing (optional, paid).** With a code-signing certificate, the website installer is trusted too. Set `WHEEL_SIGN_PFX` and `WHEEL_SIGN_PASSWORD`, or `WHEEL_SIGN_COMMAND` for a cloud signing service (see the top of `build.ps1`). Every `.exe` and `.dll` that ships is signed, plus the installer, Inno Setup's helper that runs from the temporary folder and the uninstaller.

**Mac notarization (optional, US$99/yr).** With an Apple Developer account:

```bash
# once: store your notarization login in the keychain
xcrun notarytool store-credentials wheel-notary --apple-id you@example.com --team-id TEAMID
# each release
export WHEEL_SIGN_ID="Developer ID Application: Your Name (TEAMID)"
export WHEEL_NOTARY_PROFILE=wheel-notary
./build-mac.sh --dmg        # → dist/DeskWheel-<version>.dmg, signed, notarized and stapled
```

Without it, the DMG includes "First time opening Desk Wheel.txt" with the one-time steps. Note that each update replaces an ad-hoc-signed app, so macOS may ask again for Screen Recording or Input Monitoring after updating; a Developer ID signature avoids that.

### Publishing an update (installs over the old version)

Installed copies check for updates twice a day at most (Settings › Updates, or "Check for updates…" in the menu) and install them over the current version: nothing to uninstall, and settings, history and the clipboard are kept.

- **Windows:** the new installer runs silently, closes Desk Wheel, upgrades it in place and starts it again.
- **Mac:** the new app is copied out of the DMG; after Desk Wheel quits, it's swapped in (the old one is put back if anything goes wrong) and opened.

Updates are safe by design: the update feed must be signed with Desk Wheel's private key (the public half is `platform/update-public-key.pem`, built into the app), and every download must match the SHA-256 in the feed. Anything else is ignored.

**One-time setup:**

1. Installed apps read `https://github.com/irfanaslam-io/desk-wheel/releases/latest/download/update.json`: each release is published in this repository, with no extra repo or token.
2. In this repo's **Settings › Secrets and variables › Actions**, add:
   - `UPDATE_SIGNING_KEY`: the private key (the whole `update-signing-key.pem`, including the BEGIN/END lines). Keep a backup somewhere safe: without it, future updates can't be signed.
   - `GA_API_SECRET` (optional, turns on usage statistics): see Privacy below.

**Each release:** set `<Version>` in `src/Wheel.App/Wheel.App.csproj`, commit, then tag and push:

```bash
git tag -a v0.1.0 -m "What's new: …"   # the message becomes the release notes shown in the app
git push origin v0.1.0
```

GitHub Actions builds the Mac DMG and both Windows installers, writes and signs `update.json`, and publishes them as a release. Installed copies pick it up within about 12 hours.

To use a different address for the feed, build with `WHEEL_UPDATE_FEED=https://…/update.json`; set it to an empty value to switch in-app updates off.

## Privacy and usage statistics

Desk Wheel works fully offline. Optionally, and only if the person says yes when asked (or later in Settings › Privacy), it sends **anonymous usage counts** to Google Analytics 4:

- **Sent:** the tool's code (e.g. `img.to-png`, `pdf.compress`), which Desk Wheel screens are opened (e.g. `settings-updates`, `clipboard`), roughly how many files ("2-5"), the screenshot type, the app version, Mac or Windows, and a random id made on the computer.
- **Never sent:** files, file names, folders, screenshots, clipboard contents or anything typed. Every value is checked against a strict pattern (short lowercase codes and numbers only) before it's even queued, so a name or path can't slip through by mistake.

Events wait in a small file and go out in batches, so it costs nothing while offline. Turning statistics off deletes anything not yet sent.

Desk Wheel reports into irfanaslam.io's Google Analytics property (`G-9X34YKZZL5`, built in by default), next to the website, so there is no separate analytics account. To switch it on, open GA4 › Admin › Data streams › the irfanaslam.io stream › *Measurement Protocol API secrets*, create a secret named "Desk Wheel", and add it as the `GA_API_SECRET` Actions secret (or set `WHEEL_GA_API_SECRET` when building locally; `WHEEL_GA_MEASUREMENT_ID` / the `GA_MEASUREMENT_ID` secret can point it at another property). Without the secret, no statistics code runs and nobody is asked.

Every app event starts with `dw_` and carries `app_name = desk-wheel`, so the app and the website never mix in reports:

| Event | Parameters | When |
| --- | --- | --- |
| `dw_app_start` | | the app starts |
| `dw_screen` | `screen_name` (`settings-general`, `settings-updates`, `clipboard`, `history`, `editor`, `wheel-convert`, `wheel-tools`, `snip`, `pin`, `preview-card`…) | a Desk Wheel screen opens |
| `dw_tool_used` | `tool`, `files` | a conversion or tool runs |
| `dw_screenshot` | `kind` | a screenshot is taken |
| `dw_snip` | `action` | a snip is copied, saved, pinned… |
| `dw_update_started` | `to_version` | an in-app update starts |

Every event also has `app_version`, `os` and `arch`. In GA4 › Admin › Custom definitions, register `app_name`, `screen_name`, `tool` and `kind` as event-scoped custom dimensions; then in Explore filter on *Event name starts with `dw_`* (or `app_name = desk-wheel`). To keep app events out of the website's own reports, add a comparison or filter excluding `app_name = desk-wheel`.

## Build and run on Windows

```powershell
winget install Microsoft.DotNet.SDK.10      # once
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\dist\DeskWheel-win-x64\DeskWheel.exe
```

`build.ps1` downloads an LGPL build of FFmpeg and ships it beside DeskWheel.exe (and in the installer), so nothing else needs installing.

The tray icon's menu has: on/off, work in all apps, start at login, add to "Send to", and quit.

Or push to GitHub: `.github/workflows/build.yml` tests the core, then builds `DeskWheel.app` (Apple silicon) and `DeskWheel.exe` (x64 and ARM64).

## Look and feel

Light and dark themes: **Settings → General → Theme** (Match system, Light, Dark). *Match system* follows macOS / Windows and switches the moment you change it there; open windows recolour without restarting.

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

## Known limits

- Drags of email attachments or browser images (virtual files) aren't accepted yet. Save them first.
- Scrolling screenshots and offline translation aren't built (see the ideas list).
- Colour picking and choosing a recording area work on the screen under the pointer; the top strip under the macOS menu bar can't be picked.
- Windows: drags from an app running as administrator can't reach Desk Wheel (a Windows security rule).
- With "Work in all apps" on, holding the mouse and tapping a key (for example, to select text) can briefly open an empty wheel. It closes when you let go.

## Licence

Desk Wheel is **free for individuals and small businesses**, under your choice of the [PolyForm Small Business or PolyForm Noncommercial License 1.0.0](LICENSE.md):

- **Free:** personal use; freelancers and companies with **fewer than 100 people and under US$1 million** revenue a year; and charities, schools, universities, public research and health organisations and government institutions of any size. You may read, change and share the code too.
- **Larger companies** need a commercial licence: see [COMMERCIAL.md](COMMERCIAL.md) or write to hello@irfanaslam.io.
- Because it limits use by larger companies, Desk Wheel is "source-available" rather than "open source" in the OSI sense.

Copyright © 2026 Muhammad Irfan Aslam ([irfanaslam.io](https://irfanaslam.io)).

Third-party components keep their own licences, and all of them allow this:

- **FFmpeg** is an **LGPL** build: no GPL or "nonfree" parts (the build scripts refuse to ship one that has them). It's a separate program beside Desk Wheel, with its licence (`FFmpeg-LICENSE.txt`) and where to get its source (`FFmpeg-SOURCE.txt`) next to it, which is what the LGPL asks for. H.264 video uses the system's own encoder (VideoToolbox on Mac, Media Foundation on Windows), so no x264 (GPL) is needed.
- PDF passwords and compression use **PDFsharp** (MIT); PDF rendering uses **PDFium** (BSD/Apache); QR codes use **ZXing.Net** (Apache-2.0); the Markdown reader uses marked, DOMPurify, highlight.js and GitHub's CSS (MIT/BSD/Apache).
- **LibreOffice** isn't shipped: it's over 400 MB. Office → PDF uses Microsoft Office when it's installed, and LibreOffice otherwise.

See THIRD-PARTY-NOTICES.md for the full list.

---

Powered by [irfanaslam.io](https://irfanaslam.io) · Support: [hello@irfanaslam.io](mailto:hello@irfanaslam.io)
