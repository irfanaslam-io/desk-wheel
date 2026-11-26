using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Converters;
using Wheel.Core.Markdown;
using Wheel.Core.Ocr;

namespace Wheel.App;

/// <summary>Where job results are shown (toast card + tray tooltip).</summary>
internal interface INotifier
{
    void Notify(string title, string text, bool error = false, string? reveal = null);
    void SetBusy(int runningJobs);
}

/// <summary>Runs a chosen option over the dropped files and reports the result.</summary>
internal sealed class JobRunner
{
    private readonly INotifier _notifier;
    private readonly IPlatform _platform;
    private int _running;

    public JobRunner(INotifier notifier, IPlatform platform)
    {
        _notifier = notifier;
        _platform = platform;
    }

    /// <summary>Never throws: every failure becomes a notification.</summary>
    public async Task RunAsync(WheelOption option, IReadOnlyList<string> paths)
    {
        _running++;
        _notifier.SetBusy(_running);
        Usage.Tool(option.Id, paths.Count);
        try
        {
            if (option.Id == "act.copy-image")
            {
                await CopyImageAsync(paths[0]);
                return;
            }

            if (option.Engine == EngineKind.Action)
            {
                ReportAction(option, paths, FileActions.Run(_platform, option, paths));
                return;
            }

            if (option.Engine == EngineKind.Markdown && option.Id != "md.to-html")
            {
                Read(paths);
                return;
            }
            if (option.Id is "json.view" or "json.view-tools")
            {
                ViewJson(paths);
                return;
            }

            Wheel.Core.Files.ResizeSpec? size = null;
            string? password = null;
            string? answerText = null;
            switch (option.Id)
            {
                case "pdf.pages" or "pdf.delete":
                {
                    if (paths.Count != 1)
                    {
                        _notifier.Notify("One PDF at a time", "Select a single PDF to choose its pages.");
                        return;
                    }
                    int count;
                    try { count = await Task.Run(() => PdfConverter.CountPages(paths[0])); }
                    catch (Exception ex) when (ex is IOException or InvalidDataException) { _notifier.Notify("Couldn't read the PDF", ex.Message, error: true); return; }
                    var keep = option.Id == "pdf.pages";
                    answerText = await PromptWindow.AskAsync(keep ? "Keep pages" : "Delete pages",
                        keep ? $"{Describe(paths)} has {count} pages. Pages to keep, in the order you want them: 1-3, 7, 10-end. Backwards: end-1. Also: odd, even."
                             : $"{Describe(paths)} has {count} pages. Pages to remove: 2, 5-7.",
                        keep ? _lastPages : null, keep ? "1-3, 7" : "2, 5-7",
                        check: t =>
                        {
                            var (pages, problem) = Wheel.Core.Pdf.PageSpec.Parse(t, count);
                            if (problem is not null) return problem;
                            return !keep && Wheel.Core.Pdf.PageSpec.Without(pages!, count).Count == 0 ? "That would remove every page." : null;
                        });
                    if (answerText is null) return;
                    if (keep) _lastPages = answerText;
                    break;
                }
                case "pdf.watermark" or "img.watermark":
                    answerText = await PromptWindow.AskAsync("Watermark",
                        option.Id == "pdf.watermark" ? $"Text across every page of {Describe(paths)}:" : $"Text for the corner of {Describe(paths)}:",
                        option.Id == "pdf.watermark" ? _lastPdfMark ?? "CONFIDENTIAL" : _lastPictureMark ?? $"© {DateTime.Now.Year}", null,
                        check: t => t.Trim().Length == 0 ? "Type the text." : t.Length > 60 ? "Keep it under 60 characters." : null);
                    if (answerText is null) return;
                    answerText = answerText.Trim();
                    if (option.Id == "pdf.watermark") _lastPdfMark = answerText; else _lastPictureMark = answerText;
                    break;
                case "img.resize":
                    var answer = await PromptWindow.AskAsync("Resize", "New size: 1920x1080 (fits inside, keeps proportions), 1080 (width), x720 (height) or 25%.",
                        _lastSize, "1920x1080", check: t => Wheel.Core.Files.ResizeSpec.Parse(t) is null ? "Try 1920x1080, 1080, x720 or 50%." : null);
                    if (answer is null) return;
                    _lastSize = answer;
                    size = Wheel.Core.Files.ResizeSpec.Parse(answer);
                    break;
                case "pdf.protect":
                    password = await PromptWindow.AskAsync("Lock PDF", $"Password for {Describe(paths)}. Anyone opening it will need this password; there's no way to recover it.",
                        password: true, confirmPassword: true, check: t => t.Length < 4 ? "Use at least 4 characters." : null);
                    if (password is null) return;
                    break;
                case "pdf.unlock":
                    password = await PromptWindow.AskAsync("Unlock PDF", $"The password of {Describe(paths)}:", password: true,
                        check: t => t.Length == 0 ? "Type the password." : null);
                    if (password is null) return;
                    break;
            }
            if (size is not null || password is not null || answerText is not null || option.Engine is EngineKind.PdfEdit or EngineKind.Vision)
            {
                _notifier.Notify("Working…", $"{option.Hint}: {Describe(paths)}");
                var done = new List<JobResult>();
                foreach (var path in paths)
                    done.Add(option.Id switch
                    {
                        "img.resize" => await ImageEngine.RunAsync(option, path, size),
                        "img.cutout" => await OcrEngine.RemoveBackgroundAsync(path),
                        "pdf.compress" => await Task.Run(() => PdfTools.Compress(path, ImageEngine.ShrinkPdfPicture)),
                        "pdf.protect" => await Task.Run(() => PdfTools.Protect(path, password!)),
                        "pdf.unlock" => await Task.Run(() => PdfTools.Unlock(path, password!)),
                        "pdf.pages" or "pdf.delete" => await Task.Run(() => KeepPages(path, option.Id == "pdf.pages", answerText!)),
                        "pdf.watermark" => await Task.Run(() => PdfTools.Watermark(path, ImageEngine.WatermarkPicture(answerText!))),
                        "pdf.images" => await Task.Run(() => PdfConverter.ExtractImages(path, ImageEngine.WriteRenderedPage)),
                        "pdf.grey" => await ImageEngine.RebuildPdfAsync(path, grey: true),
                        "pdf.flatten" => await ImageEngine.RebuildPdfAsync(path, grey: false),
                        "pdf.clean" => await Task.Run(() => PdfTools.Clean(path)),
                        "img.watermark" => await ImageEngine.WatermarkAsync(path, answerText!),
                        _ => JobResult.Fail($"No engine for {option.Id}"),
                    });
                Remember(option, paths, done);
                Report(done);
                return;
            }

            if (option.Engine == EngineKind.Ocr)
            {
                await ReadTextAsync(paths);
                return;
            }

            if (paths.Count > 1 || option.Engine is EngineKind.Ffmpeg or EngineKind.Pdf or EngineKind.Office)
                _notifier.Notify("Working…", $"{option.Hint}: {Describe(paths)}");

            var results = new List<JobResult>();
            if (option.AcceptsMany)
                results.Add(await Execute(option, paths));
            else
                foreach (var path in paths)
                    results.Add(await Execute(option, new[] { path }));

            if (option.Id == "pdf.to-txt")
                await ReadScannedPagesAsync(paths, results);

            Remember(option, paths, results);
            Report(results);
        }
        catch (Exception ex)
        {
            Log.Write(ex);
            _notifier.Notify("Desk Wheel hit a problem", ex.Message, error: true);
        }
        finally
        {
            _running--;
            _notifier.SetBusy(_running);
        }
    }

