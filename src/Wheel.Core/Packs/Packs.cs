using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Wheel.Core.Packs;

/// <summary>One platform's download of a pack: its address, exact size and SHA-256.</summary>
public sealed record PackFile(string Url, long Size, string Sha256);

/// <summary>
/// An optional part of Desk Wheel that is downloaded only when someone first needs it (the media player engine),
/// so the app itself stays small. Each file is pinned by size and SHA-256: anything else is thrown away unused.
/// </summary>
public sealed record PackInfo(string Id, string Name, string Version, string Purpose, IReadOnlyDictionary<string, PackFile> Files)
{
    /// <summary>The download for this computer (null where the pack isn't available).</summary>
    public PackFile? ForThisComputer => Files.GetValueOrDefault(PackManager.CurrentPlatform);
}

/// <summary>Progress of a download: bytes so far and in all.</summary>
public readonly record struct PackProgress(long Done, long Total, bool Unpacking = false);

public static class KnownPacks
{
    private const string Base = "https://github.com/irfanaslam-io/desk-wheel/releases/download/packs-media-player-3.0.24-1/";

    /// <summary>LibVLC 3.0.24 (LGPL-2.1-or-later), built by scripts/make-vlc-packs.py and published by CI.</summary>
    public static readonly PackInfo MediaPlayer = new(
        "media-player", "Media player engine", "3.0.24-1",
        "Plays every kind of video and music: MKV, MP4, AVI, WebM, FLAC and more, with subtitles",
        new Dictionary<string, PackFile>
        {
            ["win-x64"] = new(Base + "DeskWheel-media-player-3.0.24-1-win-x64.zip", 47_620_926,
                "e2719c13c17722fad910231b2dd9a3309bde38567a1cd208b049f87d23537811"),
            ["win-arm64"] = new(Base + "DeskWheel-media-player-3.0.24-1-win-arm64.zip", 43_159_786,
                "dea2d05783b851382ea01cab7eff8bd3c258aeb8a77bcbcb7c66eaec8b2630f7"),
            ["mac-arm64"] = new(Base + "DeskWheel-media-player-3.0.24-1-mac-arm64.zip", 31_173_269,
                "c3a765af8dfd307d857c42f825900390919b84724b2a8d394f7ea0c767dfc114"),
        });

    public static IReadOnlyList<PackInfo> All { get; } = [MediaPlayer];
}

/// <summary>
/// Downloads, checks, unpacks and removes packs. Each pack lives in its own folder under <see cref="Root"/>;
/// a folder only exists once its download was complete and correct (it's unpacked beside it, then renamed).
/// </summary>
public sealed class PackManager
{
    private readonly HttpClient _http;

    public PackManager(string root, HttpClient? http = null)
    {
        Root = root;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public string Root { get; }

    /// <summary>"win-x64", "win-arm64", "mac-arm64", "mac-x64", "linux-x64"…</summary>
    public static string CurrentPlatform =>
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "mac" : "linux") + "-" +
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public string FolderFor(PackInfo pack) => Path.Combine(Root, $"{pack.Id}-{pack.Version}");

    public bool IsInstalled(PackInfo pack) => File.Exists(Path.Combine(FolderFor(pack), ".complete"));

    /// <summary>Space the pack takes on this computer (0 if it isn't there).</summary>
    public long SizeOnDisk(PackInfo pack)
    {
        var folder = FolderFor(pack);
        return Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;
    }

    /// <summary>Downloads, checks and unpacks the pack for this computer (or <paramref name="file"/>).</summary>
    /// <exception cref="IOException">The download failed, or wasn't the expected file (nothing is kept).</exception>
    public async Task InstallAsync(PackInfo pack, IProgress<PackProgress>? progress = null, CancellationToken ct = default, PackFile? file = null)
    {
        file ??= pack.ForThisComputer ?? throw new PlatformNotSupportedException($"{pack.Name} isn't available for this computer ({CurrentPlatform}).");
        if (file.Size <= 0 || file.Sha256.Length != 64) throw new InvalidOperationException($"{pack.Name} has no pinned checksum yet.");
        Directory.CreateDirectory(Root);
        var id = Guid.NewGuid().ToString("N");
        var download = Path.Combine(Root, $".{pack.Id}-{id}.zip");
        var unpack = Path.Combine(Root, $".{pack.Id}-{id}");
        try
        {
            await DownloadAsync(file, download, progress, ct);
            progress?.Report(new PackProgress(file.Size, file.Size, Unpacking: true));
            await Task.Run(() => Unpack(download, unpack, ct), ct);
            File.WriteAllText(Path.Combine(unpack, ".complete"), $"{pack.Id} {pack.Version} {file.Sha256}\n");

            var target = FolderFor(pack);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(unpack, target);
            RemoveOtherVersions(pack);
        }
        finally
        {
            TryDelete(download);
            if (Directory.Exists(unpack)) try { Directory.Delete(unpack, recursive: true); } catch (IOException) { }
        }
    }

    private async Task DownloadAsync(PackFile file, string path, IProgress<PackProgress>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"The download failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            long done = 0, lastReport = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                done += read;
                if (done > file.Size) throw new IOException("The download is bigger than expected, so it was thrown away.");
                sha.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                if (done - lastReport >= 256 * 1024 || done == file.Size)
                {
                    lastReport = done;
                    progress?.Report(new PackProgress(done, file.Size));
                }
            }
            if (done != file.Size) throw new IOException("The download stopped before the end. Try again.");
        }
        var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The download isn't the expected file (its checksum doesn't match), so it was thrown away.");
    }

    /// <summary>Unpacks into a new folder; any entry that would land outside it stops everything.</summary>
    private static void Unpack(string zip, string folder, CancellationToken ct)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw new IOException("The download contains a file that would go outside its folder, so it was thrown away.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
            // Keep the executable bit for libraries on Mac and Linux.
            if (!OperatingSystem.IsWindows() && (entry.ExternalAttributes >> 16 & 0x40) != 0)
                File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>Removes the pack. Fails (IOException) if it's in use; it's then removed the next time Desk Wheel starts.</summary>
    public void Remove(PackInfo pack)
    {
        var folder = FolderFor(pack);
        if (!Directory.Exists(folder)) return;
        // Take away the "complete" mark first, so a half-removed pack is never used.
        TryDelete(Path.Combine(folder, ".complete"));
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>At start: clears interrupted downloads, packs marked for removal and old versions.</summary>
    public void Tidy()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(dir);
            var known = KnownPacks.All.Any(p => name == $"{p.Id}-{p.Version}");
            if (name.StartsWith('.') || !known || !File.Exists(Path.Combine(dir, ".complete")))
                try { Directory.Delete(dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var file in Directory.EnumerateFiles(Root, ".*.zip")) TryDelete(file);
    }

    private void RemoveOtherVersions(PackInfo pack)
    {
        foreach (var dir in Directory.EnumerateDirectories(Root, pack.Id + "-*"))
            if (!string.Equals(dir, FolderFor(pack), StringComparison.Ordinal))
                try { Directory.Delete(dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
