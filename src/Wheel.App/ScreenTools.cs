using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Capture;
using Wheel.Core.Converters;
using Wheel.Core.Screen;

namespace Wheel.App;

/// <summary>What the screen tools need from the app.</summary>
internal interface IScreenToolHost
{
    IPlatform Platform { get; }
    string CaptureFolder { get; }
    void Notify(string title, string text, bool error = false, Action? onClick = null);
    void ShowPreview(string imagePath);
    /// <summary>Hides the floating button (and closes the wheel) while the screen is being worked on.</summary>
    void HideChrome();
    void RestoreChrome();
}

/// <summary>Colour picker, QR codes and screen recording.</summary>
internal sealed class ScreenTools
{
    private readonly IScreenToolHost _host;
    private Recording? _recording;
    private bool _busy;

    public ScreenTools(IScreenToolHost host) => _host = host;

    public bool IsRecording => _recording is not null;

    private IPlatform Platform => _host.Platform;
    private static string Paste => OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V";

    /// <summary>A still picture of the screen under the pointer, and that screen.</summary>
    internal async Task<(string Picture, Screen Screen)?> FreezeScreenAsync(Window anyWindow)
    {
        var cursor = Platform.CursorPixel(anyWindow.Screens);
        var screen = anyWindow.Screens.ScreenFromPoint(cursor) ?? anyWindow.Screens.Primary;
        if (screen is null) return null;
        var temp = Path.Combine(Path.GetTempPath(), $"wheel-screen-{Guid.NewGuid():N}.png");
        var (ok, problem) = await Platform.CaptureAsync(CaptureKind.FullScreen, temp);
        if (!ok)
        {
            if (problem is not null) _host.Notify("Couldn't see the screen", problem, error: true);
            return null;
        }
        return (temp, screen);
    }

    // ---------- colour ----------

