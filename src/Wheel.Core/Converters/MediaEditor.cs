using System.Globalization;
using System.Text.RegularExpressions;

namespace Wheel.Core.Converters;

/// <summary>
/// Video and audio editing with the bundled ffmpeg: trim, join, speed, subtitles and a thumbnail sheet.
/// Trim and Join copy the original streams when they can (fast, no quality loss); everything else re-encodes
/// with the same H.264 setup as Convert → MP4.
/// </summary>
public static partial class MediaEditor
{
    // ---------------------------------------------------------------- times

    /// <summary>
    /// "0:30-1:45", "1:00-" (to the end), "-0:20" (the first 20 s), "90-120" (seconds), "1:02:03.5-1:05:00".
    /// Null start = the beginning, null end = the end.
    /// </summary>
    public static (double? Start, double? End, string? Problem) ParseRange(string text, double? duration = null)
    {
        var t = text.Trim().Replace('–', '-').Replace('—', '-').Replace(" to ", "-").Replace(" ", "");
        var dash = t.IndexOf('-');
        if (dash < 0) return (null, null, "Type a start and an end, like 0:30-1:45, 1:00- or -0:20.");
        double? start = dash == 0 ? null : Seconds(t[..dash]);
        double? end = dash == t.Length - 1 ? null : Seconds(t[(dash + 1)..]);
        if ((dash > 0 && start is null) || (dash < t.Length - 1 && end is null))
            return (null, null, "Times look like 1:30, 0:05.5 or 90 (seconds).");
        if (start is null && end is null) return (null, null, "Type at least a start or an end.");
        if (start is { } s && end is { } e && e <= s) return (null, null, "The end must come after the start.");
        if (duration is { } d && start is { } s2 && s2 >= d) return (null, null, $"It's only {Clock(d)} long.");
        return (start, end, null);
    }

