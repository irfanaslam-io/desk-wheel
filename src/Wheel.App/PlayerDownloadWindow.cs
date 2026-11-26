using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Converters;
using Wheel.Core.Disk;
using Wheel.Core.Packs;

namespace Wheel.App;

/// <summary>
/// Shown the first time someone plays a video or song: what the file is (a frame, its length), and a one-time
/// download of the player's engine, or opening it in the system's own player instead. Nothing is downloaded
/// without a click.
/// </summary>
internal sealed class PlayerDownloadWindow : Window
{
    private readonly IPlatform _platform;
    private readonly string? _file;
    private readonly Action _play;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 6, IsVisible = false };
    private readonly TextBlock _status = Ui.Caption("");
    private readonly Button _download;
    private readonly Button _system;
    private readonly Border _thumb;
    private CancellationTokenSource? _cts;

    public PlayerDownloadWindow(IPlatform platform, string? file, Action play)
    {
        _platform = platform;
        _file = file;
        _play = play;
        Ui.Window(this, "Desk Wheel Player");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var pack = MediaEngine.Pack;
        var download = pack.ForThisComputer;
        var system = OperatingSystem.IsMacOS() ? "Open in QuickTime" : "Open in the system player";
        _download = Ui.Primary(download is null ? "Not available here" : $"Download and play ({DiskScanner.FormatSize(download.Size)})", () => _ = DownloadAsync(), IconKind.Save);
        _download.IsVisible = download is not null;
        _system = Ui.Secondary(system, () => { if (_file is not null) _platform.OpenFile(_file); Close(); }, IconKind.Eye);
        _system.IsVisible = file is not null;
        var cancel = Ui.Ghost("Not now", () => { _cts?.Cancel(); Close(); });

        _thumb = new Border
        {
            Height = 220, CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Brushes.Black,
            Child = Ui.Icon(file is not null && FileClassifier.Classify(file) == FileKind.Audio ? IconKind.Music : IconKind.Play, 48, new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF))),
        };
        ((Control)_thumb.Child!).HorizontalAlignment = HorizontalAlignment.Center;
        ((Control)_thumb.Child!).VerticalAlignment = VerticalAlignment.Center;
        var name = new TextBlock { Text = file is null ? "Desk Wheel Player" : Path.GetFileName(file), FontWeight = FontWeight.SemiBold, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = Ui.Caption("", wrap: false);

        var explain = Ui.Body(download is null
            ? file is null ? "Desk Wheel's player isn't available for this computer yet."
                : $"Desk Wheel's player isn't available for this computer yet. You can {char.ToLowerInvariant(system[0])}{system[1..]} instead."
            : "Desk Wheel's player plays every kind of video and music (MKV, MP4, AVI, WebM, FLAC…) with subtitles, " +
              "speed, equalizer and more. It needs its engine first: a one-time download from Desk Wheel's GitHub " +
              $"page ({DiskScanner.FormatSize(download.Size)}, VLC by VideoLAN, free and open source). Remove it any time in Settings.");

        Content = new StackPanel
        {
            Margin = new Thickness(22, 20), Spacing = 12,
            Children =
            {
                _thumb,
                new StackPanel { Spacing = 2, Children = { name, detail } },
                explain,
                _progress,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, _system, _download },
                },
            },
        };
        Closed += (_, _) => _cts?.Cancel();
        _ = DescribeAsync(detail);
    }

    /// <summary>The file's length and a frame from it, with the ffmpeg Desk Wheel already has.</summary>
    private async Task DescribeAsync(TextBlock detail)
    {
        if (_file is null) return;
        try
        {
            var size = DiskScanner.FormatSize(new FileInfo(_file).Length);
            var length = await MediaEditor.DurationAsync(_file);
            detail.Text = length is { } l ? $"{MediaEditor.Clock(l)} · {size}" : size;
            if (FileClassifier.Classify(_file) != FileKind.Video || FfmpegConverter.Locate() is not { } ffmpeg) return;
            var frame = Path.Combine(Path.GetTempPath(), $"wheel-frame-{Guid.NewGuid():N}.png");
            var at = length is { } d ? Math.Min(d * 0.1, 30) : 1;
            var r = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                "-ss", at.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", _file, "-frames:v", "1", "-vf", "scale=960:-2", frame]);
            if (r.ExitCode == 0 && File.Exists(frame))
            {
                using (var stream = File.OpenRead(frame))
                    _thumb.Child = new Image { Source = new Bitmap(stream), Stretch = Stretch.UniformToFill };
                File.Delete(frame);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Log.Write(ex); }
    }

    private async Task DownloadAsync()
    {
        _cts = new CancellationTokenSource();
        _download.IsEnabled = _system.IsEnabled = false;
        _progress.IsVisible = true;
        _status.Text = "Connecting…";
        _status.Foreground = Ui.Muted;
        var progress = new Progress<PackProgress>(p =>
        {
            _progress.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
            _status.Text = p.Unpacking ? "Unpacking…" : $"{DiskScanner.FormatSize(p.Done)} of {DiskScanner.FormatSize(p.Total)}";
        });
        try
        {
            await MediaEngine.Packs.InstallAsync(MediaEngine.Pack, progress, _cts.Token);
            Log.Trace("Media player engine downloaded.");
            Close();
            _play();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Download stopped.";
        }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write(ex);
            _status.Text = ex is System.Net.Http.HttpRequestException
                ? "Couldn't reach GitHub. Check the internet connection and try again."
                : ex.Message;
            _status.Foreground = Ui.DangerText;
            _download.Content = "Try again";
        }
        finally
        {
            _progress.IsVisible = _cts?.IsCancellationRequested == false && _status.Text == "Unpacking…";
            _download.IsEnabled = _system.IsEnabled = true;
        }
    }
}
