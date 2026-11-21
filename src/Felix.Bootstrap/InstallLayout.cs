using System.Text.Json;
using System.Text.Json.Serialization;

namespace Felix.Bootstrap;

/// <summary>
/// Arborescence sur l'ordinateur du joueur :
///   %LOCALAPPDATA%\FelixLauncher\
///     app\1.2.3\FelixLauncher.exe ...   (un dossier par version : pas de fichier verrouillé)
///     current.json                      (version à lancer)
///     tmp\   logs\
/// </summary>
public sealed class InstallLayout
{
    public InstallLayout(string root) => Root = root;

    public string Root { get; }
    public string AppDir => Path.Combine(Root, "app");
    public string TmpDir => Path.Combine(Root, "tmp");
    public string LogsDir => Path.Combine(Root, "logs");
    public string CurrentFile => Path.Combine(Root, "current.json");

    public string VersionDir(string version) => Path.Combine(AppDir, version);

    public static InstallLayout Create(BootstrapConfig config) => new(
        config.InstallRootOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), config.AppFolderName));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(TmpDir);
        Directory.CreateDirectory(LogsDir);
    }
}

/// <summary>Version installée. Executable est relatif à la racine, avec des « / ».</summary>
public sealed record CurrentInfo(string Version, string Executable, string? Previous = null);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CurrentInfo))]
internal sealed partial class BootJsonContext : JsonSerializerContext
{
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
