using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Wheel.Core.Telemetry;

/// <summary>
/// Anonymous usage counts, sent to Google Analytics 4 (Measurement Protocol) only when the person said yes.
///
/// What can be sent is deliberately narrow: an event name ("tool_used") and a few short codes such as
/// "img.to-png", "mac", "0.18.0" or a count. Anything else is dropped before it's stored, so file names,
/// folder paths, clipboard text or anything a person typed can never leave the computer, even by mistake.
/// The id is a random number made on this computer, not tied to the person or the machine.
///
/// Events wait in a small file and go out in batches; offline is fine, they're sent next time (or dropped
/// once there are too many). Turning statistics off deletes anything not yet sent.
/// </summary>
public sealed partial class UsageStats
{
    public const int MaxQueued = 500;
    public const int BatchSize = 25; // Measurement Protocol limit per request

    private readonly string _queueFile;
    private readonly string _clientId;
    private readonly string _measurementId;
    private readonly string _apiSecret;
    private readonly Func<bool> _allowed;
    private readonly IReadOnlyDictionary<string, object> _common;
    private readonly long _sessionId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly Lock _lock = new();

    /// <param name="folder">The app's data folder (the queue and the random id live there).</param>
    /// <param name="allowed">Asked before every event: the person's current choice.</param>
    /// <param name="common">Sent with every event, e.g. app_version, os, arch (codes only).</param>
    public UsageStats(string folder, string measurementId, string apiSecret, Func<bool> allowed, IReadOnlyDictionary<string, object> common)
    {
        Directory.CreateDirectory(folder);
        _queueFile = Path.Combine(folder, "usage-queue.jsonl");
        _clientId = ClientId(Path.Combine(folder, "usage-id"));
        _measurementId = measurementId;
        _apiSecret = apiSecret;
        _allowed = allowed;
        _common = Clean(common);
    }

    /// <summary>True when a measurement id and API secret were built in (otherwise nothing is ever sent).</summary>
    public static bool Configured(string? measurementId, string? apiSecret) =>
        !string.IsNullOrWhiteSpace(measurementId) && !string.IsNullOrWhiteSpace(apiSecret);

    /// <summary>Records an event (if allowed). Never throws.</summary>
    public void Track(string name, IReadOnlyDictionary<string, object>? parameters = null)
    {
        if (!_allowed() || !SafeName().IsMatch(name)) return;
        var p = new JsonObject();
        foreach (var (k, v) in _common) p[k] = JsonValue.Create(v);
        foreach (var (k, v) in Clean(parameters ?? new Dictionary<string, object>())) p[k] = JsonValue.Create(v);
        p["session_id"] = _sessionId;
        p["engagement_time_msec"] = 1;
        var line = new JsonObject { ["name"] = name, ["params"] = p, ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 }.ToJsonString();
        try
        {
            lock (_lock)
            {
                var lines = File.Exists(_queueFile) ? File.ReadAllLines(_queueFile).ToList() : new List<string>();
                lines.Add(line);
                if (lines.Count > MaxQueued) lines = lines[^MaxQueued..];
                File.WriteAllLines(_queueFile, lines);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Sends what's waiting. <paramref name="post"/>(url, json) returns true when accepted.</summary>
    public async Task FlushAsync(Func<string, string, Task<bool>> post)
    {
        if (!_allowed()) { Forget(); return; }
        List<string> lines;
        lock (_lock)
        {
            try { lines = File.Exists(_queueFile) ? File.ReadAllLines(_queueFile).Where(l => l.Length > 0).ToList() : new(); }
            catch (IOException) { return; }
        }
        var sent = 0;
        foreach (var batch in lines.Chunk(BatchSize))
        {
            bool ok;
            try { ok = await post(Endpoint(), Payload(batch)); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { ok = false; }
            if (!ok) break;
            sent += batch.Length;
        }
        if (sent == 0) return;
        lock (_lock)
        {
            try
            {
                var now = File.Exists(_queueFile) ? File.ReadAllLines(_queueFile).Where(l => l.Length > 0).ToList() : new();
                File.WriteAllLines(_queueFile, now.Skip(Math.Min(sent, now.Count)));
            }
            catch (IOException) { }
        }
    }

    /// <summary>Deletes anything not yet sent (when statistics are turned off).</summary>
    public void Forget()
    {
        lock (_lock)
        {
            try { File.Delete(_queueFile); } catch (IOException) { }
        }
    }

    public int Waiting()
    {
        lock (_lock)
        {
            try { return File.Exists(_queueFile) ? File.ReadAllLines(_queueFile).Count(l => l.Length > 0) : 0; }
            catch (IOException) { return 0; }
        }
    }

    public string Endpoint() =>
        $"https://www.google-analytics.com/mp/collect?measurement_id={Uri.EscapeDataString(_measurementId)}&api_secret={Uri.EscapeDataString(_apiSecret)}";

    /// <summary>The request body for a batch of queued lines.</summary>
    public string Payload(IEnumerable<string> lines)
    {
        var events = new JsonArray();
        long? first = null;
        foreach (var line in lines)
        {
            if (JsonNode.Parse(line) is not JsonObject e) continue;
            first ??= e["ts"]?.GetValue<long>();
            e.Remove("ts");
            events.Add(e);
        }
        var body = new JsonObject { ["client_id"] = _clientId, ["non_personalized_ads"] = true, ["events"] = events };
        if (first is { } ts) body["timestamp_micros"] = ts;
        return body.ToJsonString();
    }

    /// <summary>
    /// Keeps only safe parameters: short lowercase codes (letters, digits, . _ -) and whole numbers.
    /// Anything that could be a name, a path or text is dropped.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Clean(IReadOnlyDictionary<string, object> parameters)
    {
        var safe = new Dictionary<string, object>();
        foreach (var (key, value) in parameters)
        {
            if (!SafeName().IsMatch(key)) continue;
            switch (value)
            {
                case int or long or short or byte:
                    safe[key] = Convert.ToInt64(value);
                    break;
                case bool b:
                    safe[key] = b ? 1L : 0L;
                    break;
                case string s when SafeCode().IsMatch(s):
                    safe[key] = s;
                    break;
            }
        }
        return safe;
    }

    /// <summary>A rough size for a count, so exact numbers aren't needed: "1", "2-5", "6-20", "21+".</summary>
    public static string Bucket(int n) => n <= 1 ? "1" : n <= 5 ? "2-5" : n <= 20 ? "6-20" : "21+";

    private static string ClientId(string file)
    {
        try
        {
            if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 10 } existing) return existing;
            var id = $"{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            File.WriteAllText(file, id);
            return id;
        }
        catch (IOException) { return Guid.NewGuid().ToString("N"); }
        catch (UnauthorizedAccessException) { return Guid.NewGuid().ToString("N"); }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex SafeName();

    [GeneratedRegex("^[a-z0-9][a-z0-9._+-]{0,39}$")]
    private static partial Regex SafeCode();
}