    public async Task PickColorAsync(Window anyWindow)
    {
        if (_busy) return;
        _busy = true;
        string? picture = null;
        try
        {
            _host.HideChrome();
            await Task.Delay(150);
            if (await FreezeScreenAsync(anyWindow) is not { } frozen) return;
            picture = frozen.Picture;
            Platform.BringToFront();
            if (await ScreenOverlayWindow.PickColorAsync(picture, frozen.Screen.Bounds, frozen.Screen.Scaling) is not { } c) return;
            var hex = ScreenColor.Hex(c.Red, c.Green, c.Blue);
            Platform.CopyText(hex);
            _host.Notify($"Copied {hex}", $"{ScreenColor.Rgb(c.Red, c.Green, c.Blue)} · {ScreenColor.Hsl(c.Red, c.Green, c.Blue)}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Write(ex);
            _host.Notify("Couldn't pick a colour", ex.Message, error: true);
        }
        finally
        {
            Delete(picture);
            _host.RestoreChrome();
            _busy = false;
        }
    }

    // ---------- QR codes ----------

    /// <summary>Drag over a QR code or barcode anywhere on screen: its text (or link) is copied.</summary>
    public async Task ScanQrAsync()
    {
        if (_busy) return;
        _busy = true;
        var temp = Path.Combine(Path.GetTempPath(), $"wheel-qr-{Guid.NewGuid():N}.png");
        try
        {
            _host.HideChrome();
            await Task.Delay(150);
            var (ok, problem) = await Platform.CaptureAsync(CaptureKind.Area, temp);
            if (!ok)
            {
                if (problem is not null) _host.Notify("QR code", problem, error: true);
                return;
            }
            var found = await Task.Run(() => ReadCodes(temp));
            if (found.Count == 0)
            {
                _host.Notify("No QR code found", "Select the whole code, with a little space around it.", error: true);
                return;
            }
            var text = string.Join(Environment.NewLine, found);
            Platform.CopyText(text);
            var first = found[0].Trim();
            if (QrCodes.IsWebLink(first))
                _host.Notify("Link copied · click to open", first, onClick: () => OpenLink(first));
            else
                _host.Notify(found.Count == 1 ? "QR code copied" : $"{found.Count} codes copied", Wheel.Core.Ocr.OcrText.Preview(text));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Log.Write(ex);
            _host.Notify("Couldn't read the code", ex.Message, error: true);
        }
        finally
        {
            Delete(temp);
            _host.RestoreChrome();
            _busy = false;
        }
    }

    private static System.Collections.Generic.IReadOnlyList<string> ReadCodes(string png)
    {
        using var decoded = SKBitmap.Decode(png) ?? throw new IOException("Couldn't read the screenshot.");
        using var bgra = decoded.Copy(SKColorType.Bgra8888) ?? throw new IOException("Couldn't read the screenshot.");
        var pixels = new byte[bgra.Width * bgra.Height * 4];
        for (var y = 0; y < bgra.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(bgra.GetPixels() + y * bgra.RowBytes, pixels, y * bgra.Width * 4, bgra.Width * 4);
        return QrCodes.Read(pixels, bgra.Width, bgra.Height);
    }

    private void OpenLink(string url)
    {
        try
        {
            // A small page that forwards to the link, opened in the browser (works the same on both systems).
            var page = Path.Combine(Path.GetTempPath(), "wheel-link.html");
            File.WriteAllText(page, $"<!doctype html><meta http-equiv=\"refresh\" content=\"0;url={System.Net.WebUtility.HtmlEncode(url)}\">");
            Platform.OpenInBrowser(page);
        }
        catch (IOException ex)
        {
            Log.Write(ex);
        }
    }

    /// <summary>Makes a QR code from the copied text or link and shows it on the screenshot card.</summary>
    public void MakeQr()
    {
        try
        {
            var text = Platform.ReadClipboard()?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                _host.Notify("Copy something first", "Copy a link or some text, then choose Make QR.", error: true);
                return;
            }
            var (w, h, bgra) = QrCodes.Make(text);
            Directory.CreateDirectory(_host.CaptureFolder);
            var path = OutputPaths.Unique(Path.Combine(_host.CaptureFolder, $"QR code {DateTime.Now:yyyy-MM-dd} at {DateTime.Now:HH.mm.ss}.png"));
            ImageEngine.WriteRenderedPage(path, w, h, bgra, w * 4);
            AppHistory.Record("Converted", path, null, "QR code: " + Wheel.Core.Ocr.OcrText.Preview(text, 50));
            _host.ShowPreview(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _host.Notify("Couldn't make a QR code", ex.Message, error: true);
        }
    }

    // ---------- screen recording ----------

    /// <summary>Starts recording an area (or stops the recording in progress).</summary>
    public async Task RecordAsync(Window anyWindow)
    {
        if (_recording is not null)
        {
            await StopRecordingAsync();
            return;
        }
        if (_busy) return;
        var ffmpeg = FfmpegConverter.Locate();
        if (ffmpeg is null)
        {
            _host.Notify("Screen recording needs ffmpeg",
                OperatingSystem.IsMacOS() ? "Run: brew install ffmpeg-full, then reopen Desk Wheel." : "Run: winget install Gyan.FFmpeg, then reopen Desk Wheel.", error: true);
            return;
        }

        _busy = true;
        string? picture = null;
        try
        {
            _host.HideChrome();
            await Task.Delay(150);
            if (await FreezeScreenAsync(anyWindow) is not { } frozen) return;
            picture = frozen.Picture;
            Platform.BringToFront();
            if (await ScreenOverlayWindow.SelectAreaAsync(picture, frozen.Screen.Bounds, frozen.Screen.Scaling) is not { } area) return;

            Directory.CreateDirectory(_host.CaptureFolder);
            var output = OutputPaths.Unique(Path.Combine(_host.CaptureFolder, RecordingPlan.FileName(DateTime.Now)));
            System.Collections.Generic.IReadOnlyList<string> args;
            if (OperatingSystem.IsMacOS())
            {
                var list = await ProcessRunner.RunAsync(ffmpeg, RecordingPlan.MacListDevicesArgs());
                var device = RecordingPlan.MacScreenDevice(list.ErrorTail + "\n" + list.Output, Platform.ScreenNumberUnderPointer());
                if (device is null)
                {
                    _host.Notify("Couldn't find the screen to record", "ffmpeg didn't list this display. Try the main display.", error: true);
                    return;
                }
                args = RecordingPlan.Mac(device.Value, area, output, H264(ffmpeg));
            }
            else
            {
                var b = frozen.Screen.Bounds;
                args = RecordingPlan.Windows(new ScreenRect(b.X + area.X, b.Y + area.Y, area.Width, area.Height), output, H264(ffmpeg));
            }

            var recording = Recording.Start(ffmpeg, args, output);
            _recording = recording;
            var pill = new RecordingPill(() => _ = StopRecordingAsync());
            recording.Pill = pill;
            pill.ShowBeside(frozen.Screen, area);
            Log.Trace($"  Recording {area} → {output}");
            _ = WatchAsync(recording);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Write(ex);
            _host.Notify("Couldn't start recording", ex.Message, error: true);
            _host.RestoreChrome();
        }
        finally
        {
            Delete(picture);
            _busy = false;
            if (_recording is null) _host.RestoreChrome();
        }
    }

    private static System.Collections.Generic.IReadOnlyList<string>? H264(string ffmpeg) =>
        FfmpegConverter.H264Encoder(FfmpegConverter.Encoders(ffmpeg)) is { } encoder
            ? FfmpegConverter.H264Args(encoder, FfmpegConverter.VideoQuality.Normal)
            : null;

    /// <summary>ffmpeg ends on its own after the time limit (or if it fails): finish up either way.</summary>
    private async Task WatchAsync(Recording recording)
    {
        await recording.Exited;
        if (ReferenceEquals(_recording, recording))
            await StopRecordingAsync();
    }

    public async Task StopRecordingAsync()
    {
        if (_recording is not { } recording) return;
        _recording = null;
        recording.Pill?.Close();
        await recording.StopAsync();
        _host.RestoreChrome();
        if (File.Exists(recording.Output) && new FileInfo(recording.Output).Length > 0)
        {
            AppHistory.Record("Screenshot", recording.Output, null, "Screen recording");
            _host.Notify("Recording saved · click to show",
                $"{Path.GetFileName(recording.Output)} · drop it on the button for GIF or a smaller MP4",
                onClick: () => Platform.Reveal(recording.Output));
        }
        else
        {
            _host.Notify("The recording didn't work", recording.ErrorTail.Length > 0 ? recording.ErrorTail : "ffmpeg stopped without saving.", error: true);
        }
    }

    private static void Delete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A running ffmpeg recording. Stopped by sending "q", as if pressed in its terminal.</summary>
    private sealed class Recording
    {
        private readonly Process _process;
        private readonly StringBuilder _errors = new();

        public string Output { get; }
        public RecordingPill? Pill { get; set; }
        public Task Exited { get; }
        public string ErrorTail
        {
            get { lock (_errors) return _errors.ToString().Trim(); }
        }

        private Recording(Process process, string output)
        {
            _process = process;
            Output = output;
            Exited = process.WaitForExitAsync();
        }

        public static Recording Start(string ffmpeg, System.Collections.Generic.IReadOnlyList<string> args, string output)
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var process = Process.Start(psi) ?? throw new IOException("ffmpeg didn't start.");
            var recording = new Recording(process, output);
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (recording._errors)
                {
                    recording._errors.AppendLine(e.Data);
                    if (recording._errors.Length > 4000) recording._errors.Remove(0, recording._errors.Length - 4000);
                }
            };
            process.OutputDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            return recording;
        }

        public async Task StopAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    await _process.StandardInput.WriteAsync("q");
                    await _process.StandardInput.FlushAsync();
                    _process.StandardInput.Close();
                }
                var finished = await Task.WhenAny(Exited, Task.Delay(TimeSpan.FromSeconds(15)));
                if (finished != Exited && !_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                Log.Write(ex);
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}

/// <summary>The small "● 0:12  Stop" bar shown just outside the area being recorded.</summary>
internal sealed class RecordingPill : Window
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly TextBlock _time = new() { Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public RecordingPill(Action stop)
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var stopButton = new Button
        {
            Content = "Stop", Padding = new Thickness(12, 4), Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x38, 0x3B)),
        };
        stopButton.Click += (_, _) => stop();
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1C, 0x1D, 0x21)), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 6, 6, 6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children =
                {
                    new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x38, 0x3B)), VerticalAlignment = VerticalAlignment.Center },
                    _time,
                    stopButton,
                },
            },
        };
        _time.Text = "0:00";
        _tick.Tick += (_, _) =>
        {
            var t = DateTime.UtcNow - _started;
            _time.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        };
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
    }

    /// <summary>Below the area if there's room, else above it, else in the screen's top corner.</summary>
    public void ShowBeside(Avalonia.Platform.Screen screen, ScreenRect area)
    {
        Show();
        var b = screen.Bounds;
        var h = (int)Math.Max(44 * screen.Scaling, Bounds.Height * screen.Scaling);
        var gap = (int)(10 * screen.Scaling);
        var x = b.X + area.X;
        var below = b.Y + area.Y + area.Height + gap;
        var above = b.Y + area.Y - h - gap;
        var y = below + h < b.Bottom ? below : above > b.Y ? above : b.Y + gap;
        Position = new PixelPoint(x, y);
    }
}
