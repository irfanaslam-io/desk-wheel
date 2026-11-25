using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wheel.Core;
using Wheel.Core.Converters;
using Wheel.Core.Ocr;

namespace Wheel.App;

/// <summary>
/// Reads the text in an image, on the device, with what the system already has:
/// Apple Vision on Mac (the wheel-ocr helper built into Wheel.app), Windows OCR on Windows,
/// or Tesseract when it's installed and the system engine isn't available. Nothing is uploaded.
/// </summary>
internal static class OcrEngine
{
    private enum Kind { None, Vision, Windows, Tesseract }

    private static Kind _kind = Kind.None;
    private static string? _exe;           // the helper, powershell.exe, or tesseract
    private static string _tesseractLangs = "eng";
    private static bool _detected;

    public static bool IsAvailable => Detect() != Kind.None;

    /// <summary>For the debug log and Settings: "Apple Vision", "Windows OCR", "Tesseract (eng+ara)".</summary>
    public static string Description => Detect() switch
    {
        Kind.Vision => "Apple Vision",
        Kind.Windows => "Windows OCR",
        Kind.Tesseract => $"Tesseract ({_tesseractLangs})",
        _ => "none",
    };

    public static string InstallHint => OperatingSystem.IsMacOS()
        ? "Rebuild Desk Wheel with Xcode's command-line tools installed (xcode-select --install), or run: brew install tesseract tesseract-lang"
        : "Text recognition needs Windows 10 or later.";

