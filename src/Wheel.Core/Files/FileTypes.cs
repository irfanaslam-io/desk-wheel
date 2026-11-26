using System.Text.Json;

namespace Wheel.Core.Files;

/// <summary>A group of file types Desk Wheel opens (Pictures, Videos…), from FileTypes.json.</summary>
public sealed record FileTypeGroup(string Id, string Name, string OpensIn, string ProgId, IReadOnlyList<string> Extensions, IReadOnlyList<string> MacTypes)
{
    /// <summary>"JPG, PNG, HEIC, GIF…": the first few extensions, for a label.</summary>
    public string Examples(int count = 5) =>
        string.Join(", ", Extensions.Take(count).Select(e => e.TrimStart('.').ToUpperInvariant())) + (Extensions.Count > count ? "…" : "");
}

/// <summary>
/// The one list of what Desk Wheel opens. The same file makes the Windows and Mac associations
/// (scripts/file-associations.py), so the Default apps screen and the system always agree.
/// </summary>
public static class FileTypes
{
    public static IReadOnlyList<FileTypeGroup> Groups { get; } = Load();

    public static FileTypeGroup? GroupFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return Groups.FirstOrDefault(g => g.Extensions.Contains(ext));
    }

    private static List<FileTypeGroup> Load()
    {
        using var stream = typeof(FileTypes).Assembly.GetManifestResourceStream("Wheel.Core.Files.FileTypes.json")
                           ?? throw new InvalidOperationException("FileTypes.json is missing.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("groups").EnumerateArray().Select(g => new FileTypeGroup(
            g.GetProperty("id").GetString()!, g.GetProperty("name").GetString()!, g.GetProperty("opensIn").GetString()!,
            g.GetProperty("progId").GetString()!,
            g.GetProperty("extensions").EnumerateArray().Select(e => e.GetString()!).ToList(),
            g.GetProperty("mac").EnumerateArray().Select(e => e.GetString()!).ToList())).ToList();
    }
}
