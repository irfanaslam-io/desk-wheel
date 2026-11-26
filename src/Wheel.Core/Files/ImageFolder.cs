namespace Wheel.Core.Files;

/// <summary>The pictures a viewer steps through: the ones beside a picture, or in a folder, in File Explorer's order.</summary>
public static class ImageFolder
{
    /// <summary>
    /// One picture → every picture in its folder (so Next and Back work), starting at that one.
    /// A folder → its pictures. Several files → just those pictures.
    /// </summary>
    public static (IReadOnlyList<string> Pictures, int Start) For(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1 && File.Exists(paths[0]))
        {
            var full = Path.GetFullPath(paths[0]);
            var all = PicturesIn(Path.GetDirectoryName(full)!);
            var at = all.FindIndex(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            if (at < 0) { all.Insert(0, full); at = 0; } // an unusual extension: still show it
            return (all, at);
        }
        var list = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) list.AddRange(PicturesIn(p));
            else if (File.Exists(p) && IsPicture(p)) list.Add(Path.GetFullPath(p));
        }
        return (list.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), 0);
    }

    public static bool IsPicture(string path) =>
        FileClassifier.Classify(path) == FileKind.Image && !Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal);

    /// <summary>The pictures directly inside a folder (not hidden ones), sorted like File Explorer and Finder.</summary>
    public static List<string> PicturesIn(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => IsPicture(f) && (File.GetAttributes(f) & FileAttributes.Hidden) == 0 && !Path.GetFileName(f).StartsWith('.'))
                .Select(Path.GetFullPath)
                .OrderBy(Path.GetFileName, NaturalComparer.Instance)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>"photo 2" before "photo 10", ignoring case: the order File Explorer and Finder use.</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
                if (i - si != j - sj) return (i - si).CompareTo(j - sj); // "01" after "1"
            }
            else
            {
                var c = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
