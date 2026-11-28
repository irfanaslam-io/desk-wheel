using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Wheel.Core.Updates;

namespace Wheel.App;

/// <summary>
/// Checks for a newer Desk Wheel and installs it over the current one: nothing to uninstall, settings kept.
/// Only a feed signed with Desk Wheel's own key is trusted, and each download must match the checksum in it.
/// Windows: runs the new installer silently; it closes Desk Wheel, upgrades it in place and starts it again.
/// Mac: copies the new app out of the DMG, then a small script swaps it in after Desk Wheel quits and opens it.
/// When the app can't replace itself (no signing key built in, a read-only Applications folder), the download
/// is shown instead, so the person can install it the usual way.
/// </summary>
internal sealed class Updater
{
    private static readonly HttpClient Http = CreateHttp();
    private readonly AppSettings _settings;
    private readonly Action<string, string, bool, Action?> _notify;
    private readonly Action _quit;
    private int _busy;
    private bool _checkedThisLaunch;
    private string? _announced;

    public UpdateCheck? Latest { get; private set; }
    public event Action? Changed;

    /// <summary>The version waiting to be installed, or null when Desk Wheel is up to date.</summary>
    public string? Waiting => Latest is { Available: true } u ? u.Info!.Version : null;

    /// <summary>While an update downloads: how far it is (0 to 1). Null otherwise.</summary>
    public double? Progress { get; private set; }

    public bool Installing => Volatile.Read(ref _busy) == 1;

    public Updater(AppSettings settings, Action<string, string, bool, Action?> notify, Action quit)
    {
        _settings = settings;
        _notify = notify;
        _quit = quit;
    }

