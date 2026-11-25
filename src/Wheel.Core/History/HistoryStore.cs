using System.Text.Json;

namespace Wheel.Core.History;

/// <summary>One thing Wheel made: a screenshot, a converted file, an edited copy.</summary>
/// <param name="Kind">"Screenshot", "Converted", "Edited".</param>
/// <param name="Source">The file it was made from, if any.</param>
/// <param name="Action">What was done, for example "Convert to WebP".</param>
public sealed record HistoryEntry(DateTimeOffset At, string Kind, string Path, string? Source = null, string? Action = null)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('/', '\\'));
}

/// <summary>
/// The list behind the History window: newest first, kept in a small JSON-lines file in Wheel's settings
/// folder. Only paths and descriptions are stored, never file contents. Holds the most recent
/// <see cref="Limit"/> entries.
/// </summary>
public sealed class HistoryStore
{
    public const int Limit = 500;

    private readonly string _file;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public HistoryStore(string file) => _file = file;

    public event Action? Changed;

    public void Add(HistoryEntry entry)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_file)!);
            File.AppendAllText(_file, JsonSerializer.Serialize(entry, Json) + "\n");
            // Rewrite now and then so the file stays small.
            if (new FileInfo(_file).Length > 256 * 1024)
                Write(ReadAll().Take(Limit).Reverse());
        }
        Changed?.Invoke();
    }

    public void AddAll(IEnumerable<HistoryEntry> entries)
    {
        foreach (var e in entries) Add(e);
    }

    /// <summary>Newest first, at most <see cref="Limit"/>. Unreadable lines are skipped.</summary>
    public IReadOnlyList<HistoryEntry> Load()
    {
        lock (_sync)
            return ReadAll().Take(Limit).ToList();
    }

    /// <summary>Forgets entries (the files themselves are not touched).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            if (File.Exists(_file)) File.Delete(_file);
        }
        Changed?.Invoke();
    }

    public void Remove(HistoryEntry entry)
    {
        lock (_sync)
            Write(ReadAll().Where(e => e != entry).Take(Limit).Reverse());
        Changed?.Invoke();
    }

    /// <summary>Entries whose name, folder, source or action contain the text (case-insensitive).</summary>
    public static IEnumerable<HistoryEntry> Filter(IEnumerable<HistoryEntry> entries, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return entries;
        var q = query.Trim();
        return entries.Where(e =>
            e.Path.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (e.Source?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (e.Action?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            e.Kind.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<HistoryEntry> ReadAll()
    {
        if (!File.Exists(_file)) return Enumerable.Empty<HistoryEntry>();
        var list = new List<HistoryEntry>();
        foreach (var line in File.ReadAllLines(_file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize<HistoryEntry>(line, Json) is { Path.Length: > 0 } e)
                    list.Add(e);
            }
            catch (JsonException)
            {
                // A line cut short by a crash: skip it.
            }
        }
        list.Reverse(); // the file is oldest first
        return list;
    }

    /// <summary>Rewrites the file from entries given oldest first.</summary>
    private void Write(IEnumerable<HistoryEntry> oldestFirst)
    {
        var temp = _file + ".tmp";
        File.WriteAllLines(temp, oldestFirst.Select(e => JsonSerializer.Serialize(e, Json)));
        File.Move(temp, _file, overwrite: true);
    }
}
