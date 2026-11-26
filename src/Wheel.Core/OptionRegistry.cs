namespace Wheel.Core;

/// <summary>
/// The catalogue of wheel segments, and the rule for which ones show for a given drag.
/// Rule: options valid for every dragged item (intersection by kind); Actions always show.
/// A "convert to X" option is hidden only when every dragged file is already X.
/// </summary>
public static class OptionRegistry
{
    public const int MaxSegments = 8;

    private static WheelOption C(string id, string label, string hint, EngineKind e, string? ext = null, Requirement needs = Requirement.None) =>
        new(id, label, hint, WheelMode.Convert, e, ext, Needs: needs);

    private static WheelOption O(string id, string label, string hint, string ext, Requirement needs) =>
        new(id, label, hint, WheelMode.Convert, EngineKind.Office, ext, Needs: needs);

    private static WheelOption T(string id, string label, string hint, EngineKind e, string? ext = null, bool many = false, int min = 1) =>
        new(id, label, hint, WheelMode.Tools, e, ext, many, MinItems: min);

    private static readonly Dictionary<FileKind, WheelOption[]> ByKind = new()
    {
        [FileKind.Image] =
        [
            C("img.to-png", "PNG", "Convert to PNG (lossless)", EngineKind.Image, ".png"),
            C("img.to-jpg", "JPG", "Convert to JPG (quality 90)", EngineKind.Image, ".jpg"),
            C("img.to-webp", "WebP", "Convert to WebP (quality 85)", EngineKind.Image, ".webp"),
            C("img.to-heic", "HEIC", "Convert to HEIC (small, Apple-friendly)", EngineKind.Image, ".heic", Requirement.Sips),
            C("img.to-avif", "AVIF", "Convert to AVIF", EngineKind.Ffmpeg, ".avif"),
            C("img.to-ico", "ICO", "Convert to icon (256 px)", EngineKind.Image, ".ico"),
            C("img.to-tiff", "TIFF", "Convert to TIFF", EngineKind.Image, ".tiff", Requirement.ImageFallback),
            C("img.to-gif", "GIF", "Convert to GIF", EngineKind.Image, ".gif", Requirement.ImageFallback),
            C("img.to-bmp", "BMP", "Convert to BMP", EngineKind.Image, ".bmp", Requirement.ImageFallback),
            T("img.compress", "Compress", "Compress as JPG, quality 70", EngineKind.Image, ".jpg"),
            T("img.resize-half", "50%", "Resize to half width and height", EngineKind.Image),
            T("img.resize", "Size…", "Resize to a size you type: 1920x1080, 1080, x720 or 25%", EngineKind.Image, many: true),
            T("img.rotate", "Rotate", "Rotate 90° clockwise", EngineKind.Image),
            T("img.strip", "No EXIF", "Remove metadata (GPS, camera, dates)", EngineKind.Image),
            T("img.view", "View", "Picture viewer: Next and Back through the folder, zoom, full screen", EngineKind.Window, many: true),
            T("img.edit", "Edit", "Mark up: arrows, text, blur, redact, crop", EngineKind.Image),
            T("img.square", "Square", "Crop to a square from the middle (profile pictures)", EngineKind.Image),
            T("img.flip", "Flip", "Mirror left to right", EngineKind.Image),
            T("img.grey", "Grey", "Black and white", EngineKind.Image),
            T("img.watermark", "Watermark…", "Your name or text in the corner (type it)", EngineKind.Image, many: true),
            T("img.row", "Row", "Join the pictures side by side into one", EngineKind.Image, many: true, min: 2),
            T("img.column", "Column", "Stack the pictures into one, top to bottom", EngineKind.Image, many: true, min: 2),
            T("img.grid", "Grid", "A grid collage of the pictures", EngineKind.Image, many: true, min: 2),
            T("img.ocr", "Text", "Copy the text in the image (English and Arabic)", EngineKind.Ocr),
            T("img.cutout", "Cut out", "Remove the background, keep the subject (PNG)", EngineKind.Vision, ".png"),
            T("img.icons", "Icons", "App icon set: PNGs from 16 to 1024 px, ICO and (Mac) ICNS", EngineKind.Image),
            T("img.favicon", "Favicon", "Website icons: favicon.ico, Apple and Android sizes, and the HTML to add", EngineKind.Image),
            T("img.to-pdf", "PDF", "Combine the images into one PDF", EngineKind.Image, ".pdf", many: true),
        ],
        [FileKind.Pdf] =
        [
            C("pdf.to-png", "PNG", "Every page as PNG (300 DPI)", EngineKind.Pdf, ".png"),
            C("pdf.to-jpg", "JPG", "Every page as JPG (300 DPI)", EngineKind.Pdf, ".jpg"),
            C("pdf.to-txt", "TXT", "Extract the text", EngineKind.Pdf, ".txt"),
            T("pdf.merge", "Merge", "Combine into one PDF (in name order)", EngineKind.Pdf, ".pdf", many: true, min: 2),
            T("pdf.pages", "Pages…", "Keep or reorder pages: type 1-3, 7 or 10-end", EngineKind.PdfEdit, ".pdf"),
            T("pdf.delete", "Delete…", "Remove pages: type 2, 5-7", EngineKind.PdfEdit, ".pdf"),
            T("pdf.rotate", "Rotate", "Rotate every page 90° clockwise", EngineKind.Pdf, ".pdf"),
            T("pdf.compress", "Compress", "Make it smaller: pictures at print quality, text untouched", EngineKind.PdfEdit, ".pdf"),
            T("pdf.ocr", "Searchable", "Make a scanned PDF searchable: its text can be found, selected and copied (English and Arabic)", EngineKind.Ocr, ".pdf"),
            T("pdf.watermark", "Watermark…", "Text such as CONFIDENTIAL or DRAFT across every page", EngineKind.PdfEdit, ".pdf"),
            T("pdf.protect", "Lock", "Add a password (AES-256)", EngineKind.PdfEdit, ".pdf"),
            T("pdf.unlock", "Unlock", "Remove the password (you'll need it)", EngineKind.PdfEdit, ".pdf"),
            T("pdf.split", "Split", "One PDF per page", EngineKind.Pdf),
            T("pdf.numbers", "Numbers", "Add page numbers (1 / 12) at the bottom", EngineKind.Pdf, ".pdf"),
            T("pdf.images", "Images", "Save the pictures inside as PNG files", EngineKind.PdfEdit),
            T("pdf.grey", "Grey", "Black-and-white copy for printing (pages become pictures)", EngineKind.PdfEdit, ".pdf"),
            T("pdf.flatten", "Flatten", "Lock in forms and comments: pages become pictures that can't be edited", EngineKind.PdfEdit, ".pdf"),
            T("pdf.clean", "Clean", "Remove hidden details: author, metadata, scripts, attached files", EngineKind.PdfEdit, ".pdf"),
        ],
        [FileKind.Video] =
        [
            C("vid.to-mp4", "MP4", "Convert to MP4 (H.264 + AAC)", EngineKind.Ffmpeg, ".mp4"),
            C("vid.to-webm", "WebM", "Convert to WebM (VP9 + Opus)", EngineKind.Ffmpeg, ".webm"),
            C("vid.to-mkv", "MKV", "Repackage as MKV (no re-encode)", EngineKind.Ffmpeg, ".mkv"),
            C("vid.to-mov", "MOV", "Convert to MOV", EngineKind.Ffmpeg, ".mov"),
            C("vid.to-gif", "GIF", "Animated GIF, 480 px, 12 fps", EngineKind.Ffmpeg, ".gif"),
            C("vid.to-mp3", "MP3", "Extract audio as MP3", EngineKind.Ffmpeg, ".mp3"),
            C("vid.to-m4a", "M4A", "Extract audio as M4A", EngineKind.Ffmpeg, ".m4a"),
            T("vid.play", "Play", "Desk Wheel's player: every format, subtitles, speed, playlist of the folder", EngineKind.Window, many: true),
            T("vid.trim", "Trim…", "Keep the part you type, like 0:30-1:45 or 1:00- (to the end)", EngineKind.Ffmpeg),
            T("vid.compress", "Compress", "Compress (H.264, CRF 28)", EngineKind.Ffmpeg, ".mp4"),
            T("vid.half", "50%", "Half resolution", EngineKind.Ffmpeg, ".mp4"),
            T("vid.mute", "Mute", "Remove the audio track", EngineKind.Ffmpeg),
            T("vid.snapshot", "Frame", "Save the frame at 1 s as PNG", EngineKind.Ffmpeg, ".png"),
            T("vid.join", "Join", "One video from several, in name order", EngineKind.Ffmpeg, many: true, min: 2),
            T("vid.speed", "Speed…", "Faster or slower: type 2, 1.5 or 0.5 (sound keeps its pitch)", EngineKind.Ffmpeg, ".mp4"),
            T("vid.rotate", "Rotate", "Rotate 90° clockwise", EngineKind.Ffmpeg, ".mp4"),
            T("vid.flip", "Flip", "Mirror left to right", EngineKind.Ffmpeg, ".mp4"),
            T("vid.720", "720p", "Make it 720p (smaller videos stay as they are)", EngineKind.Ffmpeg, ".mp4"),
            T("vid.1080", "1080p", "Make it 1080p (smaller videos stay as they are)", EngineKind.Ffmpeg, ".mp4"),
            T("vid.sheet", "Sheet", "16 frames from through the video on one picture (JPG)", EngineKind.Ffmpeg, ".jpg"),
        ],
        [FileKind.Audio] =
        [
            C("aud.to-mp3", "MP3", "Convert to MP3 (192 kbps)", EngineKind.Ffmpeg, ".mp3"),
            C("aud.to-m4a", "M4A", "Convert to M4A (AAC 192 kbps)", EngineKind.Ffmpeg, ".m4a"),
            C("aud.to-wav", "WAV", "Convert to WAV (lossless)", EngineKind.Ffmpeg, ".wav"),
            C("aud.to-flac", "FLAC", "Convert to FLAC (lossless)", EngineKind.Ffmpeg, ".flac"),
            C("aud.to-ogg", "OGG", "Convert to OGG Vorbis", EngineKind.Ffmpeg, ".ogg"),
            C("aud.to-opus", "Opus", "Convert to Opus", EngineKind.Ffmpeg, ".opus"),
            T("aud.play", "Play", "Desk Wheel's player: the folder as a playlist, equalizer, speed, A-B repeat", EngineKind.Window, many: true),
            T("aud.trim", "Trim…", "Keep the part you type, like 0:30-1:45", EngineKind.Ffmpeg),
            T("aud.join", "Join", "One file from several, in name order", EngineKind.Ffmpeg, many: true, min: 2),
            T("aud.speed", "Speed…", "Faster or slower: type 2, 1.5 or 0.5 (pitch kept)", EngineKind.Ffmpeg),
            T("aud.normalize", "Normalize", "Even out loudness (EBU R128)", EngineKind.Ffmpeg),
            T("aud.mono", "Mono", "Mix down to mono", EngineKind.Ffmpeg),
            T("aud.compress", "Compress", "Compress as MP3, 96 kbps", EngineKind.Ffmpeg, ".mp3"),
        ],
        [FileKind.Archive] =
        [
            C("arc.extract", "Extract", "Extract into a folder beside it", EngineKind.Archive),
            C("arc.to-zip", "ZIP", "Convert to ZIP", EngineKind.Archive, ".zip"),
            C("arc.to-targz", "TAR.GZ", "Convert to TAR.GZ", EngineKind.Archive, ".tar.gz"),
        ],
        [FileKind.Document] =
        [
            O("doc.to-pdf", "PDF", "Convert to PDF", ".pdf", Requirement.OfficeSuite),
            O("doc.to-docx", "DOCX", "Convert to Word (.docx)", ".docx", Requirement.OfficeSuiteOrTextutil),
            O("doc.to-txt", "TXT", "Plain text", ".txt", Requirement.OfficeSuiteOrTextutil),
            O("doc.to-html", "HTML", "Web page", ".html", Requirement.OfficeSuiteOrTextutil),
            O("doc.to-odt", "ODT", "OpenDocument text", ".odt", Requirement.OfficeSuiteOrTextutil),
        ],
        [FileKind.Spreadsheet] =
        [
            O("xls.to-pdf", "PDF", "Convert to PDF", ".pdf", Requirement.OfficeSuite),
            O("xls.to-xlsx", "XLSX", "Convert to Excel (.xlsx)", ".xlsx", Requirement.OfficeSuite),
            O("xls.to-csv", "CSV", "First sheet as CSV (UTF-8)", ".csv", Requirement.OfficeSuite),
            O("xls.to-ods", "ODS", "OpenDocument spreadsheet", ".ods", Requirement.OfficeSuite),
        ],
        [FileKind.Presentation] =
        [
            O("ppt.to-pdf", "PDF", "Convert to PDF", ".pdf", Requirement.OfficeSuite),
            O("ppt.to-pptx", "PPTX", "Convert to PowerPoint (.pptx)", ".pptx", Requirement.OfficeSuite),
            O("ppt.to-odp", "ODP", "OpenDocument presentation", ".odp", Requirement.OfficeSuite),
        ],
        [FileKind.Markdown] =
        [
            C("md.read", "Read", "Open as a formatted page, like GitHub", EngineKind.Markdown),
            C("md.to-html", "HTML", "Save as a formatted web page", EngineKind.Markdown, ".html"),
            T("md.read-tools", "Read", "Open as a formatted page, like GitHub", EngineKind.Markdown),
        ],
        [FileKind.Json] =
        [
            C("json.view", "View", "Open as a readable page: fold, search, copy paths", EngineKind.Json),
            C("json.format", "Format", "Indented and easy to read, saved as a copy", EngineKind.Json),
            C("json.minify", "Minify", "On one line, as small as possible", EngineKind.Json),
            C("json.to-csv", "CSV", "A list of records as a spreadsheet (opens in Excel)", EngineKind.Json, ".csv"),
            C("json.to-html", "HTML", "Save the readable page to keep or share", EngineKind.Json, ".html"),
            T("json.view-tools", "View", "Open as a readable page: fold, search, copy paths", EngineKind.Json),
        ],
        [FileKind.Subtitle] =
        [
            C("sub.to-srt", "SRT", "Convert to SubRip (.srt)", EngineKind.Subtitle, ".srt"),
            C("sub.to-vtt", "VTT", "Convert to WebVTT (.vtt)", EngineKind.Subtitle, ".vtt"),
            C("sub.to-txt", "TXT", "Plain text, no timings", EngineKind.Subtitle, ".txt"),
        ],
    };