    /// <summary>"1:02:03.5" / "2:30" / "90" → seconds.</summary>
    public static double? Seconds(string text)
    {
        var parts = text.Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0) return null;
            total = total * 60 + n;
        }
        return total;
    }

    public static string Clock(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The length of a video or audio file in seconds, read from ffmpeg's report (null if unknown).</summary>
    public static async Task<double?> DurationAsync(string input, CancellationToken ct = default)
    {
        if (FfmpegConverter.Locate() is not { } ffmpeg) return null;
        var r = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", input], ct);
        var m = DurationLine().Match(r.Error);
        return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
                           + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"Duration: (\d+):(\d\d):(\d\d(?:\.\d+)?)")]
    private static partial Regex DurationLine();

    // ---------------------------------------------------------------- trim

    /// <summary>Tools the wheel runs here rather than through a single ffmpeg recipe.</summary>
    public static bool Handles(string optionId) => optionId is "vid.trim" or "aud.trim" or "vid.join" or "aud.join"
        or "vid.speed" or "aud.speed" or "vid.sheet" or "sub.add" or "sub.burn";

    /// <summary>
    /// "clip (trimmed).mp4": the part between <paramref name="start"/> and <paramref name="end"/>.
    /// Audio, and video kept from the very beginning, are copied (fast, nothing re-encoded). Video that starts
    /// later is re-encoded so it starts on the exact frame: a copy could only start at the key frame before it,
    /// which can be seconds early.
    /// </summary>
    public static async Task<JobResult> TrimAsync(string input, double? start, double? end, CancellationToken ct = default)
    {
        if (FfmpegConverter.Locate() is not { } ffmpeg) return JobResult.Fail("ffmpeg is missing from Desk Wheel's folder.");
        var video = FileClassifier.Classify(input) == FileKind.Video;
        var ext = Path.GetExtension(input).ToLowerInvariant();
        var exact = video && start is > 0;
        var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" };
        if (start is > 0) a.AddRange(["-ss", Num(start.Value)]);
        a.AddRange(["-i", input]);
        if (end is { } e) a.AddRange(["-t", Num(e - (start ?? 0))]);
        string output;
        if (exact)
        {
            var encoder = FfmpegConverter.H264Encoder(FfmpegConverter.Encoders(ffmpeg));
            if (encoder is null) return JobResult.Fail("Trimming the middle of a video needs an H.264 encoder this ffmpeg doesn't have.");
            output = OutputPaths.Beside(input, ext is ".mp4" or ".m4v" or ".mov" or ".mkv" ? ext : ".mp4", "trimmed");
            a.AddRange(["-map", "0:v:0", "-map", "0:a?"]);
            a.AddRange(FfmpegConverter.H264Args(encoder, FfmpegConverter.VideoQuality.Normal));
            a.AddRange(["-c:a", "aac", "-b:a", "192k"]);
            if (output.EndsWith(".mp4") || output.EndsWith(".m4v") || output.EndsWith(".mov")) a.AddRange(["-movflags", "+faststart"]);
        }
        else
        {
            output = OutputPaths.Beside(input, ext, "trimmed");
            a.AddRange(["-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero"]);
        }
        a.AddRange(["-map_metadata", "0", output]);
        return await Finish(ffmpeg, a, output, ct);
    }

    // ---------------------------------------------------------------- join

    /// <summary>
    /// "Joined.mp4" (or the first file's type): the files one after another, in name order. Copied when the
    /// files match (same codecs and size), otherwise re-encoded: video to MP4 at the first clip's size, audio to
    /// the first file's format.
    /// </summary>
    public static async Task<JobResult> JoinAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        if (inputs.Count < 2) return JobResult.Fail("Select two or more files to join.");
        if (FfmpegConverter.Locate() is not { } ffmpeg) return JobResult.Fail("ffmpeg is missing from Desk Wheel's folder.");
        var ordered = inputs.OrderBy(Path.GetFileName, Files.NaturalComparer.Instance).ToList();
        var video = FileClassifier.Classify(ordered[0]) == FileKind.Video;
        var ext = Path.GetExtension(ordered[0]).ToLowerInvariant();
        var output = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(ordered[0]), "Joined" + ext));

        // 1. Same codecs: the concat demuxer copies everything (fast, lossless).
        var list = Path.Combine(Path.GetTempPath(), "deskwheel-join-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(list, ordered.Select(p => "file '" + Path.GetFullPath(p).Replace("\\", "/").Replace("'", "'\\''") + "'"));
        try
        {
            var copy = await Finish(ffmpeg, ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", list,
                                             "-map", "0", "-c", "copy", output], output, ct);
            if (copy.Success && await LooksComplete(output, ordered, ct)) return copy;
            TryDelete(output);
        }
        finally
        {
            TryDelete(list);
        }

        // 2. Different files: re-encoded through the concat filter.
        var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" };
        foreach (var p in ordered) a.AddRange(["-i", p]);
        var filter = new System.Text.StringBuilder();
        if (video)
        {
            var encoder = FfmpegConverter.H264Encoder(FfmpegConverter.Encoders(ffmpeg));
            if (encoder is null) return JobResult.Fail("These videos are different types; joining them needs an H.264 encoder this ffmpeg doesn't have.");
            output = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(ordered[0]), "Joined.mp4"));
            var (w, h) = await SizeAsync(ffmpeg, ordered[0], ct) ?? (1280, 720);
            for (var i = 0; i < ordered.Count; i++)
                filter.Append($"[{i}:v:0]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];")
                      .Append($"[{i}:a:0]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
            for (var i = 0; i < ordered.Count; i++) filter.Append($"[v{i}][a{i}]");
            filter.Append($"concat=n={ordered.Count}:v=1:a=1[v][a]");
            a.AddRange(["-filter_complex", filter.ToString(), "-map", "[v]", "-map", "[a]"]);
            a.AddRange(FfmpegConverter.H264Args(encoder, FfmpegConverter.VideoQuality.Normal));
            a.AddRange(["-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", output]);
        }
        else
        {
            for (var i = 0; i < ordered.Count; i++) filter.Append($"[{i}:a:0]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
            for (var i = 0; i < ordered.Count; i++) filter.Append($"[a{i}]");
            filter.Append($"concat=n={ordered.Count}:v=0:a=1[a]");
            a.AddRange(["-filter_complex", filter.ToString(), "-map", "[a]"]);
            a.AddRange(AudioCodec(ext));
            a.Add(output);
        }
        var joined = await Finish(ffmpeg, a, output, ct);
        return joined.Success ? joined
            : JobResult.Fail(video ? "These videos couldn't be joined (one may have no sound). Convert them to MP4 first, then join." : joined.Error ?? "Couldn't join them.");
    }

    /// <summary>A copied join can quietly drop parts when the files differ: the result must be about as long as them all.</summary>
    private static async Task<bool> LooksComplete(string output, IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var total = 0.0;
        foreach (var p in inputs) total += await DurationAsync(p, ct) ?? 0;
        var joined = await DurationAsync(output, ct) ?? 0;
        return total == 0 || joined >= total * 0.97;
    }

    private static async Task<(int W, int H)?> SizeAsync(string ffmpeg, string input, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", input], ct);
        var m = Regex.Match(r.Error, @"Video: .*?, (\d{2,5})x(\d{2,5})");
        return m.Success ? (int.Parse(m.Groups[1].Value) / 2 * 2, int.Parse(m.Groups[2].Value) / 2 * 2) : null;
    }

    /// <summary>The encoder for an audio file type, at everyday quality.</summary>
    public static IReadOnlyList<string> AudioCodec(string ext) => ext switch
    {
        ".mp3" => ["-c:a", "libmp3lame", "-b:a", "192k"],
        ".m4a" or ".aac" or ".mp4" => ["-c:a", "aac", "-b:a", "192k"],
        ".wav" => ["-c:a", "pcm_s16le"],
        ".flac" => ["-c:a", "flac"],
        ".ogg" => ["-c:a", "libvorbis", "-q:a", "5"],
        ".opus" => ["-c:a", "libopus", "-b:a", "128k"],
        _ => ["-c:a", "aac", "-b:a", "192k"],
    };

    // ---------------------------------------------------------------- speed

    public static (double? Factor, string? Problem) ParseSpeed(string text)
    {
        var t = text.Trim().TrimEnd('x', 'X', '×').Replace(',', '.');
        if (t.EndsWith('%') && double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) t = Num(percent / 100);
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return (null, "Type a speed like 2, 1.5 or 0.5.");
        if (f < 0.25 || f > 4) return (null, "Use a speed between 0.25 (four times slower) and 4 (four times faster).");
        if (Math.Abs(f - 1) < 0.001) return (null, "1 is the current speed.");
        return (f, null);
    }

    /// <summary>atempo only goes from 0.5 to 2 at a time, so bigger changes are chained ("atempo=2,atempo=2").</summary>
    public static string AudioTempo(double factor)
    {
        var parts = new List<string>();
        while (factor > 2) { parts.Add("atempo=2"); factor /= 2; }
        while (factor < 0.5) { parts.Add("atempo=0.5"); factor /= 0.5; }
        parts.Add("atempo=" + Num(factor));
        return string.Join(",", parts);
    }

    /// <summary>"clip (2x).mp4" / "song (0.5x).mp3": faster or slower, pitch kept.</summary>
    public static async Task<JobResult> SpeedAsync(string input, double factor, CancellationToken ct = default)
    {
        if (FfmpegConverter.Locate() is not { } ffmpeg) return JobResult.Fail("ffmpeg is missing from Desk Wheel's folder.");
        var video = FileClassifier.Classify(input) == FileKind.Video;
        var ext = Path.GetExtension(input).ToLowerInvariant();
        var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", input };
        string output;
        if (video)
        {
            var encoder = FfmpegConverter.H264Encoder(FfmpegConverter.Encoders(ffmpeg));
            if (encoder is null) return JobResult.Fail("Changing speed needs an H.264 encoder this ffmpeg doesn't have.");
            output = OutputPaths.Beside(input, ".mp4", Num(factor) + "x");
            a.AddRange(["-filter:v", $"setpts=PTS/{Num(factor)}", "-filter:a", AudioTempo(factor)]);
            a.AddRange(FfmpegConverter.H264Args(encoder, FfmpegConverter.VideoQuality.Normal));
            a.AddRange(["-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"]);
        }
        else
        {
            output = OutputPaths.Beside(input, ext, Num(factor) + "x");
            a.AddRange(["-vn", "-filter:a", AudioTempo(factor)]);
            a.AddRange(AudioCodec(ext));
        }
        a.Add(output);
        return await Finish(ffmpeg, a, output, ct);
    }

    // ---------------------------------------------------------------- subtitles

    /// <summary>Exactly one video and one subtitle file (SRT or VTT): what Subtitles and Burn in need.</summary>
    public static bool IsVideoWithSubtitles(IReadOnlyList<string> paths) =>
        paths.Count == 2 && paths.Count(p => FileClassifier.Classify(p) == FileKind.Video) == 1
                         && paths.Count(p => FileClassifier.Classify(p) == FileKind.Subtitle) == 1;

    /// <summary>
    /// "clip (subtitled).mp4": the subtitles added as a track people can turn on and off (nothing re-encoded).
    /// MP4 and MOV keep their type; others become MKV.
    /// </summary>
    public static async Task<JobResult> AddSubtitlesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (FfmpegConverter.Locate() is not { } ffmpeg) return JobResult.Fail("ffmpeg is missing from Desk Wheel's folder.");
        var video = paths.First(p => FileClassifier.Classify(p) == FileKind.Video);
        var subs = paths.First(p => FileClassifier.Classify(p) == FileKind.Subtitle);
        var ext = Path.GetExtension(video).ToLowerInvariant();
        var keep = ext is ".mp4" or ".m4v" or ".mov" or ".mkv";
        var output = OutputPaths.Beside(video, keep ? ext : ".mkv", "subtitled");
        var codec = ext is ".mp4" or ".m4v" or ".mov" ? "mov_text" : "srt";
        return await Finish(ffmpeg, ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", video, "-i", subs,
            "-map", "0", "-map", "1:0", "-c", "copy", "-c:s", codec, "-disposition:s:0", "default", "-map_metadata", "0", output], output, ct);
    }

    /// <summary>True when this ffmpeg can draw subtitles onto video (it was built with libass).</summary>
    public static bool CanBurnSubtitles() => FfmpegConverter.Locate() is { } ffmpeg && FfmpegConverter.Filters(ffmpeg).Contains("subtitles");

    /// <summary>"clip (subtitles burned in).mp4": the subtitles drawn onto the picture, so they show everywhere.</summary>
    public static async Task<JobResult> BurnSubtitlesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (FfmpegConverter.Locate() is not { } ffmpeg) return JobResult.Fail("ffmpeg is missing from Desk Wheel's folder.");
        if (!CanBurnSubtitles()) return JobResult.Fail("This ffmpeg can't draw subtitles. Use Subtitles instead: it adds them as a track.");
        var encoder = FfmpegConverter.H264Encoder(FfmpegConverter.Encoders(ffmpeg));
        if (encoder is null) return JobResult.Fail("Burning in subtitles needs an H.264 encoder this ffmpeg doesn't have.");
        var video = Path.GetFullPath(paths.First(p => FileClassifier.Classify(p) == FileKind.Video));
        var subs = paths.First(p => FileClassifier.Classify(p) == FileKind.Subtitle);
        // The subtitles filter takes a file name inside a filter string, where ':', '\' and quotes mean something:
        // a plain copy in a temporary folder, run from that folder, avoids all of it.
        var temp = Directory.CreateTempSubdirectory("deskwheel-subs").FullName;
        try
        {
            File.Copy(subs, Path.Combine(temp, "subs" + Path.GetExtension(subs).ToLowerInvariant()));
            var output = OutputPaths.Beside(video, ".mp4", "subtitles burned in");
            var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", video,
                "-vf", "subtitles=subs" + Path.GetExtension(subs).ToLowerInvariant() };
            a.AddRange(FfmpegConverter.H264Args(encoder, FfmpegConverter.VideoQuality.Normal));
            a.AddRange(["-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", output]);
            return await Finish(ffmpeg, a, output, ct, temp);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- thumbnail sheet

    /// <summary>"clip (sheet).jpg": 16 frames spread evenly through the video, in a 4 × 4 grid.</summary>
    public static async Task<JobResult> SheetAsync(string input, CancellationToken ct = default)
    {
        if (FfmpegConverter.Locate() is not { } ffmpeg) return JobResult.Fail("ffmpeg is missing from Desk Wheel's folder.");
        var duration = await DurationAsync(input, ct) ?? 0;
        if (duration <= 0) return JobResult.Fail("Couldn't tell how long this video is.");
        var output = OutputPaths.Beside(input, ".jpg", "sheet");
        var rate = 16 / Math.Max(duration, 1);
        return await Finish(ffmpeg, ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", input,
            "-vf", $"fps={Num(rate)},scale=480:-2,tile=4x4:padding=6:margin=6:color=white", "-frames:v", "1", "-q:v", "3", output], output, ct);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<JobResult> Finish(string ffmpeg, IReadOnlyList<string> args, string output, CancellationToken ct, string? workingDirectory = null)
    {
        var r = await ProcessRunner.RunAsync(ffmpeg, args, ct, workingDirectory);
        if (r.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 0)
            return JobResult.Ok(output);
        TryDelete(output);
        var last = r.ErrorTail.Trim().Split('\n').LastOrDefault()?.Trim();
        return JobResult.Fail(string.IsNullOrEmpty(last) ? "ffmpeg failed without an error message." : last);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
