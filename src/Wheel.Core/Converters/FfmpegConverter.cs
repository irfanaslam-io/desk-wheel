using System.Runtime.InteropServices;

namespace Wheel.Core.Converters;

/// <summary>
/// Video, audio and WebP/AVIF through ffmpeg.exe. Looked up in "engines\" beside the app, then on PATH.
/// Arguments are passed as a list (never a joined string), so file names can't inject options.
/// </summary>
public static class FfmpegConverter
{
    private static string? _cachedPath;
    private static bool _searched;

    /// <summary>Settings → "ffmpeg location": a specific ffmpeg to use. Null: search automatically.</summary>
    public static string? OverridePath { get; private set; }

    /// <summary>Sets (or clears) the ffmpeg location and forgets the last search.</summary>
    public static void SetOverride(string? path)
    {
        OverridePath = string.IsNullOrWhiteSpace(path) ? null : path;
        _searched = false;
        _cachedPath = null;
    }

    /// <summary>Full path to ffmpeg, or null when it isn't installed.</summary>
    public static string? Locate()
    {
        if (_searched)
            return _cachedPath;
        _searched = true;

        if (OverridePath is not null && File.Exists(OverridePath))
            return _cachedPath = OverridePath;

        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
        var bundled = Path.Combine(AppContext.BaseDirectory, "engines", exe);
        if (File.Exists(bundled))
            return _cachedPath = bundled;

        foreach (var dir in SearchFolders())
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
                return _cachedPath = candidate;
        }
        return null;
    }

    /// <summary>
    /// PATH, plus Homebrew's folders on macOS: apps opened from Finder get a minimal PATH without
    /// /opt/homebrew/bin, so a brew-installed ffmpeg would otherwise never be found.
    /// ffmpeg-full (more encoders) is preferred; it is keg-only, so it isn't on PATH even in a terminal.
    /// </summary>
    private static IEnumerable<string> SearchFolders()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/opt/ffmpeg-full/bin"; // Apple silicon
            yield return "/usr/local/opt/ffmpeg-full/bin";    // Intel
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir))
                yield return dir.Trim();
        if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
        }
    }

    private static string InstallHint => OperatingSystem.IsMacOS()
        ? "brew install ffmpeg-full"
        : "winget install Gyan.FFmpeg";

    public static async Task<JobResult> RunAsync(WheelOption option, string input, CancellationToken ct = default)
    {
        var ffmpeg = Locate();
        if (ffmpeg is null)
            return JobResult.Fail($"ffmpeg isn't installed. Run: {InstallHint}");

        var output = OutputPaths.Beside(input, option.TargetExtension, Suffixes.For(option));
        var encoders = Encoders(ffmpeg);
        var args = BuildArguments(option, input, output, encoders: encoders);

        var (code, errorTail) = await RunProcessAsync(ffmpeg, args, ct);

        // A clip shorter than 1 s has no frame at 1 s: take the first frame instead.
        if (option.Id == "vid.snapshot" && (code != 0 || !File.Exists(output)))
        {
            TryDelete(output);
            args = BuildArguments(option, input, output, snapshotAt: "0", encoders: encoders);
            (code, errorTail) = await RunProcessAsync(ffmpeg, args, ct);
        }

        if (code == 0 && File.Exists(output) && new FileInfo(output).Length > 0)
            return JobResult.Ok(output);

        TryDelete(output);
        return JobResult.Fail(FriendlyError(errorTail));
    }

    // ---------- encoders ----------

    public enum VideoQuality { Normal, Small }

    private static IReadOnlySet<string>? _encoders;
    private static string? _encodersOf;

    /// <summary>The encoders this ffmpeg has (from "ffmpeg -encoders"), probed once per ffmpeg.</summary>
    public static IReadOnlySet<string> Encoders(string ffmpeg)
    {
        if (_encoders is not null && _encodersOf == ffmpeg) return _encoders;
        try
        {
            var r = Task.Run(() => ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-encoders"])).GetAwaiter().GetResult();
            _encoders = ParseEncoders(r.Output);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _encoders = new HashSet<string>();
        }
        _encodersOf = ffmpeg;
        return _encoders;
    }

    /// <summary>Encoder names from "ffmpeg -encoders" output (lines like " V....D libx264  H.264…").</summary>
    public static IReadOnlySet<string> ParseEncoders(string output) =>
        output.Split('\n')
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^\s[VAS][A-Z.]{5}\s+(\S+)"))
            .Where(m => m.Success && m.Groups[1].Value != "=")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

    /// <summary>
    /// The H.264 encoder to use, best first: x264 (if this ffmpeg has it), then the system's own encoder
    /// (VideoToolbox on Mac, Media Foundation on Windows), then OpenH264. Null when there's none.
    /// </summary>
    public static string? H264Encoder(IReadOnlySet<string> encoders) =>
        new[] { "libx264", "h264_videotoolbox", "h264_mf", "libopenh264" }.FirstOrDefault(encoders.Contains);

    /// <summary>Settings for an H.264 encoder: "Normal" looks like the original; "Small" is for Compress.</summary>
    public static IReadOnlyList<string> H264Args(string encoder, VideoQuality quality)
    {
        var small = quality == VideoQuality.Small;
        return encoder switch
        {
            "h264_videotoolbox" when RuntimeInformation.ProcessArchitecture == Architecture.Arm64 =>
                ["-c:v", "h264_videotoolbox", "-q:v", small ? "48" : "65", "-allow_sw", "1", "-pix_fmt", "yuv420p"],
            "h264_videotoolbox" =>
                ["-c:v", "h264_videotoolbox", "-b:v", small ? "2500k" : "6M", "-allow_sw", "1", "-pix_fmt", "yuv420p"],
            "h264_mf" => ["-c:v", "h264_mf", "-rate_control", "quality", "-quality", small ? "45" : "70", "-pix_fmt", "nv12"],
            "libopenh264" => ["-c:v", "libopenh264", "-b:v", small ? "2500k" : "6M", "-pix_fmt", "yuv420p"],
            _ => ["-c:v", "libx264", "-preset", "veryfast", "-crf", small ? "28" : "23", "-pix_fmt", "yuv420p"],
        };
    }

    /// <summary>Encoders an option needs, other than ffmpeg's built-in ones. "h264" means any H.264 encoder.</summary>
    public static IReadOnlyList<string> NeededEncoders(string optionId) => optionId switch
    {
        "vid.to-mp4" or "vid.to-mov" or "vid.compress" or "vid.half" => ["h264"],
        "vid.to-webm" => ["libvpx-vp9", "libopus"],
        "vid.to-mp3" or "aud.to-mp3" or "aud.compress" => ["libmp3lame"],
        "aud.to-ogg" => ["libvorbis"],
        "aud.to-opus" => ["libopus"],
        "img.to-avif" => ["libaom-av1"],
        _ => [],
    };

    public static bool CanDo(string optionId, IReadOnlySet<string> encoders) =>
        NeededEncoders(optionId).All(e => e == "h264" ? H264Encoder(encoders) is not null : encoders.Contains(e));

    /// <summary>The ffmpeg argument list for an option. Public so tests can check it without running ffmpeg.</summary>
    /// <param name="encoders">What this ffmpeg can encode with; null assumes a full build (x264).</param>
    public static List<string> BuildArguments(WheelOption option, string input, string output, string snapshotAt = "1",
                                              IReadOnlySet<string>? encoders = null)
    {
        var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" };

        if (option.Id == "vid.snapshot")
            a.AddRange(["-ss", snapshotAt]); // seek before -i: fast

        a.AddRange(["-i", input]);

        var encoder = encoders is null ? "libx264" : H264Encoder(encoders) ?? "libx264";
        var h264 = H264Args(encoder, VideoQuality.Normal);
        var h264Small = H264Args(encoder, VideoQuality.Small);

        switch (option.Id)
        {
            // Images via ffmpeg
            case "img.to-avif": a.AddRange(["-frames:v", "1", "-c:v", "libaom-av1", "-still-picture", "1", "-crf", "30", "-cpu-used", "6"]); break;

            // Video conversions
            case "vid.to-mp4":
                a.AddRange(h264); a.AddRange(["-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"]); break;
            case "vid.to-mov":
                a.AddRange(h264); a.AddRange(["-c:a", "aac", "-b:a", "160k"]); break;
            case "vid.to-webm":
                a.AddRange(["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-deadline", "good", "-cpu-used", "4", "-row-mt", "1", "-c:a", "libopus", "-b:a", "128k"]); break;
            case "vid.to-mkv":
                a.AddRange(["-map", "0", "-c", "copy"]); break;
            case "vid.to-gif":
                a.AddRange(["-vf", "fps=12,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0"]); break;
            case "vid.to-mp3":
                a.AddRange(["-vn", "-c:a", "libmp3lame", "-q:a", "2"]); break;
            case "vid.to-m4a":
                a.AddRange(["-vn", "-c:a", "aac", "-b:a", "192k"]); break;

            // Video tools
            case "vid.compress":
                a.AddRange(h264Small); a.AddRange(["-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"]); break;
            case "vid.half":
                a.AddRange(h264); a.AddRange(["-vf", "scale=trunc(iw/4)*2:trunc(ih/4)*2", "-c:a", "copy"]); break;
            case "vid.mute":
                a.AddRange(["-map", "0:v", "-c:v", "copy", "-an"]); break;
            case "vid.snapshot":
                a.AddRange(["-frames:v", "1"]); break;

            // Audio conversions
            case "aud.to-mp3": a.AddRange(["-vn", "-c:a", "libmp3lame", "-b:a", "192k"]); break;
            case "aud.to-m4a": a.AddRange(["-vn", "-c:a", "aac", "-b:a", "192k"]); break;
            case "aud.to-wav": a.AddRange(["-vn", "-c:a", "pcm_s16le"]); break;
            case "aud.to-flac": a.AddRange(["-vn", "-c:a", "flac"]); break;
            case "aud.to-ogg": a.AddRange(["-vn", "-c:a", "libvorbis", "-q:a", "5"]); break;
            case "aud.to-opus": a.AddRange(["-vn", "-c:a", "libopus", "-b:a", "128k"]); break;

            // Audio tools (keep the container; ffmpeg picks its default encoder for it)
            case "aud.normalize": a.AddRange(["-vn", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11"]); break;
            case "aud.mono": a.AddRange(["-vn", "-ac", "1"]); break;
            case "aud.compress": a.AddRange(["-vn", "-c:a", "libmp3lame", "-b:a", "96k"]); break;

            default:
                throw new NotSupportedException($"No ffmpeg recipe for {option.Id}");
        }

        a.AddRange(["-map_metadata", "0"]);
        a.Add(output);
        return a;
    }

    private static async Task<(int Code, string ErrorTail)> RunProcessAsync(string exe, List<string> args, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(exe, args, ct);
        return (r.ExitCode, r.ErrorTail);
    }

    private static string FriendlyError(string ffmpegError)
    {
        if (ffmpegError.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase))
            return "This ffmpeg can't write that format. Choose a different ffmpeg in Settings → Conversions.";
        if (ffmpegError.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
            return "The file looks damaged or isn't really this format.";
        if (ffmpegError.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
            || ffmpegError.Contains("matches no streams", StringComparison.OrdinalIgnoreCase))
            return "There's no audio or video in this file to convert.";
        var lastLine = ffmpegError.Trim().Split('\n').LastOrDefault()?.Trim();
        return string.IsNullOrEmpty(lastLine) ? "ffmpeg failed without an error message." : lastLine;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
