using System.Formats.Tar;
using System.IO.Compression;

namespace Wheel.Core.Converters;

/// <summary>
/// ZIP, TAR, TAR.GZ and GZ using only the .NET base library.
/// Extraction relies on .NET's built-in path-traversal ("zip slip") protection.
/// </summary>
public static class ArchiveConverter
{
    public static JobResult Run(WheelOption option, IReadOnlyList<string> inputs) => option.Id switch
    {
        "any.zip" => ZipSelection(inputs),
        "arc.extract" => Each(inputs, Extract),
        "arc.to-zip" => Each(inputs, i => Repack(i, ".zip")),
        "arc.to-targz" => Each(inputs, i => Repack(i, ".tar.gz")),
        _ => JobResult.Fail($"Unknown archive option {option.Id}"),
    };

    private static JobResult Each(IReadOnlyList<string> inputs, Func<string, string> work)
    {
        var outputs = new List<string>();
        try
        {
            foreach (var input in inputs)
                outputs.Add(work(input));
            return new JobResult(true, outputs);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new JobResult(false, outputs, ex.Message);
        }
    }

    /// <summary>One ZIP of every dragged file and folder. Named after the item if there is one, else "Archive".</summary>
    public static JobResult ZipSelection(IReadOnlyList<string> inputs)
    {
        try
        {
            var first = Path.GetFullPath(inputs[0]).TrimEnd(Path.DirectorySeparatorChar);
            var dir = OutputPaths.FolderFor(first);
            var name = inputs.Count == 1 ? OutputPaths.StemOf(first) : "Archive";
            var output = OutputPaths.Unique(Path.Combine(dir, name + ".zip"));

            using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                foreach (var input in inputs.Select(Path.GetFullPath))
                {
                    if (Directory.Exists(input))
                    {
                        var root = Path.GetFileName(input.TrimEnd(Path.DirectorySeparatorChar));
                        foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
                        {
                            var rel = Path.Combine(root, Path.GetRelativePath(input, file)).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
                        }
                    }
                    else
                    {
                        zip.CreateEntryFromFile(input, Path.GetFileName(input), CompressionLevel.Optimal);
                    }
                }
            }
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return JobResult.Fail(ex.Message);
        }
    }

    /// <summary>Extracts into a new folder beside the archive. Returns the folder (or file, for plain .gz).</summary>
    public static string Extract(string archive)
    {
        var ext = OutputPaths.FullExtension(archive).ToLowerInvariant();

        if (ext == ".gz" && !IsTarGz(archive))
        {
            // Plain gzip of a single file: "notes.txt.gz" → "notes.txt"
            var target = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(archive),
                                                         Path.GetFileNameWithoutExtension(archive)));
            using var src = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
            using var dst = File.Create(target);
            src.CopyTo(dst);
            return target;
        }

        var folder = OutputPaths.FolderBeside(archive);
        Directory.CreateDirectory(folder);
        ExtractTo(archive, folder);
        return folder;
    }

    /// <summary>Converts between ZIP and TAR.GZ by extracting to a temp folder and packing again.</summary>
    public static string Repack(string archive, string targetExt)
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ExtractTo(archive, temp);
            var output = OutputPaths.Beside(archive, targetExt);

            if (targetExt == ".zip")
            {
                ZipFile.CreateFromDirectory(temp, output, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            else
            {
                using var fs = File.Create(output);
                using var gz = new GZipStream(fs, CompressionLevel.Optimal);
                TarFile.CreateFromDirectory(temp, gz, includeBaseDirectory: false);
            }
            return output;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private static void ExtractTo(string archive, string folder)
    {
        var ext = OutputPaths.FullExtension(archive).ToLowerInvariant();
        switch (ext)
        {
            case ".zip":
                ZipFile.ExtractToDirectory(archive, folder, overwriteFiles: false);
                break;
            case ".tar":
                TarFile.ExtractToDirectory(archive, folder, overwriteFiles: false);
                break;
            case ".tar.gz" or ".tgz" or ".gz":
                using (var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress))
                    TarFile.ExtractToDirectory(gz, folder, overwriteFiles: false);
                break;
            default:
                throw new InvalidDataException($"Can't open {ext} archives yet.");
        }
    }

    /// <summary>A .gz holds a tar if its name says so or its first block has the "ustar" magic at offset 257.</summary>
    private static bool IsTarGz(string path)
    {
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            var header = new byte[512];
            var read = 0;
            while (read < header.Length)
            {
                var n = gz.Read(header, read, header.Length - read);
                if (n == 0) break;
                read += n;
            }
            return read >= 262 && System.Text.Encoding.ASCII.GetString(header, 257, 5) == "ustar";
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
