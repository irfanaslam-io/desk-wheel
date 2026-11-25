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
            T("img.edit", "Edit", "Mark up: arrows, text, blur, redact, crop", EngineKind.Image),
            T("img.ocr", "Text", "Copy the text in the image (English and Arabic)", EngineKind.Ocr),
            T("img.cutout", "Cut out", "Remove the background, keep the subject (PNG)", EngineKind.Vision, ".png"),
            T("img.icons", "Icons", "App icon set: PNGs from 16 to 1024 px, ICO and (Mac) ICNS", EngineKind.Image),
            T("img.to-pdf", "PDF", "Combine the images into one PDF", EngineKind.Image, ".pdf", many: true),
        ],
        [FileKind.Pdf] =
        [
            C("pdf.to-png", "PNG", "Every page as PNG (300 DPI)", EngineKind.Pdf, ".png"),
            C("pdf.to-jpg", "JPG", "Every page as JPG (300 DPI)", EngineKind.Pdf, ".jpg"),
            C("pdf.to-txt", "TXT", "Extract the text", EngineKind.Pdf, ".txt"),
            T("pdf.merge", "Merge", "Combine into one PDF (in name order)", EngineKind.Pdf, ".pdf", many: true, min: 2),
            T("pdf.split", "Split", "One PDF per page", EngineKind.Pdf),
            T("pdf.rotate", "Rotate", "Rotate every page 90° clockwise", EngineKind.Pdf, ".pdf"),
            T("pdf.numbers", "Numbers", "Add page numbers (1 / 12) at the bottom", EngineKind.Pdf, ".pdf"),
            T("pdf.compress", "Compress", "Make it smaller: pictures at print quality, text untouched", EngineKind.PdfEdit, ".pdf"),
            T("pdf.protect", "Lock", "Add a password (AES-256)", EngineKind.PdfEdit, ".pdf"),
            T("pdf.unlock", "Unlock", "Remove the password (you'll need it)", EngineKind.PdfEdit, ".pdf"),
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
            T("vid.compress", "Compress", "Compress (H.264, CRF 28)", EngineKind.Ffmpeg, ".mp4"),
            T("vid.half", "50%", "Half resolution", EngineKind.Ffmpeg, ".mp4"),
            T("vid.mute", "Mute", "Remove the audio track", EngineKind.Ffmpeg),
            T("vid.snapshot", "Frame", "Save the frame at 1 s as PNG", EngineKind.Ffmpeg, ".png"),
        ],
        [FileKind.Audio] =
        [
            C("aud.to-mp3", "MP3", "Convert to MP3 (192 kbps)", EngineKind.Ffmpeg, ".mp3"),
            C("aud.to-m4a", "M4A", "Convert to M4A (AAC 192 kbps)", EngineKind.Ffmpeg, ".m4a"),
            C("aud.to-wav", "WAV", "Convert to WAV (lossless)", EngineKind.Ffmpeg, ".wav"),
            C("aud.to-flac", "FLAC", "Convert to FLAC (lossless)", EngineKind.Ffmpeg, ".flac"),
            C("aud.to-ogg", "OGG", "Convert to OGG Vorbis", EngineKind.Ffmpeg, ".ogg"),
            C("aud.to-opus", "Opus", "Convert to Opus", EngineKind.Ffmpeg, ".opus"),
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
        [FileKind.Subtitle] =
        [
            C("sub.to-srt", "SRT", "Convert to SubRip (.srt)", EngineKind.Subtitle, ".srt"),
            C("sub.to-vtt", "VTT", "Convert to WebVTT (.vtt)", EngineKind.Subtitle, ".vtt"),
            C("sub.to-txt", "TXT", "Plain text, no timings", EngineKind.Subtitle, ".txt"),
        ],
    };

    /// <summary>Tools any selection gets, including folders and mixed selections.</summary>
    private static readonly WheelOption[] AnySelectionTools =
    [
        T("any.zip", "ZIP", "Compress everything into one ZIP", EngineKind.Archive, ".zip", many: true),
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
    ];

    /// <summary>The Screenshot wheel: works with no files at all.</summary>
    private static readonly WheelOption[] CaptureOptions =
    [
        new("cap.area", "Area", "Drag over part of the screen", WheelMode.Capture, EngineKind.Capture),
        new("cap.window", "Window", "Click a window to capture it", WheelMode.Capture, EngineKind.Capture),
        new("cap.full", "Full", "Capture the whole screen", WheelMode.Capture, EngineKind.Capture),
        new("cap.text", "Text", "Drag over any text on screen to copy it", WheelMode.Capture, EngineKind.Ocr),
        new("cap.clipboard", "Clipboard", "Everything you copied recently", WheelMode.Capture, EngineKind.Capture),
        new("cap.record", "Record", "Record part of the screen as a video (MP4)", WheelMode.Capture, EngineKind.Capture),
        new("cap.color", "Colour", "Click anywhere on screen to copy its colour", WheelMode.Capture, EngineKind.Capture),
        new("cap.qr", "QR", "Drag over a QR code or barcode to copy what it says", WheelMode.Capture, EngineKind.Capture),
        new("cap.qr-make", "Make QR", "A QR code for the link or text you copied", WheelMode.Capture, EngineKind.Capture),
        new("cap.delay", "5 s", "Area capture after 5 seconds", WheelMode.Capture, EngineKind.Capture),
        new("cap.selected", "Files", "Convert the files selected in Finder / Explorer", WheelMode.Capture, EngineKind.Capture),
        new("cap.history", "History", "Recent screenshots and converted files", WheelMode.Capture, EngineKind.Capture),
        new("cap.settings", "Settings", "Folders, formats and other options", WheelMode.Capture, EngineKind.Capture),
    ];

    /// <summary>Every option in the catalogue, for counting and tests.</summary>
    public static IEnumerable<WheelOption> All =>
        ByKind.Values.SelectMany(v => v).Concat(AnySelectionTools).Concat(ActionOptions).Concat(CaptureOptions);

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
                .Where(o => o.Id != "act.copy-image" || (paths.Count == 1 && FileClassifier.Classify(paths[0]) == FileKind.Image))
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

        // ZIP-ing things that are already archives only wraps them in another archive.
        if (mode == WheelMode.Tools)
            result.AddRange(AnySelectionTools.Where(o => o.Id != "any.zip" || !kinds.All(k => k == FileKind.Archive)));

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
