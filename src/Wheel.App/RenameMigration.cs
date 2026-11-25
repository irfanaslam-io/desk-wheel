using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Wheel.App;

/// <summary>
/// The app was called "Wheel" before it became DeskWheel. On the first start after the rename, this carries
/// over what the old name left behind, so nothing is lost: settings, history and the clipboard (the data
/// folder), "start at login", and Windows' Send to entry. Runs once; does nothing when there's nothing old.
/// </summary>
internal static class RenameMigration
{
    public static void Run()
    {
        if (PackageInfo.IsPackaged) return; // the Store version never had the old name
        try
        {
            MoveDataFolder();
            if (OperatingSystem.IsMacOS()) MacLoginItem();
            if (OperatingSystem.IsWindows()) { WindowsLoginItem(); WindowsSendTo(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Best effort: the app works without the old data.
        }
    }

    private static void MoveDataFolder()
    {
        var parent = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var old = Path.Combine(parent, "Wheel");
        var now = Path.Combine(parent, "DeskWheel");
        if (Directory.Exists(old) && !Directory.Exists(now))
            Directory.Move(old, now);
    }

    [SupportedOSPlatform("macos")]
    private static void MacLoginItem()
    {
        var agents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
        var old = Path.Combine(agents, "app.wheel.plist");
        if (!File.Exists(old)) return;
        File.Delete(old);
        new Platform.MacPlatform().StartAtLogin = true; // the same choice, under the new name and path
    }

    [SupportedOSPlatform("windows")]
    private static void WindowsLoginItem()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key?.GetValue("Wheel") is not string) return;
        key.DeleteValue("Wheel", throwOnMissingValue: false);
        key.SetValue("DeskWheel", $"\"{Environment.ProcessPath}\"");
    }

    [SupportedOSPlatform("windows")]
    private static void WindowsSendTo()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Wheel (convert or tools).lnk");
        if (!File.Exists(old)) return;
        File.Delete(old);
        SendToShortcut.Install(out _);
    }
}
