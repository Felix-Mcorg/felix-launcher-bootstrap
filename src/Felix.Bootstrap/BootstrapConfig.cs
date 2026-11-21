namespace Felix.Bootstrap;

/// <summary>
/// Réglages du démarreur. Ils ne changent presque jamais : le démarreur est fait pour rester figé.
/// </summary>
public sealed record BootstrapConfig
{
    /// <summary>Compte GitHub qui possède le dépôt du launcher.</summary>
    public string Owner { get; init; } = "Felix-Mcorg";

    /// <summary>Dépôt du launcher (dépôt 2), où sont publiées les releases.</summary>
    public string Repository { get; init; } = "felix-launcher";

    /// <summary>Fichier à télécharger dans la release (et « .sha256 » à côté pour son empreinte).</summary>
    public string ZipAssetName { get; init; } = "FelixLauncher-win-x64.zip";

    /// <summary>Programme à lancer, à la racine du zip.</summary>
    public string ExecutableName { get; init; } = "FelixLauncher.exe";

    /// <summary>Dossier d'installation, dans %LOCALAPPDATA%.</summary>
    public string AppFolderName { get; init; } = "FelixLauncher";

    /// <summary>Pour les tests : remplace le dossier d'installation.</summary>
    public string? InstallRootOverride { get; init; }

    public static BootstrapConfig FromEnvironment() => new()
    {
        InstallRootOverride = Environment.GetEnvironmentVariable("FELIX_INSTALL_ROOT"),
    };
}

/// <summary>Erreur dont le message peut être montré tel quel au joueur.</summary>
public sealed class BootstrapException : Exception
{
    public BootstrapException(string message) : base(message) { }
}

public sealed record BootProgress(string Status, double? Fraction = null, string? Detail = null);

public sealed record LaunchTarget(string ExecutablePath, string Version, bool Updated, string? Notice);