    private static Kind Detect()
    {
        if (_detected) return _kind;
        _detected = true;

        if (OperatingSystem.IsMacOS())
        {
            var helper = Path.Combine(AppContext.BaseDirectory, "wheel-ocr");
            if (File.Exists(helper)) { _exe = helper; return _kind = Kind.Vision; }
        }
        else if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(ps)) { _exe = ps; return _kind = Kind.Windows; }
        }

        if (FindTesseract() is { } tesseract)
        {
            _exe = tesseract;
            _tesseractLangs = TesseractLanguages(tesseract);
            return _kind = Kind.Tesseract;
        }
        return _kind = Kind.None;
    }

    /// <summary>Background removal: Apple Vision on macOS 14 (Sonoma) or later, through the same helper.</summary>
    public static bool CanRemoveBackground =>
        OperatingSystem.IsMacOSVersionAtLeast(14) && File.Exists(Path.Combine(AppContext.BaseDirectory, "wheel-ocr"));

    /// <summary>"name (cut out).png": the subject on a transparent background.</summary>
    public static async Task<JobResult> RemoveBackgroundAsync(string input, CancellationToken ct = default)
    {
        if (!CanRemoveBackground)
            return JobResult.Fail("Removing backgrounds needs macOS 14 (Sonoma) or later.");
        var output = OutputPaths.Beside(input, ".png", "cut out");
        var r = await ProcessRunner.RunAsync(Path.Combine(AppContext.BaseDirectory, "wheel-ocr"), ["--remove-background", input, output], ct);
        if (r.ExitCode == 0 && File.Exists(output))
            return JobResult.Ok(output);
        try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
        return JobResult.Fail($"{Path.GetFileName(input)}: {(r.ErrorTail.Trim().Length > 0 ? r.ErrorTail.Trim() : "couldn't remove the background.")}");
    }

    /// <summary>The languages Apple Vision can read on this Mac (for the debug log); empty elsewhere.</summary>
    public static async Task<string> LanguagesAsync()
    {
        if (Detect() != Kind.Vision || _exe is null) return "";
        var r = await ProcessRunner.RunAsync(_exe, ["--languages"]);
        return string.Join(", ", r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The recognised text, tidied. Empty when the image has no readable text.</summary>
    /// <exception cref="IOException">The engine is missing or failed.</exception>
    public static async Task<string> RecognizeAsync(string imagePath, CancellationToken ct = default)
    {
        var kind = Detect();
        if (kind == Kind.None || _exe is null)
            throw new IOException("No text recognition on this computer. " + InstallHint);

        var result = kind switch
        {
            Kind.Vision => await ProcessRunner.RunAsync(_exe, [imagePath], ct),
            Kind.Windows => await ProcessRunner.RunAsync(_exe,
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", WindowsScript(), "-Path", imagePath], ct),
            _ => await ProcessRunner.RunAsync(_exe, [imagePath, "stdout", "-l", _tesseractLangs], ct),
        };
        if (result.ExitCode != 0)
        {
            var why = result.ErrorTail.Trim();
            throw new IOException(why.Length > 0 ? why : $"Text recognition stopped (code {result.ExitCode}).");
        }
        return OcrText.Tidy(result.Output);
    }

    /// <summary>
    /// Text of a scanned PDF: renders each page, then reads it. Pages are separated by a blank line.
    /// </summary>
    public static async Task<string> RecognizePdfAsync(string pdfPath, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-ocr-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pages = await Task.Run(() => PdfConverter.RenderPages(pdfPath, temp, ImageEngine.WriteRenderedPage), ct);
            var texts = new List<string>();
            foreach (var page in pages)
                texts.Add(await RecognizeAsync(page, ct));
            return string.Join("\n\n", texts.Where(t => t.Length > 0));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ---------- Tesseract ----------

    private static string? FindTesseract()
    {
        var exe = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).AsEnumerable();
        if (OperatingSystem.IsMacOS())
            folders = folders.Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin" });
        if (OperatingSystem.IsWindows())
            folders = folders.Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR"));
        return folders.Where(d => !string.IsNullOrWhiteSpace(d))
                      .Select(d => Path.Combine(d.Trim(), exe))
                      .FirstOrDefault(File.Exists);
    }

    /// <summary>"eng+ara" when the Arabic data is installed (brew install tesseract-lang), otherwise "eng".</summary>
    private static string TesseractLanguages(string tesseract)
    {
        try
        {
            // Off the UI thread: waiting there for an await that wants the UI thread would deadlock.
            var r = Task.Run(() => ProcessRunner.RunAsync(tesseract, ["--list-langs"])).GetAwaiter().GetResult();
            var langs = (r.Output + "\n" + r.ErrorTail).Split('\n').Select(l => l.Trim()).ToHashSet();
            return langs.Contains("ara") ? "eng+ara" : "eng";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "eng";
        }
    }

    // ---------- Windows OCR (Windows.Media.Ocr, through Windows PowerShell's WinRT support) ----------

    private static string WindowsScript()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskWheel");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "wheel-ocr-v1.ps1");
        if (!File.Exists(path) || File.ReadAllText(path) != WindowsOcrScript)
            File.WriteAllText(path, WindowsOcrScript, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    /// <summary>
    /// Runs Windows' built-in OCR. Uses Arabic when its language pack is installed and the text is mostly
    /// Arabic; otherwise the user's own languages (English for most).
    /// </summary>
    private const string WindowsOcrScript = """
        param([Parameter(Mandatory = $true)][string]$Path)
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
        $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
        $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]
        $null = [Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime]

        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        function Await($op, [Type]$type) {
            $task = $asTask.MakeGenericMethod($type).Invoke($null, @($op))
            $null = $task.Wait(-1)
            $task.Result
        }

        $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Path)) ([Windows.Storage.StorageFile])
        $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
        $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $max = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
        if ($decoder.PixelWidth -gt $max -or $decoder.PixelHeight -gt $max) {
            # Windows OCR refuses very large images: scale down to fit.
            $scale = [Math]::Min($max / $decoder.PixelWidth, $max / $decoder.PixelHeight)
            $transform = New-Object Windows.Graphics.Imaging.BitmapTransform
            $transform.ScaledWidth = [uint32][Math]::Floor($decoder.PixelWidth * $scale)
            $transform.ScaledHeight = [uint32][Math]::Floor($decoder.PixelHeight * $scale)
            $bitmap = Await ($decoder.GetSoftwareBitmapAsync(
                [Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied,
                $transform, [Windows.Graphics.Imaging.ExifOrientationMode]::RespectExifOrientation,
                [Windows.Graphics.Imaging.ColorManagementMode]::DoNotColorManage)) ([Windows.Graphics.Imaging.SoftwareBitmap])
        } else {
            $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        }

        function Read-With($engine) {
            if ($null -eq $engine) { return $null }
            $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
            ($result.Lines | ForEach-Object { $_.Text }) -join "`n"
        }
        function Is-Arabic([string]$text) {
            $letters = ($text.ToCharArray() | Where-Object { [char]::IsLetter($_) }).Count
            $arabic = ([regex]::Matches($text, '[؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]')).Count
            return ($letters -gt 0 -and $arabic * 2 -ge $letters)
        }

        $default = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
        $text = Read-With $default
        $arabicLang = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | Where-Object { $_.LanguageTag -like 'ar*' } | Select-Object -First 1
        if ($null -ne $arabicLang -and ($null -eq $default -or $default.RecognizerLanguage.LanguageTag -notlike 'ar*')) {
            $arabic = Read-With ([Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($arabicLang))
            if ($arabic -and (Is-Arabic $arabic)) { $text = $arabic }
        }
        if ($null -eq $default -and $null -eq $arabicLang) {
            [Console]::Error.WriteLine('Windows has no OCR language installed. Add one in Settings > Time & language > Language.')
            exit 4
        }
        [Console]::Out.Write($text)
        """;
}
