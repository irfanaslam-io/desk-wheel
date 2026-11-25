using System;
using System.IO;
using System.Text.Json;

namespace Wheel.App;

/// <summary>
/// User settings as JSON: ~/Library/Application Support/Wheel/settings.json on Mac,
/// %AppData%\Wheel\settings.json on Windows.
/// </summary>
internal sealed class AppSettings
{
    /// <summary>Master switch for the drag gesture.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>False: the wheel only opens over Finder / File Explorer and the desktop (fewer accidental wheels).</summary>
    public bool WorkInAllApps { get; set; }

    /// <summary>Shown once, on first launch.</summary>
    public bool SeenWelcome { get; set; }

    /// <summary>The floating Wheel button on screen (the main, no-keys way to use Wheel).</summary>
    public bool ShowButton { get; set; } = true;

    /// <summary>Where the floating button was last moved to, in screen pixels. Null: right edge, middle.</summary>
    public int? ButtonX { get; set; }
    public int? ButtonY { get; set; }

    /// <summary>
    /// Optional "drag a file, tap Shift" gesture. Off by default: on macOS it needs the Input Monitoring permission.
    /// </summary>
    public bool KeyGesture { get; set; }

    /// <summary>Where screenshots are saved. Null: Pictures/Wheel Screenshots.</summary>
    public string? CaptureFolder { get; set; }

    /// <summary>Also put each screenshot on the clipboard, ready to paste.</summary>
    public bool CaptureToClipboard { get; set; } = true;

    /// <summary>Show the preview card (Copy / Pin / Edit / Wheel / Show) after each screenshot.</summary>
    public bool ShowCapturePreview { get; set; } = true;

    /// <summary>"png" (sharp, larger) or "jpg" (smaller).</summary>
    public string CaptureFormat { get; set; } = "png";

    /// <summary>
    /// Area screenshots open in place for marking up (arrows, boxes, text, blur…) before saving.
    /// On by default on Windows; on a Mac, macOS's own area picker is used unless this is turned on.
    /// </summary>
    public bool EditAfterCapture { get; set; } = OperatingSystem.IsWindows();

    /// <summary>macOS: keep the soft shadow around window screenshots.</summary>
    public bool WindowShadow { get; set; } = true;

    /// <summary>Where converted files go. Null: beside the original.</summary>
    public string? ConvertFolder { get; set; }

    /// <summary>JPG / WebP quality for conversions, 50–100.</summary>
    public int ImageQuality { get; set; } = 90;

    /// <summary>A specific ffmpeg to use. Null: found automatically.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>"system" (follow macOS / Windows), "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Remember what's copied (kept on this computer only; password-manager copies are skipped).</summary>
    public bool ClipboardHistory { get; set; } = true;

    /// <summary>Watched folders: new files there get the rule's option automatically.</summary>
    public System.Collections.Generic.List<Wheel.Core.Files.WatchRule> WatchFolders { get; set; } = new();

    /// <summary>Segments pinned to the front of their wheel (right-click a segment), by option id.</summary>
    public System.Collections.Generic.List<string> PinnedOptions { get; set; } = new();

    /// <summary>Anonymous usage statistics: null until the person has been asked, then their answer.</summary>
    public bool? ShareUsage { get; set; }

    /// <summary>Look for a newer version every so often and offer to install it.</summary>
    public bool AutoUpdate { get; set; } = true;

    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>A version the person chose to skip.</summary>
    public string? SkippedVersion { get; set; }

    private string? _path;

    public static AppSettings Load(string folder)
    {
        var path = Path.Combine(folder, "settings.json");
        AppSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new AppSettings(); // a broken file falls back to defaults rather than stopping the app
        }
        settings._path = path;
        return settings;
    }

    public void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
