using System.IO;
using Avalonia.Controls;

namespace Wheel.App;

/// <summary>The Desk Wheel icon, built into the program (see scripts/icons).</summary>
internal static class AppIcons
{
    /// <summary>Title bars, the taskbar and Alt-Tab.</summary>
    public static WindowIcon? Window { get; } = Load("app-256.png");

    /// <summary>
    /// macOS menu bar: a black "template" image that macOS tints to suit a light or dark menu bar.
    /// Windows tray: the colour icon.
    /// </summary>
    public static WindowIcon? Tray { get; } = Load(System.OperatingSystem.IsMacOS() ? "menubar-44.png" : "win-32.png");

    /// <summary>The icon as a picture, for showing inside windows (the Settings sidebar).</summary>
    public static Avalonia.Media.Imaging.Bitmap? Picture { get; } = LoadBitmap("app-256.png");

    private static Avalonia.Media.Imaging.Bitmap? LoadBitmap(string name)
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("DeskWheel." + name);
        return stream is null ? null : new Avalonia.Media.Imaging.Bitmap(stream);
    }

    private static WindowIcon? Load(string name)
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("DeskWheel." + name);
        if (stream is null) return null;
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return new WindowIcon(copy);
    }
}
