using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Wheel.Core.Converters;

/// <summary>The archive needs a password (none given, or the one given is wrong).</summary>
public sealed class ArchivePasswordException(string message, bool wrong) : IOException(message)
{
    /// <summary>A password was given but didn't open it.</summary>
    public bool Wrong { get; } = wrong;
}

/// <summary>
/// Archives. Opens ZIP (also AES-encrypted), RAR (4 and 5), 7z, TAR, TAR.GZ / .BZ2 / .XZ / .LZ, GZ, BZ2, XZ and ISO
/// disc images; makes ZIP (optionally with an AES-256 password) and TAR.GZ. Everything runs inside Desk Wheel:
/// SharpCompress (MIT) reads most formats, DiscUtils (MIT) reads ISO, SharpZipLib (MIT) writes password ZIPs,
/// and .NET writes plain ZIP and TAR.GZ. RAR files can be opened but not made (RAR creation is WinRAR's own).
///
/// Extraction never trusts the paths inside an archive: every entry must land inside the target folder, links
/// are skipped, and nothing existing is overwritten.
/// </summary>
/// <summary>A file inside an archive. <see cref="Size"/> is -1 when unknown (a plain .gz).</summary>
public sealed record ArchiveItem(string Path, long Size, DateTime? Modified, bool Encrypted)
{
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

public static class ArchiveConverter
{
    public static JobResult Run(WheelOption option, IReadOnlyList<string> inputs, string? password = null)
    {
        try
        {
            return option.Id switch
            {
                "any.zip" => ZipSelection(inputs),
                "arc.extract" => Each(inputs, i => Extract(i, password)),
                "arc.to-zip" => Each(inputs, i => Repack(i, ".zip", password)),
                "arc.to-targz" => Each(inputs, i => Repack(i, ".tar.gz", password)),
                _ => JobResult.Fail($"Unknown archive option {option.Id}"),
            };
        }
        catch (ArchivePasswordException ex)
        {
            return JobResult.Fail(ex.Message);
        }
    }

    /// <summary>True when the archive needs a password to open (asked before extracting).</summary>
    public static bool NeedsPassword(string archive)
    {
        try
        {
            if (IsIso(archive) || IsSingleCompressedFile(archive)) return false;
            using var opened = ArchiveFactory.OpenArchive(archive, new ReaderOptions());
            return opened.Entries.Any(e => e.IsEncrypted);
        }
        catch (Exception ex) when (IsPasswordProblem(ex))
        {
            return true; // 7z with encrypted file names can't even be listed without the password
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or NotSupportedException
                                   or SharpCompress.Common.SharpCompressException or ArgumentException)
        {
            return false; // not readable this way: the real extraction reports the problem
        }
    }

    private static JobResult Each(IReadOnlyList<string> inputs, Func<string, string> work)
    {
        var outputs = new List<string>();
        try
        {
            foreach (var input in inputs)
                outputs.Add(work(input));
            return new JobResult(true, outputs);
        }
        catch (ArchivePasswordException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
                                   or NotSupportedException or SharpCompress.Common.SharpCompressException or ArgumentException)
        {
            return new JobResult(false, outputs, Friendly(ex));
        }
    }

