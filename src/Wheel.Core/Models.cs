namespace Wheel.Core;

/// <summary>Which wheel is open. Mapped to Shift / Ctrl / Alt by default.</summary>
public enum WheelMode
{
    Convert,
    Tools,
    Actions,
    /// <summary>Screenshots (and "convert the selected files"): needs no dropped files.</summary>
    Capture,
}

/// <summary>Broad category of a dragged item, decided by extension.</summary>
public enum FileKind
{
    Image,
    Video,
    Audio,
    Archive,
    Subtitle,
    Pdf,
    /// <summary>Word-processor files: DOCX, DOC, ODT, RTF, TXT.</summary>
    Document,
    /// <summary>XLSX, XLS, ODS, CSV.</summary>
    Spreadsheet,
    /// <summary>PPTX, PPT, ODP.</summary>
    Presentation,
    Markdown,
    /// <summary>JSON, JSONC, GeoJSON, HAR.</summary>
    Json,
    Folder,
    Other,
}

/// <summary>Which engine carries out an option.</summary>
public enum EngineKind
{
    /// <summary>Windows Imaging Component (built into Windows). Lives in Wheel.App.</summary>
    Image,
    /// <summary>ffmpeg.exe found beside the app or on PATH.</summary>
    Ffmpeg,
    /// <summary>System.IO.Compression / System.Formats.Tar.</summary>
    Archive,
    /// <summary>Plain-text subtitle conversion.</summary>
    Subtitle,
    /// <summary>File actions (copy path, terminal, reveal...). Lives in Wheel.App.</summary>
    Action,
    /// <summary>PDFium (bundled native library).</summary>
    Pdf,
    /// <summary>Screenshot and other no-file commands. Lives in Wheel.App.</summary>
    Capture,
    /// <summary>Text recognition: Apple Vision (Mac), Windows OCR, or Tesseract. Lives in Wheel.App.</summary>
    Ocr,
    /// <summary>Markdown shown as a formatted page (engines/markdown/).</summary>
    Markdown,
    /// <summary>JSON: viewer page, format, minify, CSV (in-process, System.Text.Json).</summary>
    Json,
    /// <summary>Apple Vision through Wheel's Mac helper (background removal). Lives in Wheel.App.</summary>
    Vision,
    /// <summary>PDF passwords and compression, in-process (PDFsharp).</summary>
    PdfEdit,
    /// <summary>Opens a window in the app (bulk rename).</summary>
    Window,
    /// <summary>"More ›" / "‹ Back" segments that page through a wheel with more than 8 options.</summary>
    Navigation,
    /// <summary>Office files: Microsoft Office when installed, else LibreOffice, else macOS's textutil for text formats.</summary>
    Office,
}

/// <summary>Extra tools an option needs beyond its engine.</summary>
public enum Requirement
{
    None,
    /// <summary>An image format SkiaSharp can't write (BMP, TIFF, GIF): needs sips (Mac) or ffmpeg.</summary>
    ImageFallback,
    /// <summary>Only macOS's sips can write it (HEIC).</summary>
    Sips,
    /// <summary>Needs an office suite: Microsoft Office (when it can write that format) or LibreOffice.</summary>
    OfficeSuite,
    /// <summary>An office suite, or macOS's built-in textutil (word-processor text formats).</summary>
    OfficeSuiteOrTextutil,
}

/// <summary>One segment on a wheel.</summary>
/// <param name="Id">Stable id, e.g. "img.to-png". Used for pinning and usage stats.</param>
/// <param name="Label">Short text drawn on the segment, e.g. "PNG".</param>
/// <param name="Hint">Tooltip / centre text, e.g. "Convert to PNG (lossless)".</param>
/// <param name="Mode">Which wheel it belongs to.</param>
/// <param name="Engine">Which engine runs it.</param>
/// <param name="TargetExtension">Output extension including the dot, when the option produces a file of a fixed type.</param>
/// <param name="AcceptsMany">True when all dropped files are processed together (e.g. one ZIP from many files).</param>
/// <param name="Needs">Extra tool the option needs, if any.</param>
/// <param name="MinItems">Fewest dropped items that make sense (2 for merging).</param>
public sealed record WheelOption(
    string Id,
    string Label,
    string Hint,
    WheelMode Mode,
    EngineKind Engine,
    string? TargetExtension = null,
    bool AcceptsMany = false,
    Requirement Needs = Requirement.None,
    int MinItems = 1);

