# Third-party software in DeskWheel

Desk Wheel's own code is licensed under the PolyForm Small Business or PolyForm Noncommercial License 1.0.0, at the user's choice (see LICENSE.md). It builds on these open-source projects, which keep their own licences:

| Component | Used for | Licence | Shipped with DeskWheel |
| --- | --- | --- | --- |
| [.NET](https://github.com/dotnet/runtime) | Runtime | MIT | Yes |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | User interface on macOS and Windows | MIT | Yes |
| [SkiaSharp](https://github.com/mono/SkiaSharp) / Skia | Images, PDF creation | MIT / BSD-3-Clause | Yes |
| [PDFium](https://pdfium.googlesource.com/pdfium/) via [pdfium-binaries](https://github.com/bblanchon/pdfium-binaries) | Reading, rendering, merging and splitting PDFs | BSD-3-Clause and Apache-2.0 | Yes (`engines/`, licence text beside the library) |
| [FFmpeg](https://ffmpeg.org) 7.1 | Video and audio, screen recording | LGPL-2.1+ (built without GPL or nonfree parts) | Yes, as a separate program (`engines/ffmpeg`); licence and source notes alongside |
| [LAME](https://lame.sourceforge.io) 3.100 | MP3 (inside the Mac FFmpeg) | LGPL-2.0+ | Yes, inside FFmpeg |
| [libvpx](https://chromium.googlesource.com/webm/libvpx), [libaom](https://aomedia.googlesource.com/aom), [Opus](https://opus-codec.org), [Ogg/Vorbis](https://xiph.org), [libwebp](https://chromium.googlesource.com/webm/libwebp) | VP9, AV1, Opus, Vorbis, WebP (inside the Mac FFmpeg) | BSD-style | Yes, inside FFmpeg |
| [PDFsharp](https://github.com/empira/PDFsharp) 6.2 | PDF passwords and compression | MIT | Yes (NuGet package) |
| sips | HEIC, TIFF, GIF, BMP on macOS | Part of macOS | No: built into macOS |
| [marked](https://github.com/markedjs/marked) 15.0.12 | Markdown reader: Markdown to HTML | MIT | Yes (`engines/markdown/marked.js`) |
| [DOMPurify](https://github.com/cure53/DOMPurify) 3.5.0 | Markdown reader: removes scripts from rendered pages | Apache-2.0 or MPL-2.0 | Yes (`engines/markdown/purify.js`) |
| [highlight.js](https://github.com/highlightjs/highlight.js) 11.12.0 | Markdown reader: code colouring | BSD-3-Clause | Yes (`engines/markdown/highlight.js`, `highlight-*.css`) |
| [github-markdown-css](https://github.com/sindresorhus/github-markdown-css) 5.9.0 | Markdown reader: GitHub's reading style | MIT | Yes (`engines/markdown/github-markdown.css`) |
| Apple Vision / Windows OCR | Text recognition | Part of macOS / Windows | No: built into the system |
| [Tesseract](https://github.com/tesseract-ocr/tesseract) | Text recognition when the system has none | Apache-2.0 | No: used only if installed |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) 0.50.8 | Opening RAR, 7z, TAR, GZ, BZ2, XZ and LZ archives | MIT | Yes (NuGet package) |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) 1.4.2 | ZIPs with a password (AES-256) | MIT | Yes (NuGet package) |
| [DiscUtils](https://github.com/DiscUtils/DiscUtils) 0.16.13 | Opening ISO disc images | MIT | Yes (NuGet package) |
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) 0.16.11 | Reading and making QR codes and barcodes | Apache-2.0 | Yes (NuGet package) |
| [SkiaSharp.HarfBuzz](https://github.com/mono/SkiaSharp) 2.88.9 | Joined Arabic text in the editor and watermarks | MIT | Yes (NuGet package) |
| [LibreOffice](https://www.libreoffice.org) | Office conversions | MPL-2.0 | No: used only if installed |