    /// <summary>One ZIP of every dragged file and folder. Named after the item if there is one, else "Archive".</summary>
    public static JobResult ZipSelection(IReadOnlyList<string> inputs)
    {
        try
        {
            var output = SelectionZipPath(inputs);
            using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
                foreach (var (file, name) in FilesIn(inputs))
                    zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return JobResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// "name.zip" with a password: every file encrypted with AES-256 (WinZip AES). Opens in Desk Wheel, 7-Zip and
    /// WinRAR; File Explorer's ZIP folders and macOS Archive Utility can't open AES-encrypted ZIPs.
    /// </summary>
    public static JobResult ZipWithPassword(IReadOnlyList<string> inputs, string password)
    {
        string? output = null;
        try
        {
            output = SelectionZipPath(inputs, " (protected)");
            using (var fs = File.Create(output))
            using (var zip = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(fs) { Password = password })
            {
                zip.SetLevel(6);
                foreach (var (file, name) in FilesIn(inputs))
                {
                    var info = new FileInfo(file);
                    zip.PutNextEntry(new ICSharpCode.SharpZipLib.Zip.ZipEntry(name)
                    {
                        DateTime = info.LastWriteTime,
                        Size = info.Length,
                        AESKeySize = 256,
                    });
                    using (var src = File.OpenRead(file)) src.CopyTo(zip);
                    zip.CloseEntry();
                }
            }
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ICSharpCode.SharpZipLib.SharpZipBaseException)
        {
            if (output is not null) try { File.Delete(output); } catch (IOException) { }
            return JobResult.Fail(ex.Message);
        }
    }

    private static string SelectionZipPath(IReadOnlyList<string> inputs, string suffix = "")
    {
        var first = Path.GetFullPath(inputs[0]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = inputs.Count == 1 ? OutputPaths.StemOf(first) : "Archive";
        return OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(first), name + suffix + ".zip"));
    }

    /// <summary>Every file in the selection with its name inside the ZIP (folders keep their structure).</summary>
    private static IEnumerable<(string File, string Name)> FilesIn(IReadOnlyList<string> inputs)
    {
        foreach (var input in inputs.Select(Path.GetFullPath))
        {
            if (Directory.Exists(input))
            {
                var root = Path.GetFileName(input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
                    yield return (file, Path.Combine(root, Path.GetRelativePath(input, file)).Replace('\\', '/'));
            }
            else
            {
                yield return (input, Path.GetFileName(input));
            }
        }
    }

    /// <summary>Extracts into a new folder beside the archive. Returns the folder (or the file, for a plain .gz/.bz2/.xz).</summary>
    public static string Extract(string archive, string? password = null)
    {
        if (IsSingleCompressedFile(archive))
        {
            // One compressed file: "notes.txt.gz" → "notes.txt"
            var target = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(archive), Path.GetFileNameWithoutExtension(archive)));
            try
            {
                using var src = Decompressor(archive);
                using var dst = File.Create(target);
                src.CopyTo(dst);
            }
            catch
            {
                try { File.Delete(target); } catch (IOException) { }
                throw;
            }
            return target;
        }

        var folder = OutputPaths.FolderBeside(archive);
        Directory.CreateDirectory(folder);
        try
        {
            ExtractTo(archive, folder, password);
        }
        catch
        {
            // Don't leave a half-filled folder behind (a wrong password, a damaged or harmful archive).
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            throw;
        }
        return folder;
    }

    /// <summary>
    /// Extracts only the chosen files, and everything inside the chosen folders (entry paths as <see cref="List"/>
    /// gives them), into <paramref name="folder"/>, keeping their folders. Returns the files written.
    /// </summary>
    public static IReadOnlyList<string> ExtractSome(string archive, IReadOnlyCollection<string> entries, string folder, string? password = null)
    {
        var wanted = entries.Select(e => e.Replace('\\', '/').Trim('/')).Where(e => e.Length > 0).ToHashSet(StringComparer.Ordinal);
        bool Include(string name) => wanted.Contains(name) || wanted.Any(w => name.StartsWith(w + "/", StringComparison.Ordinal));
        Directory.CreateDirectory(folder);
        var before = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToHashSet();
        if (IsSingleCompressedFile(archive))
        {
            var target = OutputPaths.Unique(Path.Combine(folder, Path.GetFileNameWithoutExtension(archive)));
            using (var src = Decompressor(archive))
            using (var dst = File.Create(target))
                src.CopyTo(dst);
        }
        else
        {
            ExtractTo(archive, folder, password, Include);
        }
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => !before.Contains(f)).ToList();
    }

