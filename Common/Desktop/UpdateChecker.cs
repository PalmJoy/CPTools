using System.Net.Http;
using System.Reflection;
using System.Text.Json;
#if CPTOOLS_CLIPBOARD
namespace CPClipboard.App;
#elif CPTOOLS_MONITOR
namespace CPSystemMonitor.App;
#elif CPTOOLS_LAUNCHER
namespace FileLauncher.App;
#else
#error A CPTools product must be selected.
#endif

internal static class UpdateChecker
{
    internal const string Website = ProductReleaseSettings.Website;
    internal const string TagPrefix = ProductReleaseSettings.TagPrefix;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    internal static string CurrentVersion => typeof(App).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    internal static bool IsNewer(string tag, string current)
    {
        Version? Parse(string value)
        {
            if (value.StartsWith(TagPrefix, StringComparison.Ordinal)) value = value[TagPrefix.Length..];
            return Version.TryParse(value.TrimStart('v', 'V').Split('+')[0], out var v)
                ? new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)) : null;
        }
        return Parse(tag) is { } latest && Parse(current) is { } installed && latest > installed;
    }

    internal static string? SelectLatestTag(JsonElement releases)
    {
        if (releases.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a release list");
        string? best = null;
        Version? bestVersion = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object
                || !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !release.TryGetProperty("prerelease", out var preview) || preview.ValueKind != JsonValueKind.False
                || !release.TryGetProperty("tag_name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            string tag = name.GetString()!;
            if (!tag.StartsWith(TagPrefix, StringComparison.Ordinal)) continue;
            string numeric = tag[TagPrefix.Length..];
            var parts = numeric.Split('.');
            if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Any(c => c < '0' || c > '9'))
                || !Version.TryParse(numeric, out var version)) continue;
            if (bestVersion is null || version > bestVersion) { best = tag; bestVersion = version; }
        }
        return best;
    }

    internal static async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            ProductReleaseSettings.Api);
        request.Headers.UserAgent.ParseAdd(AppInfo.ProductName + "/" + CurrentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = SelectLatestTag(json.RootElement);
        return tag is not null && IsNewer(tag, CurrentVersion) ? tag : null;
    }
}
