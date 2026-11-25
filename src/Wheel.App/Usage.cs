using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Wheel.Core.Telemetry;

namespace Wheel.App;

/// <summary>
/// The app's single place for anonymous usage counts (see <see cref="UsageStats"/> for what is and isn't sent).
/// Does nothing unless Google Analytics ids were built in and the person said yes.
/// Desk Wheel shares irfanaslam.io's Google Analytics property, so every event is named "dw_…" and carries
/// app_name = "desk-wheel": one filter shows (or hides) the app's activity next to the website's.
/// </summary>
internal static class Usage
{
    private static UsageStats? _stats;
    private static AppSettings? _settings;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static void Start(AppSettings settings, string dataFolder)
    {
        _settings = settings;
        if (!BuildConfig.StatsAvailable) return;
        _stats = new UsageStats(dataFolder, BuildConfig.GaMeasurementId!, BuildConfig.GaApiSecret!,
            allowed: () => _settings?.ShareUsage == true,
            common: new Dictionary<string, object>
            {
                ["app_name"] = "desk-wheel",
                ["app_version"] = BuildConfig.Version,
                ["os"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "other",
                ["arch"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            });
    }

    /// <summary>True when the person should be asked (ids built in, not asked yet).</summary>
    public static bool ShouldAsk => _stats is not null && _settings?.ShareUsage is null;

    public static void Track(string name, Dictionary<string, object>? parameters = null) => _stats?.Track("dw_" + name, parameters);

    /// <summary>
    /// A Desk Wheel screen was opened ("settings-updates", "clipboard", "wheel-convert"…): event dw_screen with
    /// screen_name, so reports show which screens people actually use.
    /// </summary>
    public static void Screen(string name) => Track("screen", new() { ["screen_name"] = ScreenCode(name) });

    /// <summary>"SettingsWindow" → "settings", "Watched folders" → "watched-folders".</summary>
    public static string ScreenCode(string name)
    {
        if (name.EndsWith("Window", StringComparison.Ordinal) && name.Length > 6) name = name[..^6];
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsUpper(c) && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        var code = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return code.Length > 40 ? code[..40] : code;
    }

    /// <summary>A tool from the wheel was used: its id (e.g. "img.to-png") and roughly how many files.</summary>
    public static void Tool(string optionId, int files) =>
        Track("tool_used", new() { ["tool"] = optionId, ["files"] = UsageStats.Bucket(files) });

    public static void Forget() => _stats?.Forget();

    public static async Task FlushAsync()
    {
        if (_stats is null) return;
        await _stats.FlushAsync(async (url, json) =>
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        });
    }
}
