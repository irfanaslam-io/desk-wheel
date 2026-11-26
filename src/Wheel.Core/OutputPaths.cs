namespace Wheel.Core;

/// <summary>
/// Output naming rules from the spec: save beside the original, never overwrite,
/// clash → "name (1).ext", same-type edits get a suffix like "name (compressed).jpg".
/// </summary>
public static class OutputPaths
{
    private static readonly string[] DoubleExtensions = [".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz"];

    /// <summary>
    /// When set (Settings → "Save converted files to a folder"), every result goes here instead of beside the original.
    /// Null: beside the original (the default).
    /// </summary>
    public static string? OverrideFolder { get; set; }

    /// <summary>The folder a result for <paramref name="input"/> is written to: the override if set, else the input's folder.</summary>
    public static string FolderFor(string input)
    {
        if (!string.IsNullOrWhiteSpace(OverrideFolder))
        {
            Directory.CreateDirectory(OverrideFolder);
            return OverrideFolder;
        }
        return Path.GetDirectoryName(Path.GetFullPath(input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))) ?? ".";
    }

    /// <summary>Extension including compound ones: "a.tar.gz" → ".tar.gz", "a.PNG" → ".PNG".</summary>
    public static string FullExtension(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var dbl in DoubleExtensions)
            if (name.EndsWith(dbl, StringComparison.OrdinalIgnoreCase))
                return name[^dbl.Length..];
        return Path.GetExtension(name);
    }

    /// <summary>File name without its (possibly compound) extension.</summary>
    public static string StemOf(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var ext = FullExtension(name);
        return ext.Length > 0 ? name[..^ext.Length] : name;
    }

    /// <summary>
    /// A free output path beside <paramref name="input"/>.
    /// </summary>
    /// <param name="input">Source file.</param>
    /// <param name="extension">Output extension with dot; null keeps the source's.</param>
    /// <param name="suffix">Added in brackets, e.g. "compressed". Used automatically when the extension does not change.</param>
    /// <param name="directory">Folder to write into; defaults to the source's folder.</param>
    public static string Beside(string input, string? extension, string? suffix = null, string? directory = null)
    {
        var dir = directory ?? FolderFor(input);
        var stem = StemOf(input);
        var ext = extension ?? FullExtension(input);

        // Same type in, same type out: make the name say what changed, so it never looks like a copy.
        if (suffix is null && string.Equals(ext, FullExtension(input), StringComparison.OrdinalIgnoreCase))
            suffix = "edited";

        var baseName = suffix is null ? stem : $"{stem} ({suffix})";
        return Unique(Path.Combine(dir, baseName + ext));
    }

    /// <summary>A free folder path beside <paramref name="input"/>, named after its stem.</summary>
    public static string FolderBeside(string input)
    {
        return Unique(Path.Combine(FolderFor(input), StemOf(input)));
    }

    /// <summary>Returns <paramref name="path"/> if nothing exists there, else "name (1).ext", "name (2).ext"…</summary>
    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? ".";
        var stem = StemOf(path);
        var ext = FullExtension(path);
        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        throw new IOException($"No free name for {path}");
    }
}
