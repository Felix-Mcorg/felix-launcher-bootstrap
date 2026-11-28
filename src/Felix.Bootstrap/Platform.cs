using System.Runtime.InteropServices;

namespace Felix.Bootstrap;

/// <summary>Tout ce qui change d'un système à l'autre : noms de fichiers, dossiers, droits d'exécution.</summary>
public static class Platform
{
    public static string Os => OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    public static string Arch => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        var other => other.ToString().ToLowerInvariant(),
    };

    /// <summary>« win-x64 », « linux-x64 », « osx-arm64 »… : sert à choisir le bon zip dans la release.</summary>
    public static string Rid => $"{Os}-{Arch}";

    public static string LauncherExecutable => OperatingSystem.IsWindows() ? "FelixLauncher.exe" : "FelixLauncher";
    public static string BootstrapExecutable => OperatingSystem.IsWindows() ? "FelixBootstrap.exe" : "FelixBootstrap";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Windows %LOCALAPPDATA%\FelixLauncher · macOS ~/Library/Application Support/FelixLauncher ·
    /// Linux ~/.local/share/FelixLauncher (ou $XDG_DATA_HOME). Même choix que le launcher.
    /// </summary>
    public static string DefaultInstallRoot(string appFolder)
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appFolder);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Home, "Library", "Application Support", appFolder);

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Home, ".local", "share") : xdg, appFolder);
    }

    /// <summary>
    /// Dossier des réglages du démarreur (bootstrap.json). Il est volontairement SÉPARÉ du dossier d'installation :
    /// c'est lui qui retient où le launcher est installé, même si on le déplace.
    /// </summary>
    public static string ConfigDir(string appFolder)
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appFolder);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Home, "Library", "Application Support", appFolder + " Settings");

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Home, ".config") : xdg, "felix-launcher");
    }

    /// <summary>Dossier qui contient le jeu de chaque serveur (même choix que le launcher).</summary>
    public static string DefaultGameDir()
    {
        string documents;
        if (OperatingSystem.IsWindows())
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        else
        {
            documents = Path.Combine(Home, "Documents");
            if (!Directory.Exists(documents))
                documents = Home;
        }
        if (string.IsNullOrEmpty(documents))
            documents = Home;
        return Path.Combine(documents, "FelixLauncher");
    }

    public static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
            return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BootLog.Write($"Impossible de rendre {path} exécutable : {ex.Message}");
        }
    }

    public static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Vrai si <paramref name="child"/> est <paramref name="parent"/> lui-même ou se trouve dedans.</summary>
    public static bool IsInside(string child, string parent)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child)) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    /// <summary>Refuse les dossiers qu'on ne doit jamais supprimer ni utiliser comme installation (racine, dossier perso, Documents…).</summary>
    public static bool IsDangerousFolder(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.Length == 0 || Path.GetPathRoot(full) is { } root && SamePath(root, full))
            return true;

        var protectedFolders = new[]
        {
            Home,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.Combine(Home, "Downloads"),
            Path.Combine(Home, "Documents"),
        };
        return protectedFolders.Any(f => !string.IsNullOrEmpty(f) && SamePath(f, full));
    }
}
