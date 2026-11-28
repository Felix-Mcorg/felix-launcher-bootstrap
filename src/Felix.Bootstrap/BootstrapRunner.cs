namespace Felix.Bootstrap;

/// <summary>
/// Le déroulé complet : chercher la dernière version, l'installer si besoin,
/// sinon (hors ligne, erreur) retomber sur la version déjà installée.
/// </summary>
public sealed class BootstrapRunner
{
    private readonly InstallLayout _layout;
    private readonly ReleaseClient _releases;
    private readonly LauncherInstaller _installer;

    public BootstrapRunner(InstallLayout layout, ReleaseClient releases, LauncherInstaller installer)
    {
        _layout = layout;
        _releases = releases;
        _installer = installer;
    }

    /// <param name="checkForUpdates">Faux = mises à jour automatiques désactivées : on lance la version installée sans interroger GitHub (sauf s'il n'y en a aucune).</param>
    public async Task<LaunchTarget> RunAsync(
        IProgress<BootProgress> progress, CancellationToken ct = default, bool checkForUpdates = true)
    {
        _layout.EnsureCreated();

        var current = CurrentStore.Read(_layout);
        var currentExe = current is null ? null : CurrentStore.ResolveExecutable(_layout, current);
        if (!checkForUpdates && currentExe is not null)
            return new LaunchTarget(currentExe, current!.Version, Updated: false, Notice: null);

        progress.Report(new BootProgress("Recherche de mises à jour…"));
        Version? currentVersion = null;
        if (current is not null && VersionHelper.TryParse(current.Version, out var parsed))
            currentVersion = parsed;

        ReleaseInfo? latest = null;
        string? problem = null;
        try
        {
            latest = await _releases.GetLatestAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            BootLog.Write($"Recherche de version impossible : {ex.Message}");
        }

        var needsInstall = latest is not null
                           && (currentExe is null || currentVersion is null || latest.Version > currentVersion);
        if (needsInstall)
        {
            try
            {
                var exe = await _installer.InstallAsync(latest!, progress, ct);
                return new LaunchTarget(exe, latest!.Version.ToString(), Updated: true, Notice: null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
                BootLog.Write($"Installation impossible : {ex}");
                if (currentExe is null)
                    throw ex as BootstrapException
                          ?? new BootstrapException($"Installation impossible : {ex.Message}");
            }
        }

        if (currentExe is null)
            throw new BootstrapException(
                "Impossible d'installer le launcher" + (problem is null ? "." : $" : {problem}") +
                "\nVérifie ta connexion internet puis réessaie.");

        return new LaunchTarget(
            currentExe,
            current!.Version,
            Updated: false,
            Notice: problem is null ? null : "Mise à jour impossible : lancement de la version déjà installée.");
    }
}
