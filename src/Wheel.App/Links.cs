using System;
using System.Diagnostics;
using Wheel.Core;

namespace Wheel.App;

/// <summary>Opens the website or a new support email in the person's own browser / mail app.</summary>
internal static class Links
{
    public static string SupportMailto(string? version = null) =>
        "mailto:" + Brand.SupportEmail + "?subject=" + Uri.EscapeDataString($"{Brand.Name} {version ?? Version}".Trim());

    public static string Version => typeof(Links).Assembly.GetName().Version?.ToString(3) ?? "";

    public static void Open(string uri)
    {
        try
        {
            var psi = OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, ArgumentList = { uri } }
                : new ProcessStartInfo(uri) { UseShellExecute = true };
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Write(ex);
        }
    }
}