/// <summary>What the current machine can do. Options whose engine or requirement is missing are hidden.</summary>
/// <param name="HasFfmpeg">ffmpeg found (bundled or on PATH).</param>
/// <param name="HasSips">macOS's built-in sips image tool is present.</param>
/// <param name="HasPdfium">The PDF engine library is bundled and loads.</param>
/// <param name="HasOcr">A text-recognition engine is available (set by the app, which owns OCR).</param>
/// <param name="HasMarkdown">The Markdown reading-page files are shipped beside the app.</param>
/// <param name="HasLibreOffice">LibreOffice is installed (Office files to PDF and between formats).</param>
/// <param name="HasTextutil">macOS's textutil (DOC/DOCX/ODT/RTF/TXT/HTML without LibreOffice).</param>
/// <param name="MicrosoftOffice">The Microsoft Office apps installed (Word, Excel, PowerPoint).</param>
public sealed record Capabilities(bool HasFfmpeg, bool HasSips = false, bool HasPdfium = false, bool HasOcr = false,
                                  bool HasMarkdown = false, bool HasLibreOffice = false, bool HasTextutil = false,
                                  bool HasVision = false, IReadOnlySet<string>? FfmpegEncoders = null,
                                  IReadOnlySet<Converters.OfficeApp>? MicrosoftOffice = null)
{
    public bool Satisfies(WheelOption option) =>
        (option.Engine != EngineKind.Ffmpeg || HasFfmpeg) &&
        (option.Engine != EngineKind.Ffmpeg || FfmpegEncoders is null || Converters.FfmpegConverter.CanDo(option.Id, FfmpegEncoders)) &&
        (option.Engine != EngineKind.Pdf || HasPdfium) &&
        (option.Engine != EngineKind.Ocr || HasOcr) &&
        (option.Engine != EngineKind.Markdown || HasMarkdown) &&
        (option.Engine != EngineKind.Vision || HasVision) &&
        option.Needs switch
        {
            Requirement.ImageFallback => HasFfmpeg || HasSips,
            Requirement.Sips => HasSips,
            Requirement.OfficeSuite => HasLibreOffice || HasOffice(option),
            Requirement.OfficeSuiteOrTextutil => HasLibreOffice || HasTextutil || HasOffice(option),
            _ => true,
        };

    private bool HasOffice(WheelOption option) =>
        Converters.MicrosoftOffice.CanDo(option.Id, option.TargetExtension, MicrosoftOffice, OperatingSystem.IsWindows());

    /// <summary>Detects ffmpeg and sips on this machine.</summary>
    public static Capabilities Detect() => new(
        HasFfmpeg: Converters.FfmpegConverter.Locate() is not null,
        FfmpegEncoders: Converters.FfmpegConverter.Locate() is { } ffmpeg ? Converters.FfmpegConverter.Encoders(ffmpeg) : null,
        HasSips: OperatingSystem.IsMacOS() && File.Exists("/usr/bin/sips"),
        HasPdfium: Converters.PdfConverter.IsAvailable(),
        HasMarkdown: Markdown.MarkdownAssets.FindFolder() is not null,
        HasLibreOffice: Converters.OfficeConverter.LocateLibreOffice() is not null,
        HasTextutil: Converters.OfficeConverter.HasTextutil,
        MicrosoftOffice: Converters.MicrosoftOffice.Installed());
}

/// <summary>Result of running one option over one or more inputs.</summary>
public sealed record JobResult(bool Success, IReadOnlyList<string> Outputs, string? Error = null)
{
    public static JobResult Ok(params string[] outputs) => new(true, outputs);
    public static JobResult Fail(string error) => new(false, Array.Empty<string>(), error);
}