    /// <summary>Copies an image's picture (not the file). Formats other than PNG are converted on the way.</summary>
    private async Task CopyImageAsync(string path)
    {
        var png = path;
        string? temp = null;
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            temp = Path.Combine(Path.GetTempPath(), $"wheel-copy-{Guid.NewGuid():N}.png");
            using (var bitmap = await ImageEngine.LoadForEditingAsync(path))
                ImageEngine.SaveEdited(bitmap, temp);
            png = temp;
        }
        try
        {
            await _platform.CopyImageAsync(png);
            _notifier.Notify("Copied", $"The picture from {Path.GetFileName(path)}: paste with {(OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V")}");
        }
        finally
        {
            if (temp is not null) try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private string? _lastSize;

    /// <summary>Most Markdown files opened at once by Read (a folder of notes shouldn't open 80 tabs).</summary>
    private const int MaxPagesOpened = 5;

    /// <summary>Read: shows each Markdown file as a formatted page in the browser.</summary>
    private void Read(IReadOnlyList<string> paths)
    {
        var assets = MarkdownAssets.Shipped();
        if (assets is null)
        {
            _notifier.Notify("Can't show Markdown", "The page files are missing from Desk Wheel's folder. Rebuild Desk Wheel.", error: true);
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "DeskWheel Reader");
        foreach (var path in paths.Take(MaxPagesOpened))
        {
            try
            {
                var page = MarkdownPage.WriteReadingCopy(path, folder, assets);
                Log.Trace($"  Reading page written: {page}");
                _platform.OpenInBrowser(page);
                _notifier.Notify("Opened in your browser", Path.GetFileName(path), reveal: page);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _notifier.Notify("Couldn't show it", $"{Path.GetFileName(path)}: {ex.Message}", error: true);
            }
        }
        if (paths.Count > MaxPagesOpened)
            _notifier.Notify("Opened the first 5", $"{paths.Count} Markdown files selected; select fewer to read the rest.");
    }

    /// <summary>View: each JSON file as a readable page in the browser (self-contained, nothing loaded from the internet).</summary>
    private void ViewJson(IReadOnlyList<string> paths)
    {
        var folder = Path.Combine(Path.GetTempPath(), "DeskWheel Reader");
        foreach (var path in paths.Take(MaxPagesOpened))
        {
            try
            {
                var page = Wheel.Core.Json.JsonTools.WriteViewingCopy(path, folder);
                _platform.OpenInBrowser(page);
                _notifier.Notify("Opened in your browser", Path.GetFileName(path), reveal: page);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _notifier.Notify("Couldn't show it", $"{Path.GetFileName(path)}: {ex.Message}", error: true);
            }
        }
        if (paths.Count > MaxPagesOpened)
            _notifier.Notify("Opened the first 5", $"{paths.Count} JSON files selected; select fewer to view the rest.");
    }

    /// <summary>
    /// Text tool: one image → its text goes on the clipboard; several → a .txt beside each.
    /// </summary>
    private async Task ReadTextAsync(IReadOnlyList<string> paths)
    {
        _notifier.Notify("Reading text…", Describe(paths));
        if (paths.Count == 1)
        {
            var text = await OcrEngine.RecognizeAsync(paths[0]);
            ReportText(text);
            return;
        }

        var results = new List<JobResult>();
        foreach (var path in paths)
        {
            try
            {
                var text = await OcrEngine.RecognizeAsync(path);
                var output = OutputPaths.Beside(path, ".txt");
                await File.WriteAllTextAsync(output, text);
                results.Add(JobResult.Ok(output));
                AppHistory.Record("Converted", output, path, "Text from the image");
            }
            catch (IOException ex)
            {
                results.Add(JobResult.Fail($"{Path.GetFileName(path)}: {ex.Message}"));
            }
        }
        Report(results);
    }

    /// <summary>Copies recognised text and says what was copied. Also used for on-screen text capture.</summary>
    public void ReportText(string text)
    {
        if (text.Length == 0)
        {
            _notifier.Notify("No text found", "Try a sharper or larger area.", error: true);
            return;
        }
        _platform.CopyText(text);
        _notifier.Notify(OcrText.CopiedSummary(text), OcrText.Preview(text));
    }

    /// <summary>
    /// "PDF → TXT" on a scanned PDF gives an empty file (its pages are pictures). When text recognition is
    /// available, read the pages instead and fill the file.
    /// </summary>
    private async Task ReadScannedPagesAsync(IReadOnlyList<string> pdfs, List<JobResult> results)
    {
        if (!OcrEngine.IsAvailable) return;
        for (var i = 0; i < results.Count && i < pdfs.Count; i++)
        {
            if (!results[i].Success || results[i].Outputs.Count != 1) continue;
            var txt = results[i].Outputs[0];
            if (File.Exists(txt) && (await File.ReadAllTextAsync(txt)).Trim().Length > 0) continue;
            try
            {
                _notifier.Notify("Scanned PDF", $"Reading the pages of {Path.GetFileName(pdfs[i])}…");
                var text = await OcrEngine.RecognizePdfAsync(pdfs[i]);
                await File.WriteAllTextAsync(txt, text);
            }
            catch (IOException ex)
            {
                results[i] = JobResult.Fail($"{Path.GetFileName(pdfs[i])} is scanned and its text couldn't be read: {ex.Message}");
            }
        }
    }

    private string? _lastPages, _lastPdfMark, _lastPictureMark;

    /// <summary>Pages… keeps the typed pages in that order; Delete… keeps all the others.</summary>
    private static JobResult KeepPages(string path, bool keep, string typed)
    {
        try
        {
            var count = PdfConverter.CountPages(path);
            var (pages, problem) = Wheel.Core.Pdf.PageSpec.Parse(typed, count);
            if (problem is not null) return JobResult.Fail(problem);
            var label = Wheel.Core.Pdf.PageSpec.Label(typed);
            return keep
                ? JobResult.Ok(PdfConverter.KeepPages(path, pages!, $"pages {label}"))
                : JobResult.Ok(PdfConverter.KeepPages(path, Wheel.Core.Pdf.PageSpec.Without(pages!, count), $"without {label}"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return JobResult.Fail($"{Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static Task<JobResult> Execute(WheelOption option, IReadOnlyList<string> paths) => option.Engine switch
    {
        EngineKind.Image when option.Id == "img.to-pdf" => ImageEngine.ImagesToPdfAsync(paths),
        EngineKind.Image when option.Id is "img.row" or "img.column" or "img.grid" => ImageEngine.CombineAsync(paths, option.Id[4..]),
        EngineKind.Image => ImageEngine.RunAsync(option, paths[0]),
        EngineKind.Pdf => Task.Run(() => PdfConverter.Run(option, paths, ImageEngine.WriteRenderedPage)),
        EngineKind.Ffmpeg => FfmpegConverter.RunAsync(option, paths[0]),
        EngineKind.Archive => Task.Run(() => ArchiveConverter.Run(option, paths)),
        EngineKind.Subtitle => Task.Run(() => SubtitleConverter.Run(option, paths[0])),
        EngineKind.Office => OfficeConverter.RunAsync(option, paths[0]),
        EngineKind.Markdown => Task.Run(() => MarkdownAssets.Shipped() is { } assets
            ? MarkdownPage.SaveHtml(paths[0], assets)
            : JobResult.Fail("The Markdown page files are missing from Desk Wheel's folder. Rebuild Desk Wheel.")),
        EngineKind.Json => Task.Run(() => option.Id switch
        {
            "json.format" => Wheel.Core.Json.JsonTools.Format(paths[0]),
            "json.minify" => Wheel.Core.Json.JsonTools.Minify(paths[0]),
            "json.to-csv" => Wheel.Core.Json.JsonTools.ToCsv(paths[0]),
            "json.to-html" => Wheel.Core.Json.JsonTools.SaveHtml(paths[0]),
            _ => JobResult.Fail($"No engine for {option.Id}"),
        }),
        _ => Task.FromResult(JobResult.Fail($"No engine for {option.Id}")),
    };

    /// <summary>Raised with the files each job wrote (watched folders use it to skip Wheel's own output).</summary>
    public event Action<IReadOnlyList<string>>? OutputsWritten;

    /// <summary>Adds what was made to the History window.</summary>
    private void Remember(WheelOption option, IReadOnlyList<string> paths, IReadOnlyList<JobResult> results)
    {
        OutputsWritten?.Invoke(results.SelectMany(r => r.Outputs).ToList());
        for (var i = 0; i < results.Count; i++)
        {
            if (!results[i].Success) continue;
            string? source = option.AcceptsMany
                ? (paths.Count == 1 ? paths[0] : null)
                : (i < paths.Count ? paths[i] : null);
            foreach (var output in results[i].Outputs)
                AppHistory.Record("Converted", output, source, option.Hint);
        }
    }

    private void Report(IReadOnlyList<JobResult> results)
    {
        var outputs = results.SelectMany(r => r.Outputs).ToList();
        var failures = results.Where(r => !r.Success).ToList();

        if (failures.Count == 0)
        {
            var text = outputs.Count == 1 ? $"Saved {Path.GetFileName(outputs[0])}" : $"Saved {outputs.Count} files";
            _notifier.Notify("Done", text + " · click to show", reveal: outputs.FirstOrDefault());
            return;
        }

        var first = failures[0].Error ?? "Unknown error";
        var more = failures.Count > 1 ? $" (and {failures.Count - 1} more)" : "";
        var done = outputs.Count > 0 ? $" {outputs.Count} finished." : "";
        _notifier.Notify("Couldn't finish", first + more + done, error: true, reveal: outputs.FirstOrDefault());
    }

    private void ReportAction(WheelOption option, IReadOnlyList<string> paths, JobResult result)
    {
        if (!result.Success)
        {
            _notifier.Notify("Couldn't do that", result.Error ?? "", error: true);
            return;
        }
        switch (option.Id)
        {
            case "act.copy-file":
                _notifier.Notify("Copied", $"{Describe(paths)}: paste with {(OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V")} into a folder, chat or email");
                break;
            case "act.copy-path":
                _notifier.Notify("Copied", paths.Count == 1 ? "Path copied to the clipboard" : $"{paths.Count} paths copied");
                break;
            case "act.copy-name":
                _notifier.Notify("Copied", paths.Count == 1 ? "Name copied to the clipboard" : $"{paths.Count} names copied");
                break;
            case "act.base64":
                _notifier.Notify("Copied", "The picture as a data URL (Base64): paste it into HTML, CSS or code");
                break;
            case "act.copy-desktop":
                _notifier.Notify("Copied to Desktop", Describe(paths), reveal: result.Outputs.FirstOrDefault());
                break;
            // Terminal and Reveal open a window themselves.
        }
    }

    private static string Describe(IReadOnlyList<string> paths) =>
        paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('/', '\\')) : $"{paths.Count} items";
}
