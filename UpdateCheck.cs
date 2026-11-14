using System.Net.Http;
using System.Text.Json;

namespace UltimateMacro;

internal enum UpdateStatus { Current, Available, NoRelease, Unavailable }
internal sealed record UpdatePackage(Version Version, long Size, string? Sha256);
internal sealed record UpdateResult(UpdateStatus Status, UpdatePackage? Package = null);

internal static class UpdateCheck
{
    private const string Repository = "adrianmue/UltimateMacro";
    private const string Endpoint = $"repos/{Repository}/releases/latest";
    private const string Package = "UltimateMacro-Windows-x64.zip";
    internal static readonly HttpClient Client = new();
    internal const string ReleasesUrl = $"https://github.com/{Repository}/releases";
    internal static Version InstalledVersion { get; } = typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0, 0);
    internal static string DownloadUrl(Version version) => $"{ReleasesUrl}/download/v{version.ToString(3)}/{Package}";

    internal static async Task<UpdateResult> CheckAsync(CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/{Endpoint}");
            request.Headers.UserAgent.ParseAdd($"UltimateMacro/{InstalledVersion.ToString(3)}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Client.SendAsync(request, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return ReadRelease(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false), InstalledVersion);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        return new(UpdateStatus.Unavailable);
    }

    internal static UpdateResult ReadRelease(string json, Version installed)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var release = document.RootElement;
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return new(UpdateStatus.NoRelease);
            var tag = release.GetProperty("tag_name").GetString();
            if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
                version.Build < 0 || version.Revision >= 0 || tag != $"v{version.ToString(3)}") return new(UpdateStatus.Unavailable);
            if (version <= new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build))) return new(UpdateStatus.Current);
            var download = DownloadUrl(version);
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != Package || asset.GetProperty("state").GetString() != "uploaded" ||
                    asset.GetProperty("browser_download_url").GetString() != download) continue;
                var size = asset.GetProperty("size").GetInt64();
                if (size <= 0) continue;
                var digest = asset.TryGetProperty("digest", out var property) ? property.GetString() : null;
                var sha256 = digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.Ordinal) &&
                    digest.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0 ? digest[7..] : null;
                return new(UpdateStatus.Available, new UpdatePackage(version, size, sha256));
            }
            return new(UpdateStatus.NoRelease);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new(UpdateStatus.Unavailable);
        }
    }
}
