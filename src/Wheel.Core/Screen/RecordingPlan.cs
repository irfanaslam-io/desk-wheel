using System.Text.RegularExpressions;

namespace Wheel.Core.Screen;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    /// <summary>H.264 needs even sizes: shrinks by a pixel where needed, never grows.</summary>
    public ScreenRect Even() => this with { Width = Width & ~1, Height = Height & ~1 };
}

/// <summary>
/// ffmpeg command lines for recording part of the screen to MP4 (H.264, plays everywhere).
/// macOS uses AVFoundation's screen capture; Windows uses GDI desktop capture. Stop by writing "q" to ffmpeg.
/// </summary>
public static partial class RecordingPlan
{
    public const int FramesPerSecond = 30;
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(15);

    /// <summary>The AVFoundation device index of "Capture screen N", from ffmpeg's -list_devices output.</summary>
    public static int? MacScreenDevice(string listDevicesOutput, int screenNumber)
    {
        foreach (Match m in ScreenDevice().Matches(listDevicesOutput))
            if (int.Parse(m.Groups[2].Value) == screenNumber)
                return int.Parse(m.Groups[1].Value);
        return null;
    }

    [GeneratedRegex(@"\[(\d+)\]\s+Capture screen (\d+)")]
    private static partial Regex ScreenDevice();

    public static IReadOnlyList<string> MacListDevicesArgs() =>
        ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""];

    /// <param name="device">AVFoundation index of the screen (see <see cref="MacScreenDevice"/>).</param>
    /// <param name="area">Part of that screen, in its pixels.</param>
    /// <param name="h264">Encoder settings (FfmpegConverter.H264Args); null: x264.</param>
    public static IReadOnlyList<string> Mac(int device, ScreenRect area, string output, IReadOnlyList<string>? h264 = null)
    {
        var r = area.Even();
        return
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "avfoundation", "-capture_cursor", "1", "-capture_mouse_clicks", "1",
            "-framerate", FramesPerSecond.ToString(), "-pixel_format", "uyvy422",
            "-i", $"{device}:none",
            "-vf", $"crop={r.Width}:{r.Height}:{r.X}:{r.Y}",
            .. Encode(output, h264),
        ];
    }

    /// <param name="area">In virtual-desktop pixels (all monitors).</param>
    public static IReadOnlyList<string> Windows(ScreenRect area, string output, IReadOnlyList<string>? h264 = null)
    {
        var r = area.Even();
        return
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "gdigrab", "-draw_mouse", "1", "-framerate", FramesPerSecond.ToString(),
            "-offset_x", r.X.ToString(), "-offset_y", r.Y.ToString(), "-video_size", $"{r.Width}x{r.Height}",
            "-i", "desktop",
            .. Encode(output, h264),
        ];
    }

    private static IEnumerable<string> Encode(string output, IReadOnlyList<string>? h264) =>
    [
        .. h264 ?? ["-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p"],
        "-movflags", "+faststart", "-t", ((int)MaxLength.TotalSeconds).ToString(),
        output,
    ];

    /// <summary>"Screen Recording 2026-11-24 at 14.05.09.mp4".</summary>
    /// <summary>
    /// A recorded video → an animated GIF: 12 frames a second, at most 960 px wide, with a palette made for
    /// this video (sharp text, few stray dots), looping.
    /// </summary>
    public static IReadOnlyList<string> GifArgs(string video, string gif) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", video,
        "-vf", "fps=12,scale='min(960,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
        "-loop", "0", gif,
    ];

    public static string FileName(DateTime at) => $"Screen Recording {at:yyyy-MM-dd} at {at:HH.mm.ss}.mp4";
}
