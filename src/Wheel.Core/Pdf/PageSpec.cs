namespace Wheel.Core.Pdf;

/// <summary>
/// Pages typed by a person: "1-3, 7, 10-end", "end-1" (all pages, backwards), "odd", "even", "last".
/// Order is kept, so "3, 1, 2" reorders. Page numbers start at 1; the result is 0-based indexes.
/// </summary>
public static class PageSpec
{
    /// <summary>The page indexes (0-based) in the order typed, or an explanation of what's wrong.</summary>
    public static (IReadOnlyList<int>? Pages, string? Problem) Parse(string text, int pageCount)
    {
        if (pageCount < 1) return (null, "This PDF has no pages.");
        var pages = new List<int>();
        var parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (null, "Type pages like 1-3, 7 or 10-end.");
        foreach (var raw in parts)
        {
            var part = raw.ToLowerInvariant();
            switch (part)
            {
                case "all":
                    pages.AddRange(Enumerable.Range(0, pageCount));
                    continue;
                case "odd":
                    pages.AddRange(Enumerable.Range(0, pageCount).Where(i => i % 2 == 0));
                    continue;
                case "even":
                    pages.AddRange(Enumerable.Range(0, pageCount).Where(i => i % 2 == 1));
                    continue;
                case "reverse":
                    pages.AddRange(Enumerable.Range(0, pageCount).Reverse());
                    continue;
            }
            var dash = part.IndexOfAny(['-', '–', '—']);
            if (dash < 0)
            {
                if (Number(part, pageCount) is not { } single) return (null, Explain(raw, pageCount));
                pages.Add(single - 1);
                continue;
            }
            var (from, to) = (Number(part[..dash], pageCount), Number(part[(dash + 1)..], pageCount));
            if (from is null || to is null) return (null, Explain(raw, pageCount));
            if (from <= to)
                for (var p = from.Value; p <= to.Value; p++) pages.Add(p - 1);
            else
                for (var p = from.Value; p >= to.Value; p--) pages.Add(p - 1); // "5-1": backwards
        }
        return (pages, null);
    }

    /// <summary>The pages left after removing <paramref name="remove"/>, in their original order.</summary>
    public static IReadOnlyList<int> Without(IEnumerable<int> remove, int pageCount)
    {
        var gone = remove.ToHashSet();
        return Enumerable.Range(0, pageCount).Where(i => !gone.Contains(i)).ToList();
    }

    /// <summary>A short label for file names: "1-3, 7".</summary>
    public static string Label(string text)
    {
        var cleaned = string.Join(", ", text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var bad in Path.GetInvalidFileNameChars()) cleaned = cleaned.Replace(bad, ' ');
        return cleaned.Length > 30 ? cleaned[..30].TrimEnd() + "…" : cleaned;
    }

    private static int? Number(string text, int pageCount)
    {
        text = text.Trim();
        if (text is "end" or "last" or "") return text == "" ? null : pageCount;
        return int.TryParse(text, out var n) && n >= 1 && n <= pageCount ? n : null;
    }

    private static string Explain(string part, int pageCount) =>
        $"\"{part}\" isn't a page of this PDF: it has {pageCount} page{(pageCount == 1 ? "" : "s")}. Try 1-3, 7 or 10-end.";
}