    /// <summary>Tools any selection gets, including folders and mixed selections.</summary>
    /// <summary>Options that depend on the exact file type, not just its kind (shown when every file passes).</summary>
    private static readonly (WheelOption Option, Func<IReadOnlyList<string>, bool> When)[] ByExtension =
    [
        (C("csv.to-json", "JSON", "Rows as JSON objects, keyed by the header row", EngineKind.Text, ".json"),
            p => p.All(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase))),
        (T("txt.words", "Words", "Count words, characters, lines and reading time", EngineKind.Text, many: true),
            p => p.All(Files.FileTools.CanCount)),
        (T("txt.join", "Join", "One file from several, in name order (CSV keeps one header row)", EngineKind.Text, many: true, min: 2),
            Files.FileTools.CanJoin),
        (T("sub.add", "Subtitles", "Add the subtitles to the video as a track you can turn on and off", EngineKind.Ffmpeg, many: true, min: 2),
            Converters.MediaEditor.IsVideoWithSubtitles),
        (T("sub.burn", "Burn in", "Draw the subtitles onto the picture, so they show everywhere (MP4)", EngineKind.Ffmpeg, ".mp4", many: true, min: 2),
            p => Converters.MediaEditor.IsVideoWithSubtitles(p) && Converters.MediaEditor.CanBurnSubtitles()),
        (T("off.clean", "Clean", "Remove author, company, editing time and other hidden details", EngineKind.Text),
            p => p.All(Files.FileTools.CanCleanOffice)),
    ];

    private static readonly WheelOption[] AnySelectionTools =
    [
        T("any.zip", "ZIP", "Compress everything into one ZIP", EngineKind.Archive, ".zip", many: true),
        T("any.zip-lock", "Lock ZIP", "One ZIP with a password (AES-256). Opens in Desk Wheel, 7-Zip and WinRAR", EngineKind.Archive, ".zip", many: true),
        T("any.rename", "Rename", "Rename in bulk: numbers, dates, find and replace (with preview and undo)", EngineKind.Window, many: true),
    ];

    private static readonly WheelOption[] ActionOptions =
    [
        new("act.copy-file", "Copy", "Copy the file(s): paste into another folder, a chat or an email", WheelMode.Actions, EngineKind.Action, AcceptsMany: true),
        new("act.copy-image", "Image", "Copy the picture itself: paste it into a chat or document", WheelMode.Actions, EngineKind.Action),
        new("act.copy-path", "Path", "Copy full path(s) to the clipboard", WheelMode.Actions, EngineKind.Action, AcceptsMany: true),
        new("act.terminal", "Terminal", "Open a terminal in this folder", WheelMode.Actions, EngineKind.Action),
        new("act.reveal", "Reveal", "Show the file in its folder", WheelMode.Actions, EngineKind.Action),
        new("act.copy-desktop", "Desktop", "Copy to the Desktop", WheelMode.Actions, EngineKind.Action, AcceptsMany: true),
        new("act.copy-name", "Name", "Copy file name(s) without folder", WheelMode.Actions, EngineKind.Action, AcceptsMany: true),
        new("act.checksum", "Checksum", "Copy the SHA-256 and MD5 checksums (several files: also saved as checksums.sha256)", WheelMode.Actions, EngineKind.Action, AcceptsMany: true),
        new("act.disk", "Sizes", "What's taking up space in this folder (or the selection's folder): sizes, chart, clean-up", WheelMode.Actions, EngineKind.Window, AcceptsMany: true),
        new("act.base64", "Base64", "Copy the picture as a data URL (data:image/…;base64), for HTML, CSS or code", WheelMode.Actions, EngineKind.Action),
    ];

    /// <summary>The Screenshot wheel: works with no files at all.</summary>
    private static readonly WheelOption[] CaptureOptions =
    [
        new("cap.area", "Area", "Drag over part of the screen", WheelMode.Capture, EngineKind.Capture),
        new("cap.window", "Window", "Click a window to capture it", WheelMode.Capture, EngineKind.Capture),
        new("cap.full", "Full", "Capture the whole screen", WheelMode.Capture, EngineKind.Capture),
        new("cap.scroll", "Scroll", "Scrolling screenshot: pick an area, then scroll down; one tall picture of the whole page", WheelMode.Capture, EngineKind.Capture),
        new("cap.text", "Text", "Drag over any text on screen to copy it", WheelMode.Capture, EngineKind.Ocr),
        new("cap.clipboard", "Clipboard", "Everything you copied recently", WheelMode.Capture, EngineKind.Capture),
        new("cap.record", "Record", "Record part of the screen as a video (MP4)", WheelMode.Capture, EngineKind.Capture),
        new("cap.gif", "GIF", "Record part of the screen as an animated GIF, for chats and docs", WheelMode.Capture, EngineKind.Capture),
        new("cap.color", "Colour", "Click anywhere on screen to copy its colour", WheelMode.Capture, EngineKind.Capture),
        new("cap.ruler", "Ruler", "Measure anything on screen in pixels: drag over it, the size is copied", WheelMode.Capture, EngineKind.Capture),
        new("cap.qr", "QR", "Drag over a QR code or barcode to copy what it says", WheelMode.Capture, EngineKind.Capture),
        new("cap.qr-make", "Make QR", "A QR code for the link or text you copied", WheelMode.Capture, EngineKind.Capture),
        new("cap.delay", "5 s", "Area capture after 5 seconds", WheelMode.Capture, EngineKind.Capture),
        new("cap.delay10", "10 s", "Area capture after 10 seconds: time to open a menu", WheelMode.Capture, EngineKind.Capture),
        new("cap.selected", "Files", "Convert the files selected in Finder / Explorer", WheelMode.Capture, EngineKind.Capture),
        new("cap.history", "History", "Recent screenshots and converted files", WheelMode.Capture, EngineKind.Capture),
        new("cap.disk", "Disk", "What's taking up space: every folder and file by size, with a chart. Open, rename, ZIP or remove them", WheelMode.Capture, EngineKind.Capture),
        new("cap.settings", "Settings", "Folders, formats and other options", WheelMode.Capture, EngineKind.Capture),
    ];

    /// <summary>Every option in the catalogue, for counting and tests.</summary>
    public static IEnumerable<WheelOption> All =>
        ByKind.Values.SelectMany(v => v).Concat(ByExtension.Select(x => x.Option)).Concat(AnySelectionTools).Concat(ActionOptions).Concat(CaptureOptions);

    public const string MoreId = "nav.more", BackId = "nav.back";

    /// <summary>Pinned options first (in the order they were pinned), then the rest in their usual order.</summary>
    public static IReadOnlyList<WheelOption> WithPinsFirst(IReadOnlyList<WheelOption> options, IReadOnlyList<string> pinned)
    {
        if (pinned.Count == 0) return options;
        var first = pinned.Select(id => options.FirstOrDefault(o => o.Id == id)).Where(o => o is not null).Select(o => o!);
        return first.Concat(options.Where(o => !pinned.Contains(o.Id))).ToList();
    }

    /// <summary>How many pages a list of options takes on the wheel.</summary>
    public static int PageCount(int optionCount) =>
        optionCount <= MaxSegments ? 1 : 1 + (int)Math.Ceiling((optionCount - (MaxSegments - 1)) / (double)(MaxSegments - 2));

    /// <summary>
    /// One page of at most <see cref="MaxSegments"/> segments. With more options than fit, the first page ends
    /// with "More ›"; later pages start with "‹ Back" (and end with "More ›" while more remain).
    /// </summary>
    public static IReadOnlyList<WheelOption> Page(IReadOnlyList<WheelOption> options, int page, WheelMode mode)
    {
        if (options.Count <= MaxSegments) return options;
        var pages = PageCount(options.Count);
        page = Math.Clamp(page, 0, pages - 1);
        var more = new WheelOption(MoreId, "More ›", "More options", mode, EngineKind.Navigation);
        var back = new WheelOption(BackId, "‹ Back", "Back to the first options", mode, EngineKind.Navigation);
        if (page == 0)
            return options.Take(MaxSegments - 1).Append(more).ToList();
        var start = MaxSegments - 1 + (page - 1) * (MaxSegments - 2);
        var items = options.Skip(start).Take(MaxSegments - 2).ToList();
        var list = new List<WheelOption> { back };
        list.AddRange(items);
        if (page < pages - 1) list.Add(more);
        return list;
    }

    /// <summary>Every option for a drag of <paramref name="paths"/> on the given wheel (see <see cref="Page"/> for drawing).</summary>
    public static IReadOnlyList<WheelOption> GetOptions(WheelMode mode, IReadOnlyList<string> paths, Capabilities caps)
    {
        if (mode == WheelMode.Capture)
            return CaptureOptions.Where(caps.Satisfies).ToList();

        if (paths.Count == 0)
            return Array.Empty<WheelOption>();

        if (mode == WheelMode.Actions)
            return ActionOptions
                .Where(o => o.Id is not ("act.copy-image" or "act.base64") || (paths.Count == 1 && FileClassifier.Classify(paths[0]) == FileKind.Image))
                .ToList();

        var kinds = paths.Select(FileClassifier.Classify).Distinct().ToList();

        // Intersection of options valid for every kind present.
        IEnumerable<WheelOption>? common = null;
        foreach (var kind in kinds)
        {
            var forKind = ByKind.TryGetValue(kind, out var list) ? list : Array.Empty<WheelOption>();
            common = common is null ? forKind : common.Where(o => forKind.Any(f => f.Id == o.Id));
        }

        var result = (common ?? Enumerable.Empty<WheelOption>())
            .Where(o => o.Mode == mode)
            .Where(caps.Satisfies)
            .Where(o => paths.Count >= o.MinItems)
            .Where(o => !(o.Mode == WheelMode.Convert && AllAlready(paths, o.TargetExtension)))
            .ToList();

        result.AddRange(ByExtension.Where(x => x.Option.Mode == mode && caps.Satisfies(x.Option) && paths.Count >= x.Option.MinItems && x.When(paths)).Select(x => x.Option));

        // ZIP-ing things that are already archives only wraps them in another archive.
        if (mode == WheelMode.Tools)
            result.AddRange(AnySelectionTools.Where(o => o.Id is not ("any.zip" or "any.zip-lock") || !kinds.All(k => k == FileKind.Archive)));

        return result;
    }

    /// <summary>True when every path already has the target extension (".jpeg" counts as ".jpg").</summary>
    private static bool AllAlready(IReadOnlyList<string> paths, string? targetExt)
    {
        if (targetExt is null)
            return false;
        return paths.All(p => Normalize(OutputPaths.FullExtension(p)) == Normalize(targetExt));
    }

    private static string Normalize(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpeg" or ".jfif" => ".jpg",
        ".tif" => ".tiff",
        ".tgz" => ".tar.gz",
        var e => e,
    };
}
