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
