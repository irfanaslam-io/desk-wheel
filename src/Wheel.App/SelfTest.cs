using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using Wheel.Core;
using Wheel.Core.Converters;

namespace Wheel.App;

/// <summary>
/// <c>DeskWheel --self-test [folder]</c>: runs the real engines of this installed copy on small generated files
/// (image, PDF, video, audio, ZIP) and reports each one. The build runs it on the finished app, so a release
/// that is missing ffmpeg or PDFium, or whose ZIP or PDF tools fail, never ships.
/// Results go to the console and to self-test.txt in the folder; the exit code is the number of failures.
/// No window opens and nothing outside the folder is touched.
/// </summary>
internal static class SelfTest
{
    public static int Run(string? folder)
    {
        folder = Path.GetFullPath(folder ?? Path.Combine(Path.GetTempPath(), "DeskWheel-self-test-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(folder);
        var report = new List<string> { $"Desk Wheel {BuildConfig.Version} self-test, {Environment.OSVersion}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}", $"App folder: {AppContext.BaseDirectory}" };
        var failures = 0;

        void Check(string name, Func<string?> test)
        {
            string line;
            try
            {
                var problem = test();
                line = problem is null ? $"PASS  {name}" : $"FAIL  {name}: {problem}";
            }
            catch (Exception ex)
            {
                line = $"FAIL  {name}: {ex.GetType().Name}: {ex.Message}";
            }
            if (line.StartsWith("FAIL")) failures++;
            report.Add(line);
            Console.WriteLine(line);
        }

        static WheelOption Opt(string id) => OptionRegistry.All.First(o => o.Id == id);
        static string? Produced(JobResult r, string what) =>
            !r.Success ? r.Error ?? "failed"
            : r.Outputs.Count == 0 || r.Outputs.Any(o => !File.Exists(o) && !Directory.Exists(o)) ? $"no {what} was written"
            : r.Outputs.Where(File.Exists).Any(o => new FileInfo(o).Length == 0) ? $"the {what} is empty"
            : null;
        static JobResult Wait(Task<JobResult> task) => task.GetAwaiter().GetResult();

        // ---- the engines are shipped beside the app (not just found somewhere on this computer) ----
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pdfium = OperatingSystem.IsWindows() ? "pdfium.dll" : OperatingSystem.IsMacOS() ? "libpdfium.dylib" : "libpdfium.so";
        var engines = Path.Combine(AppContext.BaseDirectory, "engines");
        Check("ffmpeg is shipped", () => File.Exists(Path.Combine(engines, exe)) ? null : $"{exe} missing from {engines}");
        Check("PDFium is shipped", () => File.Exists(Path.Combine(engines, pdfium)) ? null : $"{pdfium} missing from {engines}");
        Check("licence files are shipped", () =>
            new[] { "LICENSE.md", "THIRD-PARTY-NOTICES.md", Path.Combine("LICENSES", "PolyForm-Small-Business-1.0.0.md"), Path.Combine("LICENSES", "PolyForm-Noncommercial-1.0.0.md") }
                .FirstOrDefault(f => !File.Exists(Path.Combine(AppContext.BaseDirectory, f))) is { } missing ? $"{missing} missing" : null);
        Check("Markdown reader files are shipped", () => Wheel.Core.Markdown.MarkdownAssets.FindFolder() is null ? "engines/markdown missing" : null);
        Check("ffmpeg runs", () => FfmpegConverter.Locate() is { } f && f.StartsWith(engines, StringComparison.OrdinalIgnoreCase) && FfmpegConverter.Encoders(f).Count > 0 ? null : $"using {FfmpegConverter.Locate() ?? "none"}");
        Check("PDFium loads", () => PdfConverter.IsAvailable() ? null : "couldn't load it");

        // ---- images ----
        var png = Path.Combine(folder, "sample.png");
        Check("make a test picture", () =>
        {
            using var bitmap = new SKBitmap(320, 200);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(new SKColor(0x3D, 0x8B, 0xFD));
                using var paint = new SKPaint { Color = new SKColor(0xE8, 0x62, 0x2C), IsAntialias = true };
                canvas.DrawCircle(160, 100, 70, paint);
            }
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(png, data.ToArray());
            return null;
        });
        Check("image → JPG", () => Produced(Wait(ImageEngine.RunAsync(Opt("img.to-jpg"), png)), "JPG"));

        // ---- PDF ----
        string? pdf = null;
        Check("images → PDF", () => { var r = Wait(ImageEngine.ImagesToPdfAsync([png])); pdf = r.Outputs.FirstOrDefault(); return Produced(r, "PDF"); });
        Check("PDF → PNG pages", () => pdf is null ? "no PDF" : Produced(PdfConverter.Run(Opt("pdf.to-png"), [pdf], ImageEngine.WriteRenderedPage), "PNG"));
        Check("PDF rotate", () => pdf is null ? "no PDF" : Produced(PdfConverter.Run(Opt("pdf.rotate"), [pdf], ImageEngine.WriteRenderedPage), "PDF"));

        // ---- picture tools ----
        var png2 = Path.Combine(folder, "second.png");
        File.Copy(png, png2, overwrite: true);
        foreach (var id in new[] { "img.square", "img.flip", "img.grey", "img.favicon" })
            Check($"picture {id}", () => Produced(Wait(ImageEngine.RunAsync(Opt(id), png)), "picture"));
        Check("picture watermark (Arabic and English)", () => Produced(Wait(ImageEngine.WatermarkAsync(png, "© Desk Wheel · سري")), "picture"));
        foreach (var layout in new[] { "row", "column", "grid" })
            Check($"combine pictures: {layout}", () => Produced(Wait(ImageEngine.CombineAsync([png, png2], layout)), "picture"));
        Check("picture as Base64", () => Wheel.Core.Files.DataUrl.For(png, File.ReadAllBytes(png)).StartsWith("data:image/png;base64,iVBOR") ? null : "wrong data URL");

        // ---- more PDF tools ----
        Check("PDF keep pages", () => pdf is null ? "no PDF" : PdfConverter.KeepPages(pdf, [0, 0], "pages 1, 1") is { } kept && PdfConverter.CountPages(kept) == 2 ? null : "wrong page count");
        Check("PDF watermark", () => pdf is null ? "no PDF" : Produced(PdfTools.Watermark(pdf, ImageEngine.WatermarkPicture("CONFIDENTIAL سري")), "PDF"));
        Check("PDF pictures out", () => pdf is null ? "no PDF" : Produced(PdfConverter.ExtractImages(pdf, ImageEngine.WriteRenderedPage), "pictures"));
        Check("PDF grey copy", () => pdf is null ? "no PDF" : Produced(Wait(ImageEngine.RebuildPdfAsync(pdf, grey: true)), "PDF"));
        Check("PDF flatten", () => pdf is null ? "no PDF" : Produced(Wait(ImageEngine.RebuildPdfAsync(pdf, grey: false)), "PDF"));
        Check("PDF clean", () => pdf is null ? "no PDF" : Produced(PdfTools.Clean(pdf), "PDF"));

        // ---- video and audio ----
        var video = Path.Combine(folder, "sample.mkv");
        Check("make a test video", () =>
        {
            if (FfmpegConverter.Locate() is not { } ffmpeg) return "no ffmpeg";
            var p = Process.Start(new ProcessStartInfo(ffmpeg)
            {
                ArgumentList = { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=10",
                                 "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:v", "ffv1", "-c:a", "flac", "-shortest", video },
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            })!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 && File.Exists(video) ? null : err.Trim();
        });
        var encoders = FfmpegConverter.Locate() is { } found ? FfmpegConverter.Encoders(found) : new HashSet<string>();
        foreach (var id in new[] { "vid.to-gif", "vid.to-m4a", "vid.snapshot", "vid.to-mp4", "vid.to-webm", "vid.to-mp3" })
        {
            // Only what the wheel offers with this ffmpeg; the rest is hidden in the app, so it's listed, not failed.
            if (!FfmpegConverter.CanDo(id, encoders))
            {
                report.Add($"SKIP  video {id}: this ffmpeg can't, so the wheel doesn't offer it");
                Console.WriteLine(report[^1]);
                continue;
            }
            Check($"video {id}", () => Produced(Wait(FfmpegConverter.RunAsync(Opt(id), video)), "file"));
        }
        if (FfmpegConverter.H264Encoder(encoders) is not null)
        {
            Check("video trim (to the frame)", () => Produced(Wait(MediaEditor.TrimAsync(video, 0.2, 0.8)), "video"));
            Check("video speed", () => Produced(Wait(MediaEditor.SpeedAsync(video, 2)), "video"));
            Check("video 720p", () => Produced(Wait(FfmpegConverter.RunAsync(Opt("vid.720"), video)), "video"));
        }
        Check("video join", () => Produced(Wait(MediaEditor.JoinAsync([video, video])), "video"));
        Check("video thumbnail sheet", () => Produced(Wait(MediaEditor.SheetAsync(video)), "picture"));
        Check("subtitles added as a track", () =>
        {
            var srt = Path.Combine(folder, "sample.srt");
            File.WriteAllText(srt, "1\n00:00:00,100 --> 00:00:00,900\nHello مرحبا\n");
            var mkv = Path.Combine(folder, "sample-copy.mkv");
            File.Copy(video, mkv, overwrite: true);
            return Produced(Wait(MediaEditor.AddSubtitlesAsync([mkv, srt])), "video");
        });

        // ---- ZIP and extract ----
        string? zip = null;
        Check("ZIP files", () =>
        {
            var inputs = new[] { png, pdf }.OfType<string>().ToList();
            var r = ArchiveConverter.Run(Opt("any.zip"), inputs);
            zip = r.Outputs.FirstOrDefault();
            return Produced(r, "ZIP");
        });
        Check("extract ZIP", () =>
        {
            if (zip is null) return "no ZIP";
            var r = ArchiveConverter.Run(Opt("arc.extract"), [zip]);
            if (Produced(r, "folder") is { } problem) return problem;
            var files = Directory.GetFiles(r.Outputs[0]).Select(Path.GetFileName).ToList();
            return files.Contains("sample.png") ? null : $"extracted: {string.Join(", ", files)}";
        });
        Check("ZIP → TAR.GZ", () => zip is null ? "no ZIP" : Produced(ArchiveConverter.Run(Opt("arc.to-targz"), [zip]), "TAR.GZ"));
        Check("ZIP with a password, opened again", () =>
        {
            var locked = ArchiveConverter.ZipWithPassword([png], "self-test");
            if (Produced(locked, "ZIP") is { } problem) return problem;
            if (!ArchiveConverter.NeedsPassword(locked.Outputs[0])) return "not protected";
            if (ArchiveConverter.Run(Opt("arc.extract"), [locked.Outputs[0]], "wrong").Success) return "wrong password accepted";
            var opened = ArchiveConverter.Run(Opt("arc.extract"), [locked.Outputs[0]], "self-test");
            return Produced(opened, "folder") ?? (File.Exists(Path.Combine(opened.Outputs[0], "sample.png")) ? null : "picture missing");
        });
        Check("TAR.GZ opened again", () =>
            Directory.GetFiles(folder, "*.tar.gz").FirstOrDefault() is { } tgz ? Produced(ArchiveConverter.Run(Opt("arc.extract"), [tgz]), "folder") : "no TAR.GZ");
        Check("checksums", () => Wheel.Core.Files.FileTools.ChecksumOf(png).Sha256.Length == 64 ? null : "no SHA-256");

        // ---- JSON ----
        var json = Path.Combine(folder, "sample.json");
        File.WriteAllText(json, "{\"name\":\"Desk Wheel\",\"tools\":[\"convert\",\"view\"],\"free\":true}");
        Check("JSON format", () => Produced(Wheel.Core.Json.JsonTools.Format(json), "JSON"));
        Check("JSON viewer page", () => Produced(Wheel.Core.Json.JsonTools.SaveHtml(json), "page"));
        Check("JSON → CSV", () => Produced(Wheel.Core.Json.JsonTools.ToCsv(json), "CSV"));

        report.Add(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        Console.WriteLine(report[^1]);
        File.WriteAllLines(Path.Combine(folder, "self-test.txt"), report);
        return failures;
    }
}
