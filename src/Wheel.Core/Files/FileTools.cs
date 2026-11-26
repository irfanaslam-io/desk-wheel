using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Wheel.Core.Files;

/// <summary>Small everyday file tools: checksums, word counts, joining text files, CSV → JSON, cleaning Office files.</summary>
public static class FileTools
{
    // ---------------------------------------------------------------- checksums

    public sealed record Checksum(string Path, string Sha256, string Md5, long Size);

    public static Checksum ChecksumOf(string path)
    {
        using var sha = SHA256.Create();
        using var md5 = MD5.Create();
        using (var stream = File.OpenRead(path))
        {
            var buffer = new byte[1 << 20];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, n, null, 0);
                md5.TransformBlock(buffer, 0, n, null, 0);
            }
            sha.TransformFinalBlock([], 0, 0);
            md5.TransformFinalBlock([], 0, 0);
        }
        return new Checksum(path, Convert.ToHexStringLower(sha.Hash!), Convert.ToHexStringLower(md5.Hash!), new FileInfo(path).Length);
    }

    /// <summary>
    /// The text copied for checksums. One file: its SHA-256 and MD5. Several: one "hash  name" line each
    /// (the format sha256sum and Get-FileHash comparisons use), also saved as checksums.sha256 beside them.
    /// </summary>
    public static (string Text, string? SavedFile) Checksums(IReadOnlyList<string> paths)
    {
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories) : [p]).ToList();
        if (files.Count == 1)
        {
            var c = ChecksumOf(files[0]);
            return ($"{Path.GetFileName(files[0])}\nSHA-256: {c.Sha256}\nMD5: {c.Md5}", null);
        }
        var root = OutputPaths.FolderFor(Path.GetFullPath(paths[0]));
        var lines = files.Select(f => $"{ChecksumOf(f).Sha256}  {Path.GetRelativePath(root, f).Replace('\\', '/')}").ToList();
        var text = string.Join("\n", lines) + "\n";
        var saved = OutputPaths.Unique(Path.Combine(root, "checksums.sha256"));
        File.WriteAllText(saved, text, new UTF8Encoding(false));
        return (text, saved);
    }

    // ---------------------------------------------------------------- words

    public sealed record TextCount(int Words, int Characters, int CharactersNoSpaces, int Lines, int Paragraphs)
    {
        /// <summary>At about 230 words a minute.</summary>
        public int MinutesToRead => Math.Max(1, (int)Math.Round(Words / 230.0));

        public override string ToString() =>
            $"{Words:N0} words · {Characters:N0} characters ({CharactersNoSpaces:N0} without spaces) · {Lines:N0} lines · {Paragraphs:N0} paragraphs · about {MinutesToRead} min to read";
    }

    public static bool CanCount(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".markdown" or ".csv" or ".log" or ".docx" or ".odt" or ".html" or ".htm" or ".srt" or ".vtt" or ".json";

    /// <summary>Counts the words in a plain-text, Markdown, Word (.docx) or OpenDocument (.odt) file.</summary>
    public static TextCount Count(string path) => CountText(TextOf(path));

    public static TextCount CountText(string text)
    {
        var words = 0;
        var inWord = false;
        foreach (var c in text)
        {
            var letter = char.IsLetterOrDigit(c) || c is '\'' or '’' or '-' or '_';
            if (letter && !inWord) words++;
            inWord = letter;
        }
        var lines = text.Length == 0 ? 0 : text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0);
        var paragraphs = text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Count(p => p.Trim().Length > 0);
        var chars = text.Replace("\r", "").Replace("\n", "").Length;
        var noSpaces = text.Count(c => !char.IsWhiteSpace(c));
        return new TextCount(words, chars, noSpaces, lines, paragraphs);
    }

    /// <summary>The text of a file: plain text as it is; Word and OpenDocument documents read from their XML.</summary>
    public static string TextOf(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".docx":
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("This Word file has no text part.");
                using var stream = entry.Open();
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var doc = XDocument.Load(stream);
                return string.Join("\n\n", doc.Descendants(w + "p").Select(p =>
                    string.Concat(p.Descendants().Select(e => e.Name == w + "t" ? e.Value : e.Name == w + "tab" ? "\t" : e.Name == w + "br" ? "\n" : ""))));
            }
            case ".odt":
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException("This document has no text part.");
                using var stream = entry.Open();
                XNamespace t = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
                var doc = XDocument.Load(stream);
                return string.Join("\n\n", doc.Descendants().Where(e => e.Name == t + "p" || e.Name == t + "h").Select(e => e.Value));
            }
            case ".html" or ".htm":
            {
                var html = File.ReadAllText(path);
                html = System.Text.RegularExpressions.Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "));
            }
            default:
                return File.ReadAllText(path);
        }
    }

    // ---------------------------------------------------------------- join

    public static bool CanJoin(IReadOnlyList<string> paths)
    {
        var exts = paths.Select(p => Path.GetExtension(p).ToLowerInvariant()).Distinct().ToList();
        return paths.Count >= 2 && exts.Count == 1 && exts[0] is ".txt" or ".md" or ".csv" or ".log";
    }

    /// <summary>
    /// Several text files as one, in name order: "Joined.txt" beside the first. Text files are separated by a blank
    /// line; CSV files keep the first file's header row once (the others' header rows are dropped when identical).
    /// </summary>
    public static JobResult Join(IReadOnlyList<string> paths)
    {
        if (!CanJoin(paths))
            return JobResult.Fail("Select two or more files of the same kind: TXT, MD, CSV or LOG.");
        var ordered = paths.OrderBy(Path.GetFileName, NaturalComparer.Instance).ToList();
        var ext = Path.GetExtension(ordered[0]).ToLowerInvariant();
        var output = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(ordered[0]), "Joined" + ext));
        var sb = new StringBuilder();
        if (ext == ".csv")
        {
            string? header = null;
            foreach (var path in ordered)
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length == 0) continue;
                var start = 0;
                if (header is null) header = lines[0];
                else if (lines[0].Trim() == header.Trim()) start = 1;
                foreach (var line in lines.Skip(start).Where(l => l.Length > 0)) sb.Append(line).Append("\r\n");
            }
        }
        else
        {
            foreach (var path in ordered)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(File.ReadAllText(path).TrimEnd('\r', '\n'));
            }
            sb.Append('\n');
        }
        File.WriteAllText(output, sb.ToString(), new UTF8Encoding(ext == ".csv"));
        return JobResult.Ok(output);
    }

    // ---------------------------------------------------------------- CSV → JSON

    /// <summary>
    /// "name.json": one object per row, keyed by the header row. Numbers and true/false become JSON numbers and
    /// booleans; everything else stays text. The delimiter (comma, semicolon or tab) is detected.
    /// </summary>
    public static JobResult CsvToJson(string path)
    {
        var text = File.ReadAllText(path);
        var rows = ParseCsv(text);
        if (rows.Count < 2) return JobResult.Fail($"{Path.GetFileName(path)} needs a header row and at least one row of data.");
        var header = rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"column{i + 1}" : h.Trim()).ToList();
        var output = OutputPaths.Beside(path, ".json");
        using (var stream = File.Create(output))
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartArray();
            foreach (var row in rows.Skip(1).Where(r => r.Any(c => c.Length > 0)))
            {
                json.WriteStartObject();
                for (var i = 0; i < header.Count; i++)
                {
                    var value = i < row.Count ? row[i] : "";
                    json.WritePropertyName(header[i]);
                    if (value is "true" or "false") json.WriteBooleanValue(value == "true");
                    else if (LooksNumeric(value)) json.WriteRawValue(value);
                    else json.WriteStringValue(value);
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        return JobResult.Ok(output);
    }

    /// <summary>A plain number JSON can hold exactly as written (no leading zeros like 0050, no thousands separators).</summary>
    private static bool LooksNumeric(string s) =>
        s.Length is > 0 and < 20 && System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$");

    /// <summary>RFC 4180 CSV: quoted fields, doubled quotes, line breaks inside quotes; comma, semicolon or tab.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        if (text.StartsWith('﻿')) text = text[1..];
        var firstLine = text.Split('\n')[0];
        var delimiter = new[] { ',', ';', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    // ---------------------------------------------------------------- Office clean

    public static bool CanCleanOffice(string path) => Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx";

    /// <summary>
    /// "name (clean).docx": the same document without who wrote and last saved it, the company, the template,
    /// editing time, custom properties and the document thumbnail. Content is untouched.
    /// </summary>
    public static JobResult CleanOffice(string path)
    {
        if (!CanCleanOffice(path)) return JobResult.Fail("Clean works on Word, Excel and PowerPoint files (.docx, .xlsx, .pptx).");
        var output = OutputPaths.Beside(path, Path.GetExtension(path), "clean");
        File.Copy(path, output);
        try
        {
            using var zip = ZipFile.Open(output, ZipArchiveMode.Update);
            XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XNamespace ext = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
            Rewrite(zip, "docProps/core.xml", doc =>
            {
                foreach (var name in new[] { dc + "creator", cp + "lastModifiedBy", cp + "lastPrinted", cp + "keywords", dc + "description", cp + "category", dc + "subject", cp + "contentStatus" })
                    doc.Descendants(name).Remove();
            });
            Rewrite(zip, "docProps/app.xml", doc =>
            {
                foreach (var name in new[] { "Company", "Manager", "Template", "TotalTime", "HyperlinkBase" })
                    doc.Descendants(ext + name).Remove();
            });
            foreach (var part in new[] { "docProps/custom.xml", "docProps/thumbnail.jpeg", "docProps/thumbnail.wmf", "docProps/thumbnail.emf" })
                zip.GetEntry(part)?.Delete();
            // The package's list of parts must not point at removed ones.
            Rewrite(zip, "_rels/.rels", doc =>
                doc.Descendants().Where(e => e.Name.LocalName == "Relationship" && ((string?)e.Attribute("Target") ?? "") is var target
                    && (target.Contains("custom.xml") || target.Contains("thumbnail"))).Remove());
            Rewrite(zip, "[Content_Types].xml", doc =>
                doc.Descendants().Where(e => e.Name.LocalName == "Override" && ((string?)e.Attribute("PartName") ?? "") is var part
                    && (part.Contains("custom.xml") || part.Contains("thumbnail"))).Remove());
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            try { File.Delete(output); } catch (IOException) { }
            return JobResult.Fail($"Couldn't clean {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static void Rewrite(ZipArchive zip, string part, Action<XDocument> change)
    {
        var entry = zip.GetEntry(part);
        if (entry is null) return;
        XDocument doc;
        using (var read = entry.Open()) doc = XDocument.Load(read);
        change(doc);
        entry.Delete();
        var replaced = zip.CreateEntry(part, CompressionLevel.Optimal);
        using var write = replaced.Open();
        doc.Save(write, SaveOptions.DisableFormatting);
    }
}
