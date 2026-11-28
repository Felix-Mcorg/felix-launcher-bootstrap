using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Felix.Bootstrap;

/// <summary>
/// Télécharge le launcher, vérifie son SHA-256, l'installe dans app\&lt;version&gt;\ puis
/// met à jour current.json. Garde la version précédente (retour en arrière possible).
/// </summary>
public sealed class LauncherInstaller
{
    private readonly BootstrapConfig _config;
    private readonly InstallLayout _layout;
    private readonly HttpClient _http;

    public LauncherInstaller(BootstrapConfig config, InstallLayout layout, HttpClient http)
    {
        _config = config;
        _layout = layout;
        _http = http;
    }

    /// <summary>Installe la release et retourne le chemin complet du programme à lancer.</summary>
    public async Task<string> InstallAsync(ReleaseInfo release, IProgress<BootProgress> progress, CancellationToken ct = default)
    {
        _layout.EnsureCreated();
        var version = release.Version.ToString();
        var zipPath = Path.Combine(_layout.TmpDir, $"{version}.zip");

        var expected = release.Sha256Inline ?? await FetchExpectedHashAsync(release, ct);

        string actual = "";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                actual = await DownloadAsync(release.ZipUrl, zipPath, progress, ct);
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < 3)
            {
                BootLog.Write($"Téléchargement raté (essai {attempt}) : {ex.Message}");
                progress.Report(new BootProgress($"Nouvelle tentative ({attempt + 1}/3)…"));
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zipPath);
            throw new BootstrapException("Le fichier téléchargé est corrompu (empreinte SHA-256 différente). Réessaie.");
        }

        progress.Report(new BootProgress("Installation…", 1.0));
        var finalDir = _layout.VersionDir(version);
        var tempDir = finalDir + ".tmp";
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, tempDir); // refuse les chemins qui sortent du dossier

        if (!File.Exists(Path.Combine(tempDir, _config.ExecutableName)))
        {
            Directory.Delete(tempDir, recursive: true);
            throw new BootstrapException($"{_config.ExecutableName} est introuvable dans le zip du launcher.");
        }

        Platform.MakeExecutable(Path.Combine(tempDir, _config.ExecutableName)); // Linux/macOS : droit d'exécution

        if (Directory.Exists(finalDir))
            Directory.Delete(finalDir, recursive: true);
        Directory.Move(tempDir, finalDir);
        File.Delete(zipPath);

        var previous = CurrentStore.Read(_layout)?.Version;
        if (previous == version)
            previous = null;
        CurrentStore.Write(_layout, new CurrentInfo(version, $"app/{version}/{_config.ExecutableName}", previous));

        Cleanup(keep: new[] { version, previous });
        BootLog.Write($"Launcher {version} installé.");
        return Path.Combine(finalDir, _config.ExecutableName);
    }

    private async Task<string> FetchExpectedHashAsync(ReleaseInfo release, CancellationToken ct)
    {
        if (release.Sha256Url is null)
            throw new BootstrapException("La release n'a pas de fichier d'empreinte (.sha256) : installation refusée.");

        var text = await _http.GetStringAsync(release.Sha256Url, ct);
        var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new BootstrapException("Le fichier d'empreinte (.sha256) de la release est illisible.");
        return token.ToLowerInvariant();
    }

    private async Task<string> DownloadAsync(string url, string path, IProgress<BootProgress> progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        long done = 0;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            hasher.AppendData(buffer, 0, read);
            done += read;

            if (clock.Elapsed - lastReport > TimeSpan.FromMilliseconds(100))
            {
                lastReport = clock.Elapsed;
                var speed = done / Math.Max(clock.Elapsed.TotalSeconds, 0.001);
                progress.Report(new BootProgress(
                    "Téléchargement du launcher…",
                    total > 0 ? (double)done / total : null,
                    $"{BootFormat.Size(done)} / {BootFormat.Size(total)} · {BootFormat.Size((long)speed)}/s"));
            }
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Supprime les anciennes versions : on garde la version courante et la précédente.</summary>
    private void Cleanup(string?[] keep)
    {
        if (!Directory.Exists(_layout.AppDir))
            return;
        foreach (var dir in Directory.GetDirectories(_layout.AppDir))
        {
            var name = Path.GetFileName(dir);
            if (keep.Contains(name))
                continue;
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                BootLog.Write($"Impossible de supprimer {name} (probablement en cours d'utilisation) : {ex.Message}");
            }
        }
    }
}

public static class BootFormat
{
    public static string Size(long bytes)
    {
        double n = bytes;
        foreach (var unit in new[] { "o", "Ko", "Mo", "Go" })
        {
            if (n < 1024 || unit == "Go")
                return unit == "o" ? $"{n:0} {unit}" : $"{n:0.0} {unit}";
            n /= 1024;
        }
        return "";
    }
}
