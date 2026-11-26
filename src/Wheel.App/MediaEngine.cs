using System;
using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Wheel.Core.Packs;

namespace Wheel.App;

/// <summary>
/// The media player's engine (LibVLC), from the optional download. Loaded once, on first use; until it's
/// downloaded the player offers to get it (or to open the file in the system's own player).
/// </summary>
internal static class MediaEngine
{
    private static LibVLC? _vlc;
    private static string? _problem;

    /// <summary>Where downloads live: beside Desk Wheel's settings, in the local (not roaming) app data on Windows.</summary>
    public static readonly PackManager Packs = new(
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWheel", "Packs")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "DeskWheel", "Packs")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWheel", "Packs"));

    public static PackInfo Pack => KnownPacks.MediaPlayer;

    /// <summary>The download exists for this computer.</summary>
    public static bool CanDownload => Pack.ForThisComputer is not null;

    /// <summary>Ready to play: downloaded (or, when developing on Linux, the system's own LibVLC).</summary>
    public static bool IsReady => _vlc is not null || Packs.IsInstalled(Pack) || (OperatingSystem.IsLinux() && SystemLibVlc() is not null);

    public static bool IsLoaded => _vlc is not null;

    /// <summary>The engine, loaded on first call. Null (with <paramref name="problem"/>) if it isn't there or won't load.</summary>
    public static LibVLC? Get(out string? problem)
    {
        problem = _problem;
        if (_vlc is not null || _problem is not null) return _vlc;
        string? folder = null;
        if (Packs.IsInstalled(Pack)) folder = Packs.FolderFor(Pack);
        else if (OperatingSystem.IsLinux()) folder = SystemLibVlc();
        if (folder is null)
        {
            problem = "The media player isn't downloaded yet.";
            return null; // not remembered: it can be downloaded now
        }
        return LoadFrom(folder, out problem);
    }

    /// <summary>Loads LibVLC from a folder (a pack's, or the self-test's own download). Once per run.</summary>
    public static LibVLC? LoadFrom(string folder, out string? problem)
    {
        problem = _problem;
        if (_vlc is not null || _problem is not null) return _vlc;
        try
        {
            var plugins = Path.Combine(folder, "plugins");
            if (Directory.Exists(plugins))
            {
                // LibVLC reads this from the process environment, which on Mac and Linux .NET doesn't change.
                if (OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", plugins);
                else setenv("VLC_PLUGIN_PATH", plugins, 1);
            }
            LibVLCSharp.Shared.Core.Initialize(OperatingSystem.IsLinux() && folder.StartsWith("/usr/", StringComparison.Ordinal) ? null : folder);
            // Core options only: an option of a plugin that isn't there would stop LibVLC from starting.
            _vlc = new LibVLC(enableDebugLogs: false, "--no-video-title-show", "--no-snapshot-preview", "--no-stats", "--quiet", "--sub-autodetect-file");
            Log.Trace($"Media player engine {_vlc.Version} loaded from {folder}");
            return _vlc;
        }
        catch (Exception ex) when (ex is VLCException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException or IOException)
        {
            Log.Write(ex);
            _problem = problem = "The media player engine didn't start: " + ex.Message + " Remove it in Settings and download it again.";
            return null;
        }
    }

    /// <summary>
    /// For the self-test: plays a file without a window for up to <paramref name="seconds"/> and counts the video
    /// frames LibVLC hands over (drawn into memory, as the player does).
    /// </summary>
    public static (int Frames, long Length, string State) Probe(LibVLC vlc, string path, double seconds = 3)
    {
        using var player = new MediaPlayer(vlc) { Volume = 0 };
        var frames = 0;
        var buffer = GC.AllocateUninitializedArray<byte>(320 * 240 * 4, pinned: true);
        MediaPlayer.LibVLCVideoLockCb lockCb = (_, planes) => { Marshal.WriteIntPtr(planes, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0)); return IntPtr.Zero; };
        MediaPlayer.LibVLCVideoDisplayCb displayCb = (_, _) => System.Threading.Interlocked.Increment(ref frames);
        player.SetVideoFormat("RV32", 320, 240, 320 * 4);
        player.SetVideoCallbacks(lockCb, null, displayCb);
        using var media = new Media(vlc, new Uri(path));
        player.Play(media);
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end && frames < 10 && player.State is not (VLCState.Error or VLCState.Ended)) System.Threading.Thread.Sleep(50);
        var result = (frames, player.Length, player.State.ToString());
        player.Stop();
        GC.KeepAlive(lockCb);
        GC.KeepAlive(displayCb);
        return result;
    }

    /// <summary>Downloaded and loaded this session: removing it needs a restart (the files are in use).</summary>
    public static bool InUse => _vlc is not null;

    private static string? SystemLibVlc()
    {
        foreach (var dir in new[] { "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib" })
            if (File.Exists(Path.Combine(dir, "libvlc.so.5"))) return dir;
        return null;
    }

    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern int setenv(string name, string value, int overwrite);
}
