namespace Felix.Bootstrap;

/// <summary>
/// Réglages du démarreur. Ils ne changent presque jamais : le démarreur est fait pour rester figé.
/// (Les choix du joueur — dossier d'installation, mises à jour automatiques… — sont dans bootstrap.json.)
/// </summary>
public sealed record BootstrapConfig
{
    /// <summary>Compte GitHub qui possède le dépôt du launcher.</summary>
    public string Owner { get; init; } = "Felix-Mcorg";

    /// <summary>Dépôt du launcher (dépôt 2), où sont publiées les releases.</summary>
    public string Repository { get; init; } = "felix-launcher";

    /// <summary>Fichier à télécharger dans la release : FelixLauncher-win-x64.zip, -linux-x64.zip, -osx-arm64.zip…</summary>
    public string ZipAssetName { get; init; } = $"FelixLauncher-{Platform.Rid}.zip";

    /// <summary>Programme à lancer, à la racine du zip.</summary>
    public string ExecutableName { get; init; } = Platform.LauncherExecutable;

    /// <summary>Nom du dossier d'installation par défaut.</summary>
    public string AppFolderName { get; init; } = "FelixLauncher";

    /// <summary>Pour les tests : remplace le dossier d'installation.</summary>
    public string? InstallRootOverride { get; init; }

    /// <summary>Pour les tests : remplace le dossier des réglages.</summary>
    public string? ConfigDirOverride { get; init; }

    public string ConfigDir => ConfigDirOverride ?? Platform.ConfigDir(AppFolderName);
    public string SettingsPath => Path.Combine(ConfigDir, "bootstrap.json");

    public static BootstrapConfig FromEnvironment() => new()
    {
        InstallRootOverride = Environment.GetEnvironmentVariable("FELIX_INSTALL_ROOT"),
        ConfigDirOverride = Environment.GetEnvironmentVariable("FELIX_CONFIG_DIR"),
    };
}

/// <summary>Erreur dont le message peut être montré tel quel au joueur.</summary>
public sealed class BootstrapException : Exception
{
    public BootstrapException(string message) : base(message) { }
}

public sealed record BootProgress(string Status, double? Fraction = null, string? Detail = null);

public sealed record LaunchTarget(string ExecutablePath, string Version, bool Updated, string? Notice);
