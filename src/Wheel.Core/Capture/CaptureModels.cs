namespace Wheel.Core.Capture;

/// <summary>What to capture.</summary>
public enum CaptureKind
{
    /// <summary>Drag out a rectangle.</summary>
    Area,
    /// <summary>Click a window.</summary>
    Window,
    /// <summary>The whole screen under the pointer.</summary>
    FullScreen,
}

/// <summary>Where screenshots go and what they're called.</summary>
public static class CaptureNaming
{
    /// <summary>Default folder: Pictures/Wheel Screenshots.</summary>
    public static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) is { Length: > 0 } pictures
            ? pictures
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures"),
        "DeskWheel Screenshots");

    /// <summary>"Screenshot 2026-11-23 at 21.40.12.png", made unique in <paramref name="folder"/>.</summary>
    public static string NewPath(string folder, DateTime when)
    {
        Directory.CreateDirectory(folder);
        // Dots, not colons: colons aren't allowed in Windows file names and show as slashes in Finder.
        var name = $"Screenshot {when:yyyy-MM-dd} at {when:HH.mm.ss}.png";
        return OutputPaths.Unique(Path.Combine(folder, name));
    }
}
