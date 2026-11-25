using System.IO;
using System.Linq;
using System.Reflection;

namespace Wheel.App;

/// <summary>
/// Values filled in when the app is built (see README › Releases), so no keys live in the source code:
/// the Google Analytics ids (WHEEL_GA_MEASUREMENT_ID, WHEEL_GA_API_SECRET), the update feed address
/// (WHEEL_UPDATE_FEED) and the public key that update feeds must be signed with (platform/update-public-key.pem).
/// Anything left empty switches that feature off.
/// </summary>
internal static class BuildConfig
{
    public static string? GaMeasurementId { get; } = Meta("GaMeasurementId");
    public static string? GaApiSecret { get; } = Meta("GaApiSecret");
    public static string? UpdateFeed { get; } = Meta("UpdateFeed");
    public static string? UpdatePublicKey { get; } = Resource("DeskWheel.update-public-key.pem");

    public static string Version => typeof(BuildConfig).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static bool StatsAvailable => Wheel.Core.Telemetry.UsageStats.Configured(GaMeasurementId, GaApiSecret);
    public static bool UpdatesAvailable => !string.IsNullOrWhiteSpace(UpdateFeed);

    private static string? Meta(string key)
    {
        var value = typeof(BuildConfig).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Resource(string name)
    {
        using var stream = typeof(BuildConfig).Assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return text.Contains("BEGIN PUBLIC KEY") ? text : null;
    }
}
