namespace Wheel.Core;

/// <summary>
/// Lets a selected folder use the wheels for the files inside it: a folder of photos gets the image
/// conversions and tools, applied to every photo directly inside (not sub-folders).
/// </summary>
public static class FolderContents
{
    public const int MaxFiles = 1000;

    /// <summary>The files a folder selection will work on, all of one kind.</summary>
    public sealed record Batch(FileKind Kind, IReadOnlyList<string> Files);

    /// <summary>
    /// For a selection made only of folders: the most common convertible kind of file directly inside them.
    /// Null when the selection includes files, or the folders hold nothing Wheel can work with.
    /// </summary>
    public static Batch? Of(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || !paths.All(Directory.Exists))
            return null;

        var group = paths
            .SelectMany(FilesIn)
            .Where(f => !Path.GetFileName(f).StartsWith('.')) // .DS_Store and other hidden files
            .GroupBy(FileClassifier.Classify)
            .Where(g => g.Key is not (FileKind.Other or FileKind.Folder))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .FirstOrDefault();

        if (group is null)
            return null;
        var files = group.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(MaxFiles).ToList();
        return new Batch(group.Key, files);
    }

    /// <summary>"12 images", "1 PDF".</summary>
    public static string Describe(Batch batch)
    {
        var n = batch.Files.Count;
        var noun = batch.Kind switch
        {
            FileKind.Image => "image",
            FileKind.Video => "video",
            FileKind.Audio => "audio file",
            FileKind.Pdf => "PDF",
            FileKind.Archive => "archive",
            FileKind.Subtitle => "subtitle file",
            FileKind.Document => "document",
            FileKind.Spreadsheet => "spreadsheet",
            FileKind.Presentation => "presentation",
            FileKind.Markdown => "Markdown file",
            FileKind.Json => "JSON file",
            _ => "file",
        };
        return $"{n} {noun}{(n == 1 ? "" : "s")}";
    }

    private static IEnumerable<string> FilesIn(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