    /// <summary>
    /// What's inside: every file with its path ("folder/file.txt"), size and date, without extracting anything.
    /// </summary>
    /// <exception cref="ArchivePasswordException">The names themselves are protected (some 7z and RAR files).</exception>
    public static List<ArchiveItem> List(string archive, string? password = null)
    {
        var items = new List<ArchiveItem>();
        if (IsSingleCompressedFile(archive))
        {
            items.Add(new ArchiveItem(Path.GetFileNameWithoutExtension(archive), -1, File.GetLastWriteTime(archive), false));
            return items;
        }
        if (IsIso(archive))
        {
            using var stream = File.OpenRead(archive);
            using var reader = new DiscUtils.Iso9660.CDReader(stream, joliet: true);
            foreach (var file in reader.GetFiles("", "*.*", SearchOption.AllDirectories))
            {
                var name = file.Replace('\\', '/').TrimStart('/');
                var semicolon = name.LastIndexOf(';');
                if (semicolon > 0) name = name[..semicolon];
                items.Add(new ArchiveItem(name, reader.GetFileLength(file), reader.GetLastWriteTime(file), false));
            }
            return items;
        }
        if (CompressedTarStream(archive) is { } tarStream)
        {
            using (tarStream)
            using (var reader = new TarReader(tarStream))
                while (reader.GetNextEntry() is { } entry)
                    if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile)
                        items.Add(new ArchiveItem(EntryName(entry.Name), entry.Length, entry.ModificationTime.LocalDateTime, false));
            return items;
        }
        try
        {
            using var opened = ArchiveFactory.OpenArchive(archive, new ReaderOptions { Password = string.IsNullOrEmpty(password) ? null : password, LookForHeader = true });
            foreach (var entry in opened.Entries)
                if (!entry.IsDirectory && string.IsNullOrEmpty(entry.LinkTarget) && !string.IsNullOrEmpty(entry.Key))
                    items.Add(new ArchiveItem(EntryName(entry.Key!), entry.Size, entry.LastModifiedTime, entry.IsEncrypted));
        }
        catch (Exception ex) when (IsPasswordProblem(ex) || (password is not null && ex is SharpCompress.Common.SharpCompressException or InvalidDataException))
        {
            throw new ArchivePasswordException(
                password is null ? $"{Path.GetFileName(archive)} is protected with a password, even its file names." : "The password is wrong.",
                wrong: password is not null);
        }
        return items;
    }

    private static string EntryName(string name) => name.Replace('\\', '/').TrimStart('/');

    [ThreadStatic] private static bool IsPasswordRun;

    /// <summary>Converts between formats by extracting to a temporary folder and packing again (ZIP or TAR.GZ).</summary>
    public static string Repack(string archive, string targetExt, string? password = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            if (IsSingleCompressedFile(archive))
            {
                using var src = Decompressor(archive);
                using var dst = File.Create(Path.Combine(temp, Path.GetFileNameWithoutExtension(archive)));
                src.CopyTo(dst);
            }
            else
            {
                ExtractTo(archive, temp, password);
            }
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

    // ---------------------------------------------------------------- reading

    private static void ExtractTo(string archive, string folder, string? password, Func<string, bool>? include = null)
    {
        if (IsIso(archive))
        {
            ExtractIso(archive, folder, include);
            return;
        }
        var root = Path.GetFullPath(folder);
        if (CompressedTarStream(archive) is { } tarStream)
        {
            using (tarStream) ExtractTar(tarStream, root, include);
            return;
        }

        if (!string.IsNullOrEmpty(password) && Path.GetExtension(archive).ToLowerInvariant() is ".zip" or ".cbz" or ".jar")
        {
            // Password ZIPs (AES and the older ZipCrypto) are read with SharpZipLib, which handles every common
            // layout; SharpCompress rejects valid AES ZIPs that use data descriptors.
            ExtractProtectedZip(archive, root, password, include);
            return;
        }

        var options = new ReaderOptions { Password = string.IsNullOrEmpty(password) ? null : password, LookForHeader = true };
        IsPasswordRun = password is not null;
        try
        {
            using var opened = ArchiveFactory.OpenArchive(archive, options);
            if (password is null && opened.Entries.Any(e => e.IsEncrypted))
                throw new ArchivePasswordException($"{Path.GetFileName(archive)} is protected with a password.", wrong: false);

            if (opened.IsSolid || opened.Type == SharpCompress.Common.ArchiveType.SevenZip)
            {
                // Solid archives decompress in one pass, start to end.
                using var reader = opened.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    if (Target(root, reader.Entry, include) is not { } target) continue;
                    using (var output = File.Create(target)) reader.WriteEntryTo(output);
                    Stamp(target, reader.Entry.LastModifiedTime);
                }
            }
            else
            {
                foreach (var entry in opened.Entries)
                {
                    if (Target(root, entry, include) is not { } target) continue;
                    using (var input = entry.OpenEntryStream())
                    using (var output = File.Create(target))
                        input.CopyTo(output);
                    Stamp(target, entry.LastModifiedTime);
                }
            }
        }
        catch (ArchivePasswordException)
        {
            throw;
        }
        catch (Exception ex) when (IsPasswordProblem(ex) || (password is not null && ex is SharpCompress.Common.SharpCompressException or InvalidDataException))
        {
            throw new ArchivePasswordException(
                password is null ? $"{Path.GetFileName(archive)} is protected with a password." : "The password is wrong.",
                wrong: password is not null);
        }
        finally
        {
            IsPasswordRun = false;
        }
    }

    private static void ExtractProtectedZip(string archive, string root, string password, Func<string, bool>? include = null)
    {
        try
        {
            using var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(archive) { Password = password };
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry entry in zip)
            {
                if (!entry.IsFile || (include is not null && !include(EntryName(entry.Name)))) continue;
                var target = SafeTarget(root, entry.Name) ?? throw Unsafe(entry.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target)) target = OutputPaths.Unique(target);
                using (var input = zip.GetInputStream(entry))
                using (var output = File.Create(target))
                    input.CopyTo(output);
                Stamp(target, entry.DateTime);
            }
        }
        catch (ICSharpCode.SharpZipLib.Zip.ZipException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                                                                   || ex.Message.Contains("AES", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArchivePasswordException("The password is wrong.", wrong: true);
        }
        catch (ICSharpCode.SharpZipLib.SharpZipBaseException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    /// <summary>Where an entry goes, folders made; null for folders, links and anything that would escape the root.</summary>
    private static string? Target(string root, SharpCompress.Common.IEntry entry, Func<string, bool>? include = null)
    {
        if (entry.IsDirectory || !string.IsNullOrEmpty(entry.LinkTarget) || string.IsNullOrEmpty(entry.Key)) return null;
        if (include is not null && !include(EntryName(entry.Key!))) return null;
        var target = SafeTarget(root, entry.Key!) ?? throw Unsafe(entry.Key!);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return File.Exists(target) ? OutputPaths.Unique(target) : target;
    }

    private static void Stamp(string path, DateTime? modified)
    {
        if (modified is { Year: > 1980 } when)
            try { File.SetLastWriteTime(path, when); } catch (Exception ex) when (ex is IOException or ArgumentException) { }
    }

    /// <summary>The TAR inside a .tar.bz2 / .tar.xz / .tar.lz (and .tbz, .txz…), decompressed; null for other files.</summary>
    private static Stream? CompressedTarStream(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        string? kind = name.EndsWith(".tar.bz2") || name.EndsWith(".tbz") || name.EndsWith(".tbz2") ? ".bz2"
            : name.EndsWith(".tar.xz") || name.EndsWith(".txz") ? ".xz"
            : name.EndsWith(".tar.lz") ? ".lz"
            : name.EndsWith(".tar.gz") || name.EndsWith(".tgz") ? ".gz" : null;
        // "backup.gz" can hold a TAR too: its first block says so.
        if (kind is null && Path.GetExtension(name) is ".gz" or ".bz2" or ".xz" or ".lz" && !IsSingleCompressedFile(path))
            kind = Path.GetExtension(name);
        if (kind is null) return null;
        var file = File.OpenRead(path);
        try
        {
            return kind switch
            {
                ".bz2" => SharpCompress.Compressors.BZip2.BZip2Stream.Create(file, SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true),
                ".xz" => new SharpCompress.Compressors.Xz.XZStream(file),
                ".gz" => new GZipStream(file, CompressionMode.Decompress),
                _ => SharpCompress.Compressors.LZMA.LZipStream.Create(file, SharpCompress.Compressors.CompressionMode.Decompress),
            };
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>A TAR stream, entry by entry, through the same path checks (links and devices skipped).</summary>
    private static void ExtractTar(Stream stream, string root, Func<string, bool>? include = null)
    {
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile)) continue;
            if (include is not null && !include(EntryName(entry.Name))) continue;
            var target = SafeTarget(root, entry.Name) ?? throw Unsafe(entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) target = OutputPaths.Unique(target);
            using (var output = File.Create(target))
                entry.DataStream?.CopyTo(output);
            Stamp(target, entry.ModificationTime.LocalDateTime);
        }
    }

    /// <summary>An archive that tries to write outside its folder is refused as a whole (it was made to attack).</summary>
    private static InvalidDataException Unsafe(string entry) =>
        new($"This archive tries to put a file outside its folder (\"{entry}\"), which is how harmful archives attack, so nothing was extracted.");

    /// <summary>The full path for an entry, or null when it would land outside <paramref name="root"/>.</summary>
    public static string? SafeTarget(string root, string entryName)
    {
        var name = entryName.Replace('\\', '/').TrimStart('/');
        if (name.Length == 0 || name.Contains(':') || name.Split('/').Any(part => part == "..")) return null;
        var full = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? full : null;
    }

    private static void ExtractIso(string iso, string folder, Func<string, bool>? include = null)
    {
        using var stream = File.OpenRead(iso);
        using var reader = new DiscUtils.Iso9660.CDReader(stream, joliet: true);
        var root = Path.GetFullPath(folder);
        foreach (var file in reader.GetFiles("", "*.*", SearchOption.AllDirectories))
        {
            var name = file.Replace('\\', '/').TrimStart('/');
            var semicolon = name.LastIndexOf(';'); // ISO 9660 version numbers: "README.TXT;1"
            if (semicolon > 0) name = name[..semicolon];
            if (include is not null && !include(name)) continue;
            var target = SafeTarget(root, name) ?? throw Unsafe(name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = reader.OpenFile(file, FileMode.Open, FileAccess.Read);
            using var dst = File.Create(File.Exists(target) ? OutputPaths.Unique(target) : target);
            src.CopyTo(dst);
        }
    }

    private static bool IsIso(string path) => Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase);

    /// <summary>.gz, .bz2, .xz or .lz holding one file (not a TAR).</summary>
    private static bool IsSingleCompressedFile(string path)
    {
        var ext = OutputPaths.FullExtension(path).ToLowerInvariant();
        if (ext is not (".gz" or ".bz2" or ".xz" or ".lz")) return false;
        try
        {
            using var stream = Decompressor(path);
            var header = new byte[512];
            var read = 0;
            while (read < header.Length)
            {
                var n = stream.Read(header, read, header.Length - read);
                if (n == 0) break;
                read += n;
            }
            return !(read >= 262 && System.Text.Encoding.ASCII.GetString(header, 257, 5) == "ustar");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or SharpCompress.Common.SharpCompressException)
        {
            return false;
        }
    }

    private static Stream Decompressor(string path)
    {
        var file = File.OpenRead(path);
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".gz" => new GZipStream(file, CompressionMode.Decompress),
                ".bz2" => SharpCompress.Compressors.BZip2.BZip2Stream.Create(file, SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true),
                ".xz" => new SharpCompress.Compressors.Xz.XZStream(file),
                ".lz" => SharpCompress.Compressors.LZMA.LZipStream.Create(file, SharpCompress.Compressors.CompressionMode.Decompress),
                _ => throw new InvalidDataException("Not a compressed file."),
            };
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static bool IsPasswordProblem(Exception ex) =>
        ex is SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException
        || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase);

    private static string Friendly(Exception ex) => ex switch
    {
        SharpCompress.Common.ArchiveException or InvalidOperationException when ex.Message.Contains("multi", StringComparison.OrdinalIgnoreCase)
            => "This is one part of a multi-part archive. Put all the parts in the same folder and open the first one.",
        SharpCompress.Common.SharpCompressException or InvalidOperationException or NotSupportedException
            => "This archive couldn't be read. It may be damaged, incomplete, or a type Desk Wheel can't open.",
        _ => ex.Message,
    };
}
