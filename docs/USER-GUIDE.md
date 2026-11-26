# Desk Wheel user guide

Everything Desk Wheel can do, and how. New here? Start with [Getting started](../README.md#getting-started) in the README.

## Contents

- [How to use](#how-to-use)
- [Screenshots](#screenshots)
- [Clipboard history](#clipboard-history)
- [Screen tools](#screen-tools)
- [Copying text from the screen (OCR)](#copying-text-from-the-screen-ocr)
- [Viewing pictures](#viewing-pictures)
- [Reading Markdown files](#reading-markdown-files)
- [Reading JSON files](#reading-json-files)
- [Editing screenshots](#editing-screenshots)
- [More file tools](#more-file-tools)
- [History](#history)
- [Office files](#office-files)
- [Settings](#settings)
- [Known limits](#known-limits)
- [Look and feel](#look-and-feel)

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

Easiest: **click the orange button**. The wheel opens on **Screenshot**: Area, Window, Full, Scroll, Text, Clipboard, Record, then **More ›** for GIF, Colour, Ruler, QR, Make QR, 5 s, 10 s, Files, History, Disk and Settings. Wheels with more than eight choices have a **More ›** segment; while dragging a file, rest on it for a moment to turn the page. **Right-click any segment to pin it** to the front of its wheel (★); right-click again to unpin. **Click the centre** to step through Screenshot → Convert → Tools → Actions; dots under the name show where you are. Right-clicking the button captures an area straight away.

- Screenshots are saved to **Pictures → Desk Wheel Screenshots** and copied to the clipboard.
- A card appears bottom-left with **Copy**, **Edit**, **Text**, **Pin** (keeps it floating on top), **Desk Wheel** (convert it) and **Show**. You can also drag the thumbnail into any app.
- **Mac:** uses the system's own selection screen (as ⌘⇧4: Space switches to window mode, Esc cancels). Needs the **Screen Recording** permission once.
- **Windows:** area and window screenshots use Desk Wheel's own **mark-up overlay** (below), never the Snipping Tool; full screen is captured directly. For **Window**, the window under the pointer is outlined: click it to take it (or drag over any area instead). With *Mark up area screenshots right away* turned off in Settings, the picture is saved as soon as you pick the area or window.

### Mark up while you capture (area and window screenshots)

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
- **GIF:** the same as Record, but saved as an animated GIF (12 frames a second, at most 960 px wide) for chats, tickets and docs.
- **Scroll:** a scrolling screenshot. Drag over the part of the window that scrolls (a web page, a chat, a long document), then scroll down slowly with the mouse wheel or trackpad; the bar beside it shows how tall the picture is so far. Click **Done**: one tall picture opens on the screenshot card. Menus and banners that stay put while you scroll appear once. If you scroll too fast it says so: scroll back up a little and carry on.
- **Ruler:** drag over anything on screen to measure it. The size (in pixels) is copied each time; on a Mac's Retina screen the size in points is shown too. Esc closes it.
- **10 s:** like 5 s, with time to open a menu or hover over something first.
- **Disk:** what's taking up space (see below).

### Media player

Choose **Play** on the Tools wheel of a video or song (the first choice). One file plays with the rest of its folder as the playlist; several play in the order you chose them. Drop more files on the player to add them.

The first time, Desk Wheel asks to download the player's engine: VLC by VideoLAN, free and open source, about 31 MB on a Mac and 43–48 MB on Windows, from Desk Wheel's own GitHub page. Each download is checked against a checksum built into Desk Wheel; anything else is thrown away. Not now? **Open in the system player** instead. Remove the engine any time in **Settings → Updates → Optional downloads**.

It plays every format VLC does (MKV, MP4, AVI, MOV, WebM, FLAC, MP3, OGG…), with:

- **Subtitles**: a subtitle file with the same name next to the video is used automatically; choose tracks, add a file, or shift timing earlier or later;
- **Speed** from 0.25× to 3× (voices keep their pitch), **audio tracks**, and an **equalizer** with presets;
- **A-B repeat** (R: first press marks the start, second the end, third turns it off);
- **Save frame** (P) as a picture in your screenshots folder, **full screen** (F or double-click), **keep on top**;
- **carries on where you stopped** in videos longer than ten minutes (Home starts over).

Keys: Space play/pause · ← → 10 s (Shift: 1 minute) · ↑ ↓ volume · M mute · [ ] slower/faster · N / B next and previous · S next subtitle · L playlist · . one frame (paused) · Esc leaves full screen, then closes.

### Disk space

Open it from the Screenshot wheel (**Disk**), the tray menu (**Disk space…**), or the Actions wheel on any folder (**Sizes**). It scans your home folder (or the folder you chose; **Scan…** picks a drive or any folder) and shows:

- **a list**, biggest first, with sizes and how many files each folder holds;
- **a ring chart**: the folder in the middle, what's inside it around it, each wedge as big as its share. Hover a wedge to see what it is, click a folder to go into it, click the middle to go back up. Scanning a whole drive shows its free space in grey;
- **Largest files**: the biggest files anywhere in the folder, in one list.

Select items (Ctrl / ⌘-click and Shift-click for several; the arrow keys work too), then **Open**, **Show** in Finder / File Explorer, **Rename**, **ZIP**, **Tools** (the Desk Wheel for those items: convert, compress, share), copy the path, or **Trash** / **Delete**. Right-click for the same choices.

Nothing is ever deleted for good: items go to the Trash (Mac, with Put Back) or Recycle Bin (Windows), and Desk Wheel asks first (click again to confirm). To clean up across several folders, **Collect** items (or drag them onto the tray at the bottom right, or press C), check them with **Review**, then move them all at once.

On a Mac, a few protected folders (Mail, Messages, Safari) can only be measured if you allow Desk Wheel **Full Disk Access** in System Settings → Privacy & Security; the size line says when something couldn't be read.
- **Colour:** the screen freezes and a magnifier follows the pointer; click to copy the colour as HEX (the card also shows RGB and HSL).
- **QR:** drag over a QR code or barcode anywhere on screen; what it says is copied. Links can be opened from the card.
- **Make QR:** copy a link or some text, then choose Make QR: the code appears on the screenshot card, ready to copy, pin or drag out.

## Copying text from the screen (OCR)

Reads text in English and Arabic, on your computer. Nothing is uploaded.

- **Screenshot wheel → Text:** drag over anything on screen (a video, an image, a locked PDF, an error message) and its text is copied to the clipboard. Nothing is saved to your screenshots folder.
- **Screenshot card → Text:** copies the text in the screenshot you just took.
- **Tools → Text** on an image: copies its text. With several images, or a folder of them, a `.txt` is saved beside each.
- **Convert → TXT** on a scanned PDF (pages that are pictures) now reads the pages instead of giving an empty file.
- **Tools → Searchable** on a scanned PDF: saves `name (searchable).pdf`, which looks exactly the same but has the words as invisible text on top of the picture, in the right places. Search it with Ctrl+F (⌘F), select and copy its text, or let Windows and Spotlight find it by its words. Pages that already have text are left as they are. Works with English and Arabic.

Engines:
- **Mac:** Apple Vision, the same engine as Live Text. `build-mac.sh` builds a small helper for it, which needs Xcode's command-line tools (`xcode-select --install`). Which languages Vision reads depends on the macOS version; run `./debug-mac.sh` to see them listed in the log.
- **Windows:** the OCR built into Windows 10 and 11. For Arabic, add Arabic in Settings → Time & language → Language.
- **Either:** if neither is available, Tesseract is used when installed (`brew install tesseract tesseract-lang`).

## Viewing pictures

Desk Wheel has a picture viewer that steps through a whole folder:

- **From the wheel:** select a picture, then **Tools → View**. Select a folder, or several pictures, to view just those.
- **From File Explorer (Windows):** right-click a picture › **Open with › Desk Wheel**. To use it every time, choose Desk Wheel for pictures in *Settings › Apps › Default apps*.
- **Next and Back:** the **→ / ←** keys (also Space, Page Up / Page Down, the mouse's back and forward buttons) or the round buttons at the sides. **Home / End** jump to the first or last picture. It goes round from the last picture to the first.
- **Zoom:** the mouse wheel zooms where the pointer is; drag to move around; **double-click**, **0** (fit) and **1** (actual size) switch between the whole picture and 100 %. **+ / −** zoom too.
- **F11** (or **F**) for full screen, **Esc** to leave it or close. **E** opens the picture in the editor, **Ctrl+C** copies it, and the folder button shows it in File Explorer or Finder.
- Pictures are shown in the same order as File Explorer (photo 2 before photo 10), and the next and previous ones are loaded in advance, so stepping through is instant. HEIC, AVIF and TIFF open too.

## Reading Markdown files

Select a `.md` file (a README, notes, docs) and click the button, then the centre once: **Convert → Read** opens it as a formatted page in your browser, the way GitHub shows a README.

- GitHub's own reading style, in light or dark to match your system.
- A contents list beside the text, which follows you as you scroll.
- Coloured code with a **Copy** button, tables, task lists, and GitHub alerts (`> [!NOTE]`).
- Arabic paragraphs read right to left.
- Images next to the file show up. **Print / PDF** saves it as a PDF.
- Works offline. Scripts inside the file are removed before the page is shown.

## Reading JSON files

Select a `.json` file (also `.jsonc`, `.geojson` and `.har`) and open the **Convert** wheel:

- **View** opens it as a readable page in your browser: coloured keys and values, folding (click ▸ or the count), **Expand all / Collapse**, and a **Text** view of the formatted JSON. **Search** ( **/** ) finds keys and values, opens the parts that contain them and steps through them with Enter. Hover a line to see its path (`$.users[0].email`); click a key to copy the path, double-click a value to copy it. Light or dark to match your system, and it works offline.
- **Format** saves an indented copy (`name (formatted).json`) and **Minify** a one-line copy, both keeping the key order and numbers exactly as they were.
- **CSV** turns a list of records into a spreadsheet: one row per item, one column per key. Nested values go into the cell as JSON.
- **HTML** saves the viewer page beside the file, to keep or share.
- Comments and trailing commas (as in VS Code settings files) are accepted. If the file has a mistake, the page shows the line and column.

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
| Square, Flip, Grey | Tools (images) | Square crop from the middle (profile pictures), mirror left to right, black and white |
| Watermark… | Tools (images) | Type a line (your name, © 2026, any language, Arabic included); it goes in the bottom-right corner, white with a soft edge so it shows on any picture. Works on many pictures at once |
| Row, Column, Grid | Tools (2 or more images) | One picture from several: side by side, stacked, or a grid collage, in name order. Saved as `Combined (row).jpg` |
| Favicon | Tools (images) | Website icons in a `name (favicon)` folder: favicon.ico (16, 32, 48), PNGs for Apple and Android, `site.webmanifest`, and `favicon.html` with the lines to paste into your page |
| Base64 | Actions (one image) | Copies the picture as a data URL (`data:image/png;base64,…`) for HTML, CSS or code |
| Pages… | Tools (PDF) | Keep the pages you type, in that order: `1-3, 7`, `10-end`, `end-1` (backwards), `odd`, `even`. Extracts, reorders or reverses |
| Searchable | Tools (PDF) | Reads a scanned PDF and adds its words as invisible text: the pages look the same, but the PDF can be searched, and its text selected and copied (English and Arabic). Pages that already have text are skipped. Saved as `name (searchable).pdf` |
| Delete… | Tools (PDF) | Remove the pages you type: `2, 5-7` |
| Watermark… | Tools (PDF) | Your text (CONFIDENTIAL, DRAFT, any language) in light grey across the middle of every page; the PDF's own text stays selectable |
| Images | Tools (PDF) | Saves every picture inside the PDF as PNG in a `name (images)` folder (each picture once) |
| Grey | Tools (PDF) | A black-and-white copy for printing. Pages become pictures (200 DPI) |
| Flatten | Tools (PDF) | Locks in form entries and comments: pages become pictures that can't be edited (200 DPI) |
| Clean | Tools (PDF) | Removes hidden details: author, title and other document details, XMP metadata, scripts and attached files. Pages are unchanged |
| Numbers | Tools (PDF) | Page numbers "1 / 12" at the bottom of every page |
| Compress | Tools (PDF) | Smaller PDF: pictures re-saved at print quality (about 150 DPI), text and drawings untouched |
| Lock / Unlock | Tools (PDF) | Add or remove a password, AES-256 |
| Extract, ZIP, TAR.GZ | Convert (archives) | Opens RAR (4 and 5), 7z, ZIP, TAR, TAR.GZ / .BZ2 / .XZ / .LZ, single GZ / BZ2 / XZ files and ISO disc images, and converts any of them to ZIP or TAR.GZ. Password-protected archives ask for the password. An archive that tries to put files outside its folder is refused and nothing is extracted. Desk Wheel can open RAR files but can't make them (only WinRAR can) |
| ZIP, Lock ZIP | Tools (any files) | One ZIP of everything selected; **Lock ZIP** adds a password (AES-256). Password ZIPs open in Desk Wheel, 7-Zip and WinRAR, but not in File Explorer's or macOS's built-in ZIP support |
| Checksum | Actions (any files) | Copies the SHA-256 and MD5 of a file, to check a download. Several files: one line each, also saved as `checksums.sha256` |
| Words | Tools (TXT, MD, DOCX, ODT, HTML, CSV, SRT…) | Counts words, characters, lines and paragraphs, with a reading time; the summary is copied too |
| Join | Tools (2 or more TXT, MD, CSV or LOG files of one kind) | One file in name order: `Joined.txt`. CSV files keep a single header row |
| JSON | Convert (CSV) | One JSON object per row, keyed by the header row; numbers stay numbers, `0050` stays text |
| Clean | Tools (DOCX, XLSX, PPTX) | A copy without the author, last editor, company, template, editing time, custom properties and thumbnail. The content is untouched |
| Trim… | Tools (video, audio) | Keep the part you type: `0:30-1:45`, `1:00-` (to the end), `-0:20` (the first 20 seconds), or seconds like `90-120`. Saved as `name (trimmed)`. Audio, and video kept from the start, are copied straight across (fast, no quality loss); video starting later is re-encoded so it begins on the exact frame |
| Join | Tools (2 or more videos, or 2 or more audio files) | One file in name order: `Joined.mp4`. Matching clips (from the same phone or camera) are copied straight across; different ones are re-encoded to MP4 at the first clip's size |
| Speed… | Tools (video, audio) | Type `2` (twice as fast), `0.5` (half speed) or `150%`, from 0.25 to 4. Voices keep their pitch. Saved as `name (2x)` |
| Rotate, Flip | Tools (video) | Turn 90° clockwise (for phone videos filmed sideways), or mirror left to right. Saved as MP4 |
| 720p, 1080p | Tools (video) | A smaller copy for sharing. Videos already that size or smaller are left at their size, never enlarged |
| Sheet | Tools (video) | 16 frames from through the video on one picture, 4 × 4, as `name (sheet).jpg`: see what's in a video at a glance |
| Subtitles | Tools (one video + one SRT or VTT) | Adds the subtitles to the video as a track you can turn on and off in the player. Nothing is re-encoded. MP4, MOV and MKV keep their type; others become MKV |
| Burn in | Tools (one video + one SRT or VTT, Windows) | Draws the subtitles onto the picture, so they show in every player and on social media. Arabic and other languages work |
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
| Screenshot | Area, Window, Full, Scroll, Text, Clipboard, Record, GIF, Colour, Ruler, QR, Make QR, 5 s, 10 s, Files, History, Disk, Settings |
| Convert | Images: PNG, JPG, WebP, HEIC (Mac), AVIF, ICO, TIFF, GIF, BMP · PDF → PNG / JPG (every page) / TXT (scanned PDFs too) · Video: MP4, WebM, MKV, MOV, GIF, MP3, M4A · Audio: MP3, M4A, WAV, FLAC, OGG, Opus · ZIP ↔ TAR.GZ, Extract · SRT ↔ VTT, TXT · Office: PDF, DOCX, XLSX, PPTX, CSV… · Markdown: Read, HTML |
| Tools | Images: Compress, 50%, Rotate, No EXIF, Edit, Text, Images → PDF · PDF: Merge, Pages…, Delete…, Rotate, Compress, Searchable, Watermark…, Lock, Unlock, Split, Numbers, Images, Grey, Flatten, Clean · Video: Trim…, Compress, 50%, Mute, Frame, Join, Speed…, Rotate, Flip, 720p, 1080p, Sheet · Video + SRT: Subtitles, Burn in · Audio: Trim…, Join, Speed…, Normalize, Mono, Compress · Markdown: Read · ZIP any selection |
| Actions | Copy path, Open terminal here, Reveal, Copy to Desktop, Copy name |

With the optional keyboard gesture on, Shift / ⌘ (Ctrl) / ⌃ (Alt) also switch to Convert / Tools / Actions. Esc closes the wheel.

## Known limits

- Drags of email attachments or browser images (virtual files) aren't accepted yet. Save them first.
- Scrolling screenshots and offline translation aren't built (see the ideas list).
- Burn in (subtitles drawn onto the video) isn't offered on Mac yet: the Mac's video engine is built without the subtitle renderer. Subtitles (as a track) works on both.
- Colour picking and choosing a recording area work on the screen under the pointer; the top strip under the macOS menu bar can't be picked.
- Windows: drags from an app running as administrator can't reach Desk Wheel (a Windows security rule).
- With "Work in all apps" on, holding the mouse and tapping a key (for example, to select text) can briefly open an empty wheel. It closes when you let go.

## Look and feel

Light and dark themes: **Settings → General → Theme** (Match system, Light, Dark). *Match system* follows macOS / Windows and switches the moment you change it there; open windows recolour without restarting.