    /// <summary>Off in the Microsoft Store version: the Store installs updates itself.</summary>
    public static bool Available => BuildConfig.UpdatesAvailable && !PackageInfo.IsPackaged;

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DeskWheel/{BuildConfig.Version}");
        return http;
    }

    /// <summary>
    /// Automatic check: once each time Desk Wheel starts, then every 6 hours, only when turned on. A new version
    /// is announced once (a card); after that it stays visible on the button, the Screenshot wheel and the tray.
    /// </summary>
    public async Task CheckInBackgroundAsync()
    {
        if (!Available || !_settings.AutoUpdate || Installing) return;
        if (_checkedThisLaunch && _settings.LastUpdateCheck is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(6)) return;
        _checkedThisLaunch = true;
        var result = await CheckAsync();
        if (result is not { Available: true } || result.Info!.Version == _settings.SkippedVersion || _announced == result.Info.Version) return;
        _announced = result.Info.Version;
        _notify($"Desk Wheel {result.Info.Version} is available",
                result.CanInstall ? "Click to update now. It takes a few seconds and keeps your settings." : "Click to download it.",
                false, () => _ = InstallAsync());
    }

    public async Task<UpdateCheck> CheckAsync()
    {
        var result = await UpdateChecker.CheckAsync(Http, BuildConfig.UpdateFeed!, BuildConfig.Version, BuildConfig.UpdatePublicKey);
        Log.Trace(result.Available ? $"Update available: {result.Info!.Version} (installable: {result.CanInstall})" : $"Update check: {result.Problem ?? "up to date"}");
        _settings.LastUpdateCheck = DateTime.UtcNow;
        _settings.Save();
        Latest = result;
        Changed?.Invoke();
        return result;
    }

    /// <summary>Downloads, verifies and installs the update found by the last check.</summary>
    public async Task InstallAsync()
    {
        if (Latest is not { Available: true } check) return;
        if (!check.CanInstall)
        {
            Links.Open(check.Asset?.Url ?? check.Info!.Page ?? Brand_Website);
            return;
        }
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            _notify("Updating Desk Wheel", $"Downloading version {check.Info!.Version}…", false, null);
            var folder = Path.Combine(Path.GetTempPath(), "DeskWheel-update-" + check.Info.Version);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, Path.GetFileName(new Uri(check.Asset!.Url).LocalPath));
            Progress = 0;
            Changed?.Invoke();
            await DownloadAsync(check.Asset.Url, file, p => { Progress = p; Changed?.Invoke(); });
            if (!UpdateFeed.Sha256Matches(file, check.Asset.Sha256))
            {
                File.Delete(file);
                _notify("Update stopped", "The download didn't match the expected file, so it wasn't installed. Please try again later.", true, null);
                return;
            }
            Usage.Track("update_started", new() { ["to_version"] = check.Info.Version });
            await Usage.FlushAsync();

            if (OperatingSystem.IsWindows()) InstallWindows(file, folder);
            else if (OperatingSystem.IsMacOS()) await InstallMacAsync(file, folder);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Write(ex);
            _notify("Update didn't finish", "Desk Wheel is unchanged. Click to download the update yourself.", true, () => Links.Open(check.Asset!.Url));
        }
        finally
        {
            Progress = null;
            Interlocked.Exchange(ref _busy, 0);
            Changed?.Invoke();
        }
    }

    private const string Brand_Website = Wheel.Core.Brand.Website;

    /// <summary>Downloads to a file, reporting progress about twice a second.</summary>
    private static async Task DownloadAsync(string url, string file, Action<double> progress)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(file);
        var buffer = new byte[1 << 16];
        long done = 0;
        var clock = Stopwatch.StartNew();
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0 && clock.ElapsedMilliseconds > 400)
            {
                clock.Restart();
                progress(Math.Min(1, (double)done / total));
            }
        }
        progress(1);
    }

    private void InstallWindows(string setup, string folder)
    {
        var psi = new ProcessStartInfo(setup) { UseShellExecute = false };
        foreach (var a in UpdateInstall.WindowsSetupArgs(Path.Combine(folder, "setup.log"))) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
        _quit(); // the installer replaces the files, then starts the new version
    }

    private async Task InstallMacAsync(string dmg, string folder)
    {
        var target = UpdateInstall.MacAppBundle(Environment.ProcessPath ?? "");
        var parent = target is null ? null : Path.GetDirectoryName(target);
        if (target is null || parent is null || !CanWrite(parent))
        {
            // Not in a place it can replace itself (e.g. run from the DMG): open the DMG to drag it over.
            Process.Start("/usr/bin/open", [dmg])?.Dispose();
            _notify("Almost done", "Drag Desk Wheel into Applications and choose Replace.", false, null);
            return;
        }

        var mount = Path.Combine(folder, "mount");
        Directory.CreateDirectory(mount);
        var attach = await Wheel.Core.ProcessRunner.RunAsync("/usr/bin/hdiutil", ["attach", dmg, "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount]);
        if (attach.ExitCode != 0) throw new InvalidOperationException("hdiutil: " + attach.ErrorTail);
        var staged = Path.Combine(parent, ".DeskWheel-update.app");
        try
        {
            var source = Path.Combine(mount, Path.GetFileName(target));
            if (!Directory.Exists(source)) throw new InvalidOperationException("The update doesn't contain " + Path.GetFileName(target));
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
            var copy = await Wheel.Core.ProcessRunner.RunAsync("/usr/bin/ditto", [source, staged]);
            if (copy.ExitCode != 0) throw new InvalidOperationException("ditto: " + copy.ErrorTail);
            var verify = await Wheel.Core.ProcessRunner.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", staged]);
            if (verify.ExitCode != 0) throw new InvalidOperationException("The new app's signature isn't valid: " + verify.ErrorTail);
        }
        finally
        {
            await Wheel.Core.ProcessRunner.RunAsync("/usr/bin/hdiutil", ["detach", mount, "-quiet", "-force"]);
        }

        var script = Path.Combine(folder, "swap.sh");
        await File.WriteAllTextAsync(script, UpdateInstall.MacSwapScript.Replace("\r\n", "\n") + "\n");
        var psi = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
        foreach (var a in new[] { script, Environment.ProcessId.ToString(), staged, target, Path.Combine(folder, "swap.log") }) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
        _quit(); // the script waits for this, swaps the app and opens the new one
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, ".deskwheel-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
