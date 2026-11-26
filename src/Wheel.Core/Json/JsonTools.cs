using System.Buffers;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Wheel.Core.Json;

/// <summary>
/// JSON files: View (a readable, collapsible, searchable page in the browser), Format, Minify and CSV.
/// Comments and trailing commas are accepted (as in VS Code settings files). Key order and numbers are kept exactly.
/// </summary>
public static class JsonTools
{
    /// <summary>Bigger files are refused rather than freezing the computer.</summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    /// <summary>Bigger files open in the viewer as text only (a tree of millions of rows isn't readable anyway).</summary>
    public const long MaxTreeBytes = 25L * 1024 * 1024;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 512,
    };

    /// <summary>Reads and checks a JSON file. Throws <see cref="InvalidDataException"/> with the line and column of a mistake.</summary>
    public static JsonDocument Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes)
            throw new InvalidDataException($"{info.Name} is larger than {MaxBytes / 1024 / 1024} MB.");
        var bytes = File.ReadAllBytes(path);
        return Parse(bytes, info.Name);
    }

    public static JsonDocument Parse(ReadOnlyMemory<byte> utf8, string name = "The file")
    {
        // A UTF-8 byte-order mark is common in files saved by Notepad.
        if (utf8.Span.StartsWith("﻿"u8)) utf8 = utf8[3..];
        try
        {
            return JsonDocument.Parse(utf8, ReadOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" (line {line + 1}, column {(ex.BytePositionInLine ?? 0) + 1})" : "";
            throw new InvalidDataException($"{name} isn't valid JSON{where}: {FirstSentence(ex.Message)}");
        }
    }

    /// <summary>The JSON written out again: indented with two spaces, or on one line.</summary>
    public static string Write(JsonElement element, bool indented)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = indented,
            // Keep Arabic, accents and emoji readable instead of \uXXXX.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false,
        }))
        {
            element.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static JobResult Format(string input) => Rewrite(input, indented: true, "formatted");

    public static JobResult Minify(string input) => Rewrite(input, indented: false, "minified");

    private static JobResult Rewrite(string input, bool indented, string suffix)
    {
        try
        {
            using var doc = Read(input);
            var output = OutputPaths.Beside(input, ".json", suffix);
            File.WriteAllText(output, Write(doc.RootElement, indented) + (indented ? Environment.NewLine : ""), new UTF8Encoding(false));
            return JobResult.Ok(output);
        }
        catch (InvalidDataException ex)
        {
            return JobResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// A list of records → CSV: one row per item, one column per key (in the order keys first appear).
    /// Nested objects and lists go into a cell as JSON. A single object becomes one row.
    /// </summary>
    public static JobResult ToCsv(string input)
    {
        try
        {
            using var doc = Read(input);
            var root = doc.RootElement;
            var rows = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                JsonValueKind.Object when FirstArray(root) is { } inner => inner.EnumerateArray().ToList(),
                JsonValueKind.Object => [root],
                _ => null,
            };
            if (rows is null || rows.Count == 0)
                return JobResult.Fail($"{Path.GetFileName(input)} has no list of records to turn into rows.");

            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var objects = rows.All(r => r.ValueKind == JsonValueKind.Object);
            if (objects)
                foreach (var row in rows)
                    foreach (var p in row.EnumerateObject())
                        if (seen.Add(p.Name)) columns.Add(p.Name);
            if (!objects) columns.Add("value");

            var sb = new StringBuilder();
            sb.AppendJoin(',', columns.Select(Csv)).Append("\r\n");
            foreach (var row in rows)
            {
                if (objects)
                    sb.AppendJoin(',', columns.Select(c => Csv(row.TryGetProperty(c, out var v) ? Cell(v) : "")));
                else
                    sb.Append(Csv(Cell(row)));
                sb.Append("\r\n");
            }
            var output = OutputPaths.Beside(input, ".csv");
            // UTF-8 with a BOM so Excel opens Arabic and accents correctly.
            File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
            return JobResult.Ok(output);
        }
        catch (InvalidDataException ex)
        {
            return JobResult.Fail(ex.Message);
        }
    }

    /// <summary>{ "data": [ … ] } style files: the first property that is a list.</summary>
    private static JsonElement? FirstArray(JsonElement obj)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0)
                return p.Value;
        return null;
    }

    private static string Cell(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.Object or JsonValueKind.Array => Write(v, indented: false),
        _ => v.GetRawText(),
    };

    private static string Csv(string s)
    {
        // A leading = + - @ would run as a formula in Excel: keep it as text.
        if (s.Length > 0 && "=+-@".Contains(s[0]) && !double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            s = "'" + s;
        return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    // ---------------------------------------------------------------- viewer page

    private static string? _template;

    private static string Template()
    {
        if (_template is not null) return _template;
        using var stream = typeof(JsonTools).Assembly.GetManifestResourceStream("Wheel.Core.Json.JsonViewer.html")
            ?? throw new InvalidOperationException("The JSON viewer page is missing from Desk Wheel.");
        using var reader = new StreamReader(stream);
        return _template = reader.ReadToEnd();
    }

    /// <summary>
    /// The viewer page for one file: self-contained (no internet, nothing loaded from elsewhere).
    /// An invalid file still opens, showing where the mistake is and the text around it.
    /// </summary>
    public static string BuildPage(string path)
    {
        var name = Path.GetFileName(path);
        var info = new FileInfo(path);
        string data, error = "";
        if (info.Length > MaxBytes)
        {
            data = "null";
            error = $"{name} is larger than {MaxBytes / 1024 / 1024} MB, too big to show.";
        }
        else
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                using var doc = Parse(bytes, name);
                data = Write(doc.RootElement, indented: false);
            }
            catch (InvalidDataException ex)
            {
                data = "null";
                error = ex.Message + "\n\n" + Encoding.UTF8.GetString(bytes, 0, (int)Math.Min(bytes.Length, 2_000_000));
            }
        }
        return Template()
            .Replace("{{TITLE}}", WebUtility.HtmlEncode(name))
            .Replace("{{SIZE}}", Size(info.Length))
            .Replace("{{TREE}}", info.Length <= MaxTreeBytes ? "true" : "false")
            // Inside <script type="application/json">: "<" can only occur within strings, so escaping it is safe
            // and stops a "</script>" in the data from ending the block.
            .Replace("{{DATA}}", data.Replace("<", "\\u003c"))
            .Replace("{{ERROR}}", JsonSerializer.Serialize(error).Replace("<", "\\u003c"));
    }

    /// <summary>"View": the page written to a temporary file (the same one each time for the same source).</summary>
    public static string WriteViewingCopy(string input, string tempFolder)
    {
        var full = Path.GetFullPath(input);
        Directory.CreateDirectory(tempFolder);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..8].ToLowerInvariant();
        var output = Path.Combine(tempFolder, $"{OutputPaths.StemOf(full)}-{hash}.html");
        File.WriteAllText(output, BuildPage(full));
        return output;
    }

    /// <summary>Saves the viewer page beside the file, to keep or share.</summary>
    public static JobResult SaveHtml(string input)
    {
        var output = OutputPaths.Beside(input, ".html");
        File.WriteAllText(output, BuildPage(input));
        return JobResult.Ok(output);
    }

    private static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} bytes" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024:0.#} MB";

    private static string FirstSentence(string message)
    {
        var cut = message.IndexOf(". ", StringComparison.Ordinal);
        var s = cut > 0 ? message[..cut] : message.TrimEnd('.');
        // "'x' is an invalid start of a value. LineNumber: 3 | BytePositionInLine: 7" → the first part only.
        var meta = s.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return (meta > 0 ? s[..meta] : s).TrimEnd('.') + ".";
    }
}
