using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wheel.Core.Updates;

/// <summary>One downloadable installer in the feed.</summary>
public sealed record UpdateAsset(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size = 0);

/// <summary>
/// The update feed: one small JSON file, published with each release next to its signature
/// ("update.json" and "update.json.sig").
/// <code>
/// { "version": "0.18.0", "date": "2026-12-01", "notes": "What's new…", "page": "https://…",
///   "assets": { "mac-arm64": { "url": "…dmg", "sha256": "…", "size": 123 },
///               "windows-x64": { "url": "…setup.exe", "sha256": "…" } } }
/// </code>
/// </summary>
public sealed record UpdateInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("assets")] Dictionary<string, UpdateAsset> Assets,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("date")] string? Date = null,
    [property: JsonPropertyName("page")] string? Page = null);

public static class UpdateFeed
{
    /// <summary>This computer's installer: "mac-arm64", "mac-x64", "windows-x64" or "windows-arm64".</summary>
    public static string AssetKey(bool? windows = null, Architecture? arch = null)
    {
        var os = (windows ?? OperatingSystem.IsWindows()) ? "windows" : "mac";
        return (arch ?? RuntimeInformation.OSArchitecture) == Architecture.Arm64 ? $"{os}-arm64" : $"{os}-x64";
    }

    public static UpdateInfo? Parse(string json)
    {
        try
        {
            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            return info is { Version.Length: > 0, Assets: not null } && TryVersion(info.Version, out _) ? info : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"0.18.0" is newer than "0.17.2"; a leading "v" and a "-beta" style suffix are ignored.</summary>
    public static bool IsNewer(string current, string candidate) =>
        TryVersion(candidate, out var c) && (!TryVersion(current, out var cur) || c > cur);

    private static bool TryVersion(string text, out Version version)
    {
        var t = text.Trim().TrimStart('v', 'V');
        var dash = t.IndexOfAny(['-', '+']);
        if (dash >= 0) t = t[..dash];
        if (!t.Contains('.')) t += ".0";
        return Version.TryParse(t, out version!);
    }

    /// <summary>
    /// True when <paramref name="signature"/> (DER ECDSA P-256 over SHA-256, as made by
    /// <c>openssl dgst -sha256 -sign key.pem</c>) was made by the private key matching <paramref name="publicKeyPem"/>.
    /// The feed is only trusted when this holds, so a hijacked download server can't push its own installer.
    /// </summary>
    public static bool SignatureValid(byte[] feed, byte[] signature, string publicKeyPem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(feed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The signature file may be raw DER or base64 text; both are accepted.</summary>
    public static byte[] DecodeSignature(byte[] raw)
    {
        var text = System.Text.Encoding.ASCII.GetString(raw).Trim();
        try
        {
            if (text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='))
                return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
        }
        return raw;
    }

    public static string Sha256Of(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static bool Sha256Matches(string file, string expected) =>
        string.Equals(Sha256Of(file), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a check found.</summary>
public sealed record UpdateCheck(UpdateInfo? Info, UpdateAsset? Asset, bool Verified, string? Problem)
{
    public bool Available => Info is not null;
    /// <summary>Can be downloaded and installed from inside the app (verified, with an installer for this computer).</summary>
    public bool CanInstall => Available && Verified && Asset is not null;
}

/// <summary>Downloads and checks the feed.</summary>
public static class UpdateChecker
{
    public static async Task<UpdateCheck> CheckAsync(HttpClient http, string feedUrl, string currentVersion, string? publicKeyPem,
                                                      string? assetKey = null, CancellationToken ct = default)
    {
        byte[] feed;
        try
        {
            feed = await http.GetByteArrayAsync(feedUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new UpdateCheck(null, null, false, "Couldn't reach the update server. Check your internet connection.");
        }

        var info = UpdateFeed.Parse(System.Text.Encoding.UTF8.GetString(feed));
        if (info is null)
            return new UpdateCheck(null, null, false, "The update information couldn't be read.");

        var verified = false;
        if (!string.IsNullOrWhiteSpace(publicKeyPem))
        {
            try
            {
                var sig = UpdateFeed.DecodeSignature(await http.GetByteArrayAsync(feedUrl + ".sig", ct));
                verified = UpdateFeed.SignatureValid(feed, sig, publicKeyPem);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }
            if (!verified)
                return new UpdateCheck(null, null, false, "The update couldn't be verified, so it was ignored.");
        }

        if (!UpdateFeed.IsNewer(currentVersion, info.Version))
            return new UpdateCheck(null, null, verified, null); // up to date

        info.Assets.TryGetValue(assetKey ?? UpdateFeed.AssetKey(), out var asset);
        return new UpdateCheck(info, asset is { Url.Length: > 0, Sha256.Length: 64 } && asset.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? asset : null, verified, null);
    }
}

/// <summary>How a downloaded installer replaces the running app, without uninstalling.</summary>
public static class UpdateInstall
{
    /// <summary>
    /// Windows: the Inno Setup installer upgrades in place (same app id), closes the running copy, keeps
    /// settings, and starts the new version again when run silently.
    /// </summary>
    public static IReadOnlyList<string> WindowsSetupArgs(string log) =>
        ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/NOCANCEL", "/LOG=" + log];

    /// <summary>
    /// macOS: a small script, started just before the app quits. It waits for the app to exit, swaps in the new
    /// app (keeping the old one until the copy succeeds, and putting it back if anything fails), and opens it.
    /// Arguments: pid, staged new app, installed app, log file.
    /// </summary>
    public const string MacSwapScript = """
        #!/bin/bash
        pid="$1"; new="$2"; target="$3"; log="$4"
        exec >>"$log" 2>&1
        echo "$(date) updating $target"
        for i in $(seq 1 100); do kill -0 "$pid" 2>/dev/null || break; sleep 0.2; done
        backup="${target%.app}.previous.app"
        rm -rf "$backup"
        if mv "$target" "$backup" && /usr/bin/ditto "$new" "$target"; then
          /usr/bin/xattr -dr com.apple.quarantine "$target" 2>/dev/null
          rm -rf "$backup" "$new"
          echo "$(date) done"
        else
          echo "$(date) failed, restoring the previous version"
          rm -rf "$target"; mv "$backup" "$target"
        fi
        /usr/bin/open "$target"
        """;

    /// <summary>The installed .app around the running program (…/DeskWheel.app/Contents/MacOS/DeskWheel), or null.</summary>
    public static string? MacAppBundle(string processPath)
    {
        // macOS paths, handled as text so the result is the same whichever system runs this.
        var parts = processPath.Split('/');
        if (parts.Length < 4 || parts[^2] != "MacOS" || parts[^3] != "Contents" || !parts[^4].EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return null;
        return string.Join('/', parts[..^3]);
    }
}
