using System.Collections.Concurrent;

namespace Wheel.Core.Disk;

/// <summary>A file or folder and how much space it takes, in a tree of the folder that was scanned.</summary>
public sealed class DiskNode
{
    private readonly string? _rootPath;
    private List<DiskNode>? _children;

    private DiskNode(string name, DiskNode? parent, bool isFolder, string? rootPath = null)
    {
        Name = name;
        Parent = parent;
        IsFolder = isFolder;
        _rootPath = rootPath;
    }

    public string Name { get; private set; }
    public DiskNode? Parent { get; private set; }
    public bool IsFolder { get; }
    /// <summary>Bytes: the file's length, or everything inside the folder.</summary>
    public long Size { get; private set; }
    /// <summary>Files inside (a file counts itself).</summary>
    public long FileCount { get; private set; }
    public DateTime Modified { get; private set; }
    /// <summary>Some of it couldn't be read (no permission), so the real size may be bigger.</summary>
    public bool Incomplete { get; private set; }
    /// <summary>Biggest first.</summary>
    public IReadOnlyList<DiskNode> Children => _children ?? (IReadOnlyList<DiskNode>)Array.Empty<DiskNode>();

    public string FullPath => Parent is null ? _rootPath! : Path.Combine(Parent.FullPath, Name);
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    public static DiskNode Root(string path) =>
        new(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } n ? n : path, null, true, path);

    internal static DiskNode File(string name, DiskNode parent, long size, DateTime modified) =>
        new(name, parent, false) { Size = size, FileCount = 1, Modified = modified };

    internal static DiskNode Folder(string name, DiskNode parent) => new(name, parent, true);

    /// <summary>Sets the folder's contents (biggest first) and adds up their sizes.</summary>
    internal void SetChildren(List<DiskNode> children, bool incomplete, DateTime modified)
    {
        children.Sort((a, b) => b.Size.CompareTo(a.Size));
        _children = children;
        Size = children.Sum(c => c.Size);
        FileCount = children.Sum(c => c.FileCount);
        Incomplete = incomplete || children.Any(c => c.Incomplete);
        Modified = modified;
    }

    /// <summary>After a move to the Trash: takes it out of the tree and the sizes above it.</summary>
    public void Remove()
    {
        if (Parent is not { } parent) return;
        parent._children?.Remove(this);
        for (var p = parent; p is not null; p = p.Parent)
        {
            p.Size -= Size;
            p.FileCount -= FileCount;
        }
        Parent = null;
    }

    /// <summary>A new file made in this folder (a ZIP of some of its items): added in its place by size.</summary>
    public DiskNode AddFile(string name, long size, DateTime modified)
    {
        var file = File(name, this, size, modified);
        _children ??= [];
        var at = _children.FindIndex(c => c.Size < size);
        _children.Insert(at < 0 ? _children.Count : at, file);
        for (var p = this; p is not null; p = p.Parent)
        {
            p.Size += size;
            p.FileCount += 1;
        }
        return file;
    }

    /// <summary>After a rename: the new name, same place.</summary>
    public void Rename(string name) => Name = name;

    /// <summary>This node or one of its folders above it is <paramref name="other"/>.</summary>
    public bool IsInside(DiskNode other)
    {
        for (var n = this; n is not null; n = n.Parent)
            if (ReferenceEquals(n, other)) return true;
        return false;
    }

    /// <summary>Folder names, kind of file, or "Folder".</summary>
    public string Kind => IsFolder ? "Folder" : Path.GetExtension(Name) is { Length: > 1 } ext ? ext[1..].ToUpperInvariant() + " file" : "File";

    public override string ToString() => $"{Name} ({DiskScanner.FormatSize(Size)})";
}

/// <summary>Progress while scanning: how many files so far, and the folder being read.</summary>
public readonly record struct ScanProgress(long Files, long Bytes, string Current);

