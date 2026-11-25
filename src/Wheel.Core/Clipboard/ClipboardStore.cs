using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wheel.Core.Clipboard;

public enum ClipKind { Text, Image, Files }

/// <summary>One thing that was copied.</summary>
/// <param name="Id">Stable id; images are stored as &lt;Id&gt;.png in the store's folder.</param>
/// <param name="Hash">Identifies identical content, so copying the same thing again moves it to the top.</param>
public sealed record ClipEntry(string Id, DateTimeOffset At, ClipKind Kind, string Hash,
                               string? Text = null, string[]? Files = null, int Width = 0, int Height = 0, bool Pinned = false)
{
    /// <summary>One line for the list: the text's first line, "Image 1280×720", or the file names.</summary>
    public string Title => Kind switch
    {
        ClipKind.Image => $"Image {Width}×{Height}",
        ClipKind.Files when Files is { Length: 1 } f => Path.GetFileName(f[0].TrimEnd('/', '\\')),
        ClipKind.Files => $"{Files?.Length ?? 0} files",
        _ => FirstLine(Text ?? ""),
    };

    private static string FirstLine(string text)
    {
        var line = text.TrimStart().Split('\n')[0].Trim();
        return line.Length > 120 ? line[..119] + "…" : line;
    }
}

/// <summary>What the clipboard held, as read by the platform layer.</summary>
public sealed record ClipboardContent(string? Text, byte[]? Png, string[]? Files, int Width = 0, int Height = 0);

/// <summary>
/// Clipboard history, kept only on this computer: an index file plus one PNG per copied image, in Wheel's
/// settings folder. Newest first; pinned entries stay until unpinned. Holds at most <see cref="MaxEntries"/>
/// unpinned entries, of which at most <see cref="MaxImages"/> images.
/// </summary>
public sealed class ClipboardStore
{
    public const int MaxEntries = 300;
    public const int MaxImages = 60;
    /// <summary>Longer text is kept, but cut to this length (a huge copy shouldn't bloat the history).</summary>
    public const int MaxTextLength = 200_000;
    public const int MaxImageBytes = 25 * 1024 * 1024;

    private readonly string _folder;
    private readonly string _index;
    private readonly object _sync = new();
    private List<ClipEntry> _entries;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ClipboardStore(string folder)
    {
        _folder = folder;
        _index = Path.Combine(folder, "clipboard.json");
        _entries = Read();
    }

    public event Action? Changed;

    public IReadOnlyList<ClipEntry> Entries
    {
        get { lock (_sync) return _entries.ToList(); }
    }

    public string ImagePath(ClipEntry entry) => Path.Combine(_folder, entry.Id + ".png");

    /// <summary>Adds what was copied. Returns the entry, or null when there was nothing worth keeping.</summary>
    public ClipEntry? Add(ClipboardContent content, DateTimeOffset at)
    {
        ClipEntry? entry;
        byte[]? png = null;
        if (content.Files is { Length: > 0 } files)
        {
            entry = new ClipEntry(NewId(), at, ClipKind.Files, Hash("files\n" + string.Join("\n", files)), Files: files);
        }
        else if (content.Png is { Length: > 0 and <= MaxImageBytes } image)
        {
            png = image;
            entry = new ClipEntry(NewId(), at, ClipKind.Image, Hash(image), Width: content.Width, Height: content.Height);
        }
        else if (!string.IsNullOrWhiteSpace(content.Text))
        {
            var text = content.Text.Length > MaxTextLength ? content.Text[..MaxTextLength] : content.Text;
            entry = new ClipEntry(NewId(), at, ClipKind.Text, Hash("text\n" + text), Text: text);
        }
        else
        {
            return null;
        }

        lock (_sync)
        {
            Directory.CreateDirectory(_folder);
            // The same content again: move the existing entry to the top (keeping its pin) instead of duplicating it.
            var existing = _entries.FindIndex(e => e.Hash == entry.Hash);
            if (existing >= 0)
            {
                var old = _entries[existing];
                _entries.RemoveAt(existing);
                entry = old with { At = at };
            }
            else if (png is not null)
            {
                File.WriteAllBytes(ImagePath(entry), png);
            }
            _entries.Insert(0, entry);
            Trim();
            Save();
        }
        Changed?.Invoke();
        return entry;
    }

    /// <summary>Moves an entry to the top (it was just copied again from the history).</summary>
    public void Touch(ClipEntry entry, DateTimeOffset at) => Update(entry, e => e with { At = at }, moveToTop: true);

    public void SetPinned(ClipEntry entry, bool pinned) => Update(entry, e => e with { Pinned = pinned }, moveToTop: false);

    public void Remove(ClipEntry entry)
    {
        lock (_sync)
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            DeleteImage(entry);
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Forgets everything except pinned entries.</summary>
    public void ClearUnpinned()
    {
        lock (_sync)
        {
            foreach (var e in _entries.Where(e => !e.Pinned)) DeleteImage(e);
            _entries = _entries.Where(e => e.Pinned).ToList();
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Pinned first, then newest; filtered by text, file names or "image".</summary>
    public static IEnumerable<ClipEntry> Filter(IEnumerable<ClipEntry> entries, string? query)
    {
        var ordered = entries.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.At);
        if (string.IsNullOrWhiteSpace(query)) return ordered;
        var q = query.Trim();
        return ordered.Where(e =>
            (e.Text?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (e.Files?.Any(f => f.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false) ||
            (e.Kind == ClipKind.Image && "image".Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    private void Update(ClipEntry entry, Func<ClipEntry, ClipEntry> change, bool moveToTop)
    {
        lock (_sync)
        {
            var i = _entries.FindIndex(e => e.Id == entry.Id);
            if (i < 0) return;
            var updated = change(_entries[i]);
            _entries.RemoveAt(i);
            _entries.Insert(moveToTop ? 0 : i, updated);
            Save();
        }
        Changed?.Invoke();
    }

    private void Trim()
    {
        var unpinned = 0;
        var images = 0;
        var keep = new List<ClipEntry>();
        foreach (var e in _entries)
        {
            if (!e.Pinned)
            {
                unpinned++;
                if (e.Kind == ClipKind.Image) images++;
                if (unpinned > MaxEntries || (e.Kind == ClipKind.Image && images > MaxImages))
                {
                    DeleteImage(e);
                    continue;
                }
            }
            keep.Add(e);
        }
        _entries = keep;
    }

    private void DeleteImage(ClipEntry e)
    {
        if (e.Kind != ClipKind.Image) return;
        try { File.Delete(ImagePath(e)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private List<ClipEntry> Read()
    {
        try
        {
            if (!File.Exists(_index)) return new List<ClipEntry>();
            var list = JsonSerializer.Deserialize<List<ClipEntry>>(File.ReadAllText(_index), Json) ?? new List<ClipEntry>();
            // Images whose file is gone (cleaned up by hand) are dropped.
            return list.Where(e => e.Kind != ClipKind.Image || File.Exists(ImagePath(e))).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<ClipEntry>();
        }
    }

    private void Save()
    {
        var temp = _index + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Json));
        File.Move(temp, _index, overwrite: true);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
    private static string Hash(string s) => Hash(Encoding.UTF8.GetBytes(s));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..32];
}
