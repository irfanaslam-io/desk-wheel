using System;
using System.Runtime.InteropServices;

namespace Wheel.App;

/// <summary>
/// Whether Desk Wheel runs as a Microsoft Store (MSIX) app. The Store version is signed by Microsoft and
/// updated by the Store, starts at sign-in through a Windows startup task (on/off in Settings › Apps › Startup),
/// and can't add itself to Explorer's Send to menu (Store apps' writes to AppData are kept private).
/// </summary>
internal static class PackageInfo
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8)) return false;
        try
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Windows' own list of startup apps, where the Store version's "start at sign-in" is switched.</summary>
    public const string StartupSettings = "ms-settings:startupapps";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
