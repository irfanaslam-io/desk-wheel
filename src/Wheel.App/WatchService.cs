using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Wheel.Core;
using Wheel.Core.Files;

namespace Wheel.App;

/// <summary>
/// Watched folders: each new file that fits a folder's rule is processed once it has finished arriving
/// (its size stops changing). Results go beside it, as with any other job. Wheel's own outputs are skipped,
/// so nothing loops.
/// </summary>
internal sealed class WatchService : IDisposable
{
    private readonly JobRunner _jobs;
    private readonly Func<Capabilities> _caps;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, WatchRule> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _madeByWheel = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, long> _lastSize = new(StringComparer.Ordinal);

    public WatchService(JobRunner jobs, Func<Capabilities> caps)
    {
        _jobs = jobs;
        _caps = caps;
        _jobs.OutputsWritten += outputs =>
        {
            lock (_madeByWheel)
            {
                foreach (var o in outputs) _madeByWheel.Add(o);
                if (_madeByWheel.Count > 2000) _madeByWheel.Clear();
            }
        };
        _timer.Tick += (_, _) => _ = ProcessReadyAsync();
    }

    /// <summary>Replaces the watched folders. Folders that don't exist are skipped.</summary>
    public void Apply(IEnumerable<WatchRule> rules)
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _pending.Clear();
        foreach (var rule in rules.Where(r => Directory.Exists(r.Folder)))
        {
            try
            {
                var watcher = new FileSystemWatcher(rule.Folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                };
                var r = rule;
                watcher.Created += (_, e) => _pending[e.FullPath] = r;
                watcher.Renamed += (_, e) => _pending[e.FullPath] = r; // downloads often finish with a rename
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Log.Write(ex);
            }
        }
        if (_watchers.Count > 0) _timer.Start(); else _timer.Stop();
    }

    private async Task ProcessReadyAsync()
    {
        foreach (var (path, rule) in _pending.ToArray())
        {
            if (!File.Exists(path))
            {
                _pending.TryRemove(path, out _);
                _lastSize.Remove(path);
                continue;
            }
            // Still arriving? Wait until the size is the same on two checks in a row.
            long size;
            try { size = new FileInfo(path).Length; }
            catch (IOException) { continue; }
            if (!_lastSize.TryGetValue(path, out var before) || before != size || size == 0)
            {
                _lastSize[path] = size;
                continue;
            }
            _pending.TryRemove(path, out _);
            _lastSize.Remove(path);

            var option = OptionRegistry.All.FirstOrDefault(o => o.Id == rule.OptionId);
            if (option is null) continue;
            bool mine;
            lock (_madeByWheel) mine = _madeByWheel.Contains(path);
            var made = mine ? new HashSet<string> { path } : new HashSet<string>();
            if (!WatchDecider.ShouldProcess(path, option, _caps(), made)) continue;
            Log.Trace($"Watched folder: {option.Id} for {path}");
            await _jobs.RunAsync(option, [path]);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
