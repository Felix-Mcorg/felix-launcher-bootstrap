using System.Text.Json;
using System.Text.Json.Serialization;

namespace Felix.Bootstrap;

/// <summary>
/// Arborescence de l'installation (la racine est choisie par le joueur) :
///   app/1.2.3/FelixLauncher(.exe) ...   un dossier par version : pas de fichier verrouillé
///   bootstrap/FelixBootstrap(.exe)      copie du démarreur (cible des raccourcis)
///   current.json                        version à lancer
///   settings.json                       réglages du launcher
///   tmp/   logs/
/// </summary>
public sealed class InstallLayout
{
    public InstallLayout(string root) => Root = root;

    public string Root { get; }
    public string AppDir => Path.Combine(Root, "app");
    public string TmpDir => Path.Combine(Root, "tmp");
    public string LogsDir => Path.Combine(Root, "logs");
    public string BootstrapDir => Path.Combine(Root, "bootstrap");
    public string CurrentFile => Path.Combine(Root, "current.json");
    public string LauncherSettingsFile => Path.Combine(Root, "settings.json");

    public string VersionDir(string version) => Path.Combine(AppDir, version);

    public static InstallLayout Create(BootstrapConfig config, BootSettings settings) => new(
        config.InstallRootOverride
        ?? (string.IsNullOrWhiteSpace(settings.InstallRoot) ? null : settings.InstallRoot)
        ?? Platform.DefaultInstallRoot(config.AppFolderName));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(TmpDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>Vrai si ce dossier ressemble à une installation du launcher (sert de garde-fou avant de supprimer).</summary>
    public bool LooksInstalled => File.Exists(CurrentFile) || Directory.Exists(AppDir);
}

/// <summary>Version installée. Executable est relatif à la racine, avec des « / ».</summary>
public sealed record CurrentInfo(string Version, string Executable, string? Previous = null);

/// <summary>Les choix du joueur, dans bootstrap.json (hors du dossier d'installation).</summary>
public sealed class BootSettings
{
    /// <summary>Dossier d'installation choisi (null = dossier par défaut du système).</summary>
    public string? InstallRoot { get; set; }

    /// <summary>Chercher et installer les nouvelles versions du launcher à chaque ouverture.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Raccourcis (bureau, menu) souhaités.</summary>
    public bool Shortcuts { get; set; } = true;

    /// <summary>Faux tant que l'assistant de premier lancement n'a pas été passé.</summary>
    public bool Configured { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CurrentInfo))]
[JsonSerializable(typeof(BootSettings))]
internal sealed partial class BootJsonContext : JsonSerializerContext
{
}

public static class BootSettingsStore
{
    public static BootSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new BootSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(path), BootJsonContext.Default.BootSettings) ?? new BootSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new BootSettings(); // fichier abîmé : on repart des valeurs par défaut
        }
    }

    public static void Save(string path, BootSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, BootJsonContext.Default.BootSettings));
        File.Move(temp, path, overwrite: true);
    }
}

public static class CurrentStore
{
    public static CurrentInfo? Read(InstallLayout layout)
    {
        if (!File.Exists(layout.CurrentFile))
            return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(layout.CurrentFile), BootJsonContext.Default.CurrentInfo);
        }
        catch (JsonException)
        {
            return null; // fichier abîmé : on réinstalle
        }
    }

    public static void Write(InstallLayout layout, CurrentInfo info)
    {
        var temp = layout.CurrentFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(info, BootJsonContext.Default.CurrentInfo));
        File.Move(temp, layout.CurrentFile, overwrite: true);
    }

    /// <summary>Chemin complet du programme à lancer, ou null s'il n'existe pas ou sort du dossier d'installation.</summary>
    public static string? ResolveExecutable(InstallLayout layout, CurrentInfo info)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.Root)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(layout.Root, info.Executable.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(root, comparison) && File.Exists(full) ? full : null;
    }
}
