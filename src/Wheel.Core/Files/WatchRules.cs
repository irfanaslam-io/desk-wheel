namespace Wheel.Core.Files;

/// <summary>A watched folder: every new file that fits gets this option (for example, photos → WebP).</summary>
public sealed record WatchRule(string Folder, string OptionId);

/// <summary>The choices offered for watched folders (a short, safe list: conversions that make a new file).</summary>
public static class WatchChoices
{
    public static readonly (string OptionId, string Label)[] All =
    [
        ("img.to-webp", "Images → WebP"),
        ("img.to-jpg", "Images → JPG"),
        ("img.to-png", "Images → PNG"),
        ("img.compress", "Images → compressed JPG"),
        ("img.strip", "Images → without location (EXIF)"),
        ("vid.to-mp4", "Videos → MP4"),
        ("vid.compress", "Videos → compressed MP4"),
        ("aud.to-mp3", "Audio → MP3"),
        ("doc.to-pdf", "Documents → PDF"),
        ("arc.extract", "Archives → extract"),
    ];

    public static string LabelFor(string optionId) =>
        All.FirstOrDefault(c => c.OptionId == optionId).Label ?? optionId;
}

/// <summary>Decides whether a new file in a watched folder should be processed.</summary>
public static class WatchDecider
{
    /// <param name="madeByWheel">Files Wheel itself wrote recently (so its own output isn't processed again).</param>
    public static bool ShouldProcess(string path, WheelOption option, Capabilities caps, ISet<string> madeByWheel)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.') || name.StartsWith("~$") || madeByWheel.Contains(path)) return false;
        // Downloads still in progress, and editors' temporary files.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".crdownload" or ".part" or ".download" or ".tmp" or ".partial") return false;
        // Wheel's own outputs: "photo (compressed).jpg", "photo (1).webp".
        if (OutputPaths.StemOf(path).EndsWith(')')) return false;
        var mode = option.Mode;
        return OptionRegistry.GetOptions(mode, [path], caps).Any(o => o.Id == option.Id);
    }
}
