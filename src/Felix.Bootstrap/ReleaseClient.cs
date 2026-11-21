using System.Net;
using System.Text.Json;

namespace Felix.Bootstrap;

public sealed record ReleaseInfo(string Tag, Version Version, string ZipUrl, string? Sha256Url, string? Sha256Inline);

/// <summary>
/// Cherche la dernière release du launcher. Méthode principale : l'API GitHub.
/// Plan B (si l'API est limitée ou en panne) : la redirection de « /releases/latest »,
/// qui ne compte pas dans la limite de l'API.
/// </summary>
public sealed class ReleaseClient
{
    private readonly BootstrapConfig _config;
    private readonly HttpClient _http;
    private readonly HttpClient _noRedirectHttp;

    public ReleaseClient(BootstrapConfig config, HttpClient http, HttpClient noRedirectHttp)
    {
        _config = config;
        _http = http;
        _noRedirectHttp = noRedirectHttp;
    }

    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            return await ViaApiAsync(ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException or JsonException or KeyNotFoundException
                                       or InvalidOperationException or OperationCanceledException)
                                   && !ct.IsCancellationRequested)
        {
            BootLog.Write($"API GitHub indisponible ({ex.Message}), essai par redirection.");
            return await ViaRedirectAsync(ct);
        }
    }

    private async Task<ReleaseInfo> ViaApiAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{_config.Owner}/{_config.Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
        return ParseRelease(document.RootElement, _config);
    }

    public static ReleaseInfo ParseRelease(JsonElement root, BootstrapConfig config)
    {
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!VersionHelper.TryParse(tag, out var version))
            throw new BootstrapException($"Numéro de version illisible dans la release : « {tag} ».");

        string? zipUrl = null, shaUrl = null, digest = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == config.ZipAssetName)
            {
                zipUrl = url;
                if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String)
                {
                    var text = d.GetString() ?? "";
                    if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        digest = text["sha256:".Length..].ToLowerInvariant();
                }
            }
            else if (name == config.ZipAssetName + ".sha256")
            {
                shaUrl = url;
            }
        }

        if (zipUrl is null)
            throw new BootstrapException($"La release {tag} ne contient pas le fichier {config.ZipAssetName}.");

        return new ReleaseInfo(tag, version, zipUrl, shaUrl, digest);
    }

    private async Task<ReleaseInfo> ViaRedirectAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://github.com/{_config.Owner}/{_config.Repository}/releases/latest");
        using var response = await _noRedirectHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var status = (int)response.StatusCode;
        var location = response.Headers.Location?.OriginalString;
        if (status is not (301 or 302 or 303 or 307 or 308) || location is null || !location.Contains("/tag/"))
            throw new BootstrapException("Aucune version du launcher n'est publiée pour le moment.");

        var tag = Uri.UnescapeDataString(location.TrimEnd('/').Split('/')[^1]);
        if (!VersionHelper.TryParse(tag, out var version))
            throw new BootstrapException($"Numéro de version illisible dans la release : « {tag} ».");

        var baseUrl = $"https://github.com/{_config.Owner}/{_config.Repository}/releases/download/{Uri.EscapeDataString(tag)}";
        return new ReleaseInfo(
            tag, version, $"{baseUrl}/{_config.ZipAssetName}", $"{baseUrl}/{_config.ZipAssetName}.sha256", null);
    }
}
