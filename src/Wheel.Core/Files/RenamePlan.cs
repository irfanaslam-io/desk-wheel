using System.Globalization;

namespace Wheel.Core.Files;

public enum NameCase { Keep, Lower, Upper, Title }

/// <summary>
/// How to rename a set of files. <see cref="Pattern"/> builds the new name from:
/// {name} the current name (without extension), {n} a running number, {date} the file's date (yyyy-MM-dd),
/// {folder} the folder's name. Find/replace and the case change apply after. The extension is kept.
/// </summary>
public sealed record RenameRule(string Pattern = "{name}", string Find = "", string Replace = "", NameCase Case = NameCase.Keep,
                                int Start = 1, int Digits = 0, bool MatchCase = false);

/// <summary>One file in a rename: its new path, or why it can't be renamed.</summary>
public sealed record RenameItem(string From, string To, string? Problem)
{
    public bool Changes => Problem is null && !string.Equals(From, To, StringComparison.Ordinal);
}

/// <summary>
/// Works out the new names first (shown as a preview), then renames. Never overwrites: a name that's
/// taken, repeated, empty or not allowed is reported and that file is left alone. Can be undone.
/// </summary>
public static class RenamePlan
{
    private static readonly char[] Invalid = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>Files in name order ("2" before "10"), so numbers follow what the folder shows.</summary>
    public static IReadOnlyList<RenameItem> Build(IReadOnlyList<string> paths, RenameRule rule, Func<string, DateTime>? dateOf = null)
    {
        dateOf ??= p => File.GetLastWriteTime(p);
        var ordered = paths.OrderBy(p => Path.GetFileName(p), NaturalStringComparer.Instance).ToList();
        var digits = rule.Digits > 0 ? rule.Digits : (rule.Start + ordered.Count - 1).ToString(CultureInfo.InvariantCulture).Length;
        var items = new List<RenameItem>();
        var taken = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ordered.Count; i++)
        {
            var from = ordered[i].TrimEnd('/', '\\');
            var folder = Path.GetDirectoryName(from) ?? "";
            var isFolder = Directory.Exists(from);
            var ext = isFolder ? "" : OutputPaths.FullExtension(from);
            var stem = isFolder ? Path.GetFileName(from) : OutputPaths.StemOf(from);

            var name = rule.Pattern
                .Replace("{name}", stem)
                .Replace("{n}", (rule.Start + i).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0'))
                .Replace("{date}", dateOf(from).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Replace("{folder}", Path.GetFileName(folder));
            if (rule.Find.Length > 0)
                name = name.Replace(rule.Find, rule.Replace, rule.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            name = rule.Case switch
            {
                NameCase.Lower => name.ToLowerInvariant(),
                NameCase.Upper => name.ToUpperInvariant(),
                NameCase.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()),
                _ => name,
            };
            name = name.Trim();

            var to = Path.Combine(folder, name + ext);
            string? problem = null;
            if (name.Length == 0) problem = "The new name is empty";
            else if (name.IndexOfAny(Invalid) >= 0) problem = "Names can't contain / \\ : * ? \" < > |";
            else if (name.StartsWith('.')) problem = "The name would hide the file";
            else if (!taken.Add(to)) problem = "Another file would get the same name";
            else if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && (File.Exists(to) || Directory.Exists(to)))
                problem = "A file with that name already exists";
            items.Add(new RenameItem(from, problem is null ? to : from, problem));
        }
        return items;
    }

    /// <summary>Renames what can be renamed. Returns the renames done (for undo) and any failures.</summary>
    public static (IReadOnlyList<(string From, string To)> Done, IReadOnlyList<string> Failed) Apply(IReadOnlyList<RenameItem> items)
    {
        var done = new List<(string, string)>();
        var failed = new List<string>();
        foreach (var item in items.Where(i => i.Changes))
        {
            try
            {
                Move(item.From, item.To);
                done.Add((item.From, item.To));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{Path.GetFileName(item.From)}: {ex.Message}");
            }
        }
        return (done, failed);
    }

    /// <summary>Puts names back, newest rename first. Files renamed again since then are left alone.</summary>
    public static int Undo(IReadOnlyList<(string From, string To)> done)
    {
        var restored = 0;
        foreach (var (from, to) in done.Reverse())
        {
            if (!(File.Exists(to) || Directory.Exists(to)) || File.Exists(from) || Directory.Exists(from)) continue;
            try { Move(to, from); restored++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return restored;
    }

    private static void Move(string from, string to)
    {
        // A change of case only ("photo" → "Photo") needs a step through a temporary name on Mac and Windows.
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && !string.Equals(from, to, StringComparison.Ordinal))
        {
            var temp = from + ".wheel-rename";
            MoveOne(from, temp);
            MoveOne(temp, to);
            return;
        }
        MoveOne(from, to);
    }

    private static void MoveOne(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to, overwrite: false);
    }
}

/// <summary>"photo 2" before "photo 10".</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        if (a is null || b is null) return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
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
                var c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