/// <summary>
/// Adds up how much space every file and folder takes, in parallel. Links (symlinks, Windows junctions) are not
/// followed, so nothing is counted twice and loops can't happen. Folders that can't be read are marked, not fatal.
/// </summary>
public static class DiskScanner
{
    /// <summary>On a Mac, "/" also reaches the data volume through /System/Volumes, and other disks through /Volumes.</summary>
    private static readonly string[] SkipOnMacRoot = ["/System/Volumes", "/Volumes", "/dev", "/net", "/home"];
    private static readonly string[] SkipOnLinuxRoot = ["/proc", "/sys", "/dev", "/run", "/mnt", "/media"];

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint, // symlinks and junctions
        ReturnSpecialDirectories = false,
    };

    public static Task<DiskNode> ScanAsync(string folder, IProgress<ScanProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"{folder} isn't a folder.");
            var root = DiskNode.Root(folder);
            var counter = new Counter(progress);
            Scan(root, folder, 0, counter, ct);
            counter.Report(folder, force: true);
            return root;
        }, ct);

    private static void Scan(DiskNode node, string path, int depth, Counter counter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var children = new List<DiskNode>();
        var folders = new List<(DiskNode Node, string Path)>();
        var incomplete = false;
        DateTime modified = default;
        try
        {
            var dir = new DirectoryInfo(path);
            modified = dir.LastWriteTime;
            foreach (var info in dir.EnumerateFileSystemInfos("*", Options))
            {
                if (info is FileInfo f)
                {
                    long length;
                    try { length = f.Length; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { incomplete = true; continue; }
                    children.Add(DiskNode.File(f.Name, node, length, f.LastWriteTime));
                    counter.Add(length);
                }
                else if (info is DirectoryInfo d && !Skipped(d.FullName))
                {
                    var child = DiskNode.Folder(d.Name, node);
                    children.Add(child);
                    folders.Add((child, d.FullName));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            incomplete = true;
        }
        counter.Report(path);

        // The top few levels in parallel (that's where the big, independent folders are), deeper ones in turn.
        if (depth < 3 && folders.Count > 1)
            Parallel.ForEach(folders, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) },
                f => Scan(f.Node, f.Path, depth + 1, counter, ct));
        else
            foreach (var f in folders) Scan(f.Node, f.Path, depth + 1, counter, ct);

        node.SetChildren(children, incomplete, modified);
    }

    private static bool Skipped(string path)
    {
        if (OperatingSystem.IsMacOS()) return SkipOnMacRoot.Contains(path, StringComparer.Ordinal);
        if (OperatingSystem.IsLinux()) return SkipOnLinuxRoot.Contains(path, StringComparer.Ordinal);
        return false;
    }

    /// <summary>"587.13 GB", "6.3 MB", "512 bytes": decimal units, as Finder and File Explorer's drive sizes use.</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        if (bytes < 1000) return bytes == 1 ? "1 byte" : $"{bytes} bytes";
        double v = bytes;
        var u = -1;
        while (v >= 1000 && u < units.Length - 1) { v /= 1000; u++; }
        return v >= 100 ? $"{v:0} {units[u]}" : v >= 10 ? $"{v:0.#} {units[u]}" : $"{v:0.##} {units[u]}";
    }

    /// <summary>
    /// The drive the folder is on, if the folder is that drive's top (C:\, D:\, or "/" on a Mac): its total size
    /// and free space, to show free space in the chart.
    /// </summary>
    public static (long Total, long Free)? DriveSpace(string folder)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                if (string.Equals(Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName), full, StringComparison.OrdinalIgnoreCase)
                    || (full.Length == 0 && drive.RootDirectory.FullName == "/"))
                    return (drive.TotalSize, drive.AvailableFreeSpace);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private sealed class Counter(IProgress<ScanProgress>? progress)
    {
        private long _files, _bytes;
        private long _lastReport;

        public void Add(long bytes)
        {
            Interlocked.Increment(ref _files);
            Interlocked.Add(ref _bytes, bytes);
        }

        public void Report(string current, bool force = false)
        {
            if (progress is null) return;
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastReport);
            if (!force && now - last < 100) return;
            if (!force && Interlocked.CompareExchange(ref _lastReport, now, last) != last) return;
            progress.Report(new ScanProgress(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes), current));
        }
    }
}
