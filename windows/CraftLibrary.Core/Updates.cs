using System.Security.Cryptography;
using System.Text.Json;

namespace CraftLibrary.Core;

/// <summary>A newer version found on the project's Releases page.</summary>
public sealed record UpdateInfo(Version Version, string NotesUrl, string DownloadUrl, string FileName, long Size, string Sha256);

/// <summary>
/// Reads the project's newest release (GitHub's "latest release" answer) and checks a downloaded installer.
/// Nothing here touches the network or her files, so it is all testable.
/// </summary>
public static class Updates
{
    /// <summary>The public repository releases come from. A fork changes this to its own.</summary>
    public const string Repo = "TechDad92/craft-library";
    public static string LatestReleaseApi => "https://api.github.com/repos/" + Repo + "/releases/latest";
    public static string ReleasesPage => "https://github.com/" + Repo + "/releases";
    /// <summary>Installers are only ever downloaded from this repository's own releases.</summary>
    private static string DownloadPrefix => "https://github.com/" + Repo + "/releases/download/";

    /// <summary>"v0.7.0" or "0.7.0" → 0.7.0. Anything else → null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var t = (tag ?? "").Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        if (!Version.TryParse(t, out var v)) return null;
        return Normal(v);
    }

    /// <summary>0.6.2 and 0.6.2.0 count as the same version.</summary>
    public static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static string InstallerName(Version v) => $"Craft-Library-Setup-{v.Major}.{v.Minor}.{v.Build}.exe";

    /// <summary>
    /// The newer version described by GitHub's latest-release answer, or null when there's nothing newer,
    /// it's a draft or test version, or the installer (with its checksum) isn't attached.
    /// </summary>
    public static UpdateInfo? FindNewer(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (Bool(r, "draft") || Bool(r, "prerelease")) return null;
            var v = ParseVersion(Str(r, "tag_name"));
            if (v == null || v <= Normal(current)) return null;
            if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            var wanted = InstallerName(v);
            foreach (var a in assets.EnumerateArray())
            {
                if (!string.Equals(Str(a, "name"), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                var url = Str(a, "browser_download_url") ?? "";
                if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) return null;
                var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                var digest = Str(a, "digest") ?? "";
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
                var sha = digest[7..].ToLowerInvariant();
                if (sha.Length != 64 || !sha.All(Uri.IsHexDigit) || size <= 0) return null;
                var notes = Str(r, "html_url") ?? "";
                if (!notes.StartsWith("https://github.com/" + Repo + "/", StringComparison.Ordinal)) notes = ReleasesPage;
                return new UpdateInfo(v, notes, url, wanted, size, sha);
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>True when the downloaded file is exactly the one GitHub lists (same size and checksum).</summary>
    public static bool FileMatches(string path, UpdateInfo info)
    {
        var f = new FileInfo(path);
        if (!f.Exists || f.Length != info.Size) return false;
        using var s = f.OpenRead();
        var hash = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        return hash == info.Sha256;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
