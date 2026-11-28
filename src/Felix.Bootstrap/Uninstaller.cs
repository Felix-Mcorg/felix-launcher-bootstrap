using System.Diagnostics;
using System.Text.Json;

namespace Felix.Bootstrap;

public sealed record UninstallReport(bool Success, List<string> Messages);

/// <summary>Désinstallation : raccourcis, installation, réglages — et, seulement si demandé, les fichiers de jeu.</summary>
public static class Uninstaller
{
    /// <summary>Les dossiers de jeu (mods, mondes) : le dossier par défaut et ceux choisis dans le launcher.</summary>
    public static List<string> FindGameDirs(InstallLayout layout)
    {
        var dirs = new List<string>();
        var defaultDir = Platform.DefaultGameDir();
        if (Directory.Exists(defaultDir))
            dirs.Add(defaultDir);

        try
        {
            if (File.Exists(layout.LauncherSettingsFile))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(layout.LauncherSettingsFile));
                if (document.RootElement.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var server in servers.EnumerateArray())
                    {
                        if (server.TryGetProperty("gameDir", out var value) && value.ValueKind == JsonValueKind.String
                            && value.GetString() is { Length: > 0 } path && Directory.Exists(path)
                            && !dirs.Any(d => Platform.SamePath(d, path)))
                            dirs.Add(path);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            BootLog.Write($"Réglages du launcher illisibles : {ex.Message}");
        }
        return dirs;
    }

    public static UninstallReport Run(InstallLayout layout, BootstrapConfig config, bool deleteGameData)
    {
        var messages = new List<string>();
        var success = true;

        // 1. Les dossiers de jeu d'abord : on a besoin de settings.json, qui est dans l'installation.
        if (deleteGameData)
        {
            foreach (var dir in FindGameDirs(layout))
            {
                if (Platform.IsDangerousFolder(dir))
                {
                    messages.Add($"Dossier ignoré par sécurité : {dir}");
                    continue;
                }
                if (DeleteTree(dir))
                    messages.Add($"Fichiers de jeu supprimés : {dir}");
                else
                {
                    messages.Add($"Impossible de tout supprimer : {dir}");
                    success = false;
                }
            }
        }

        // 2. Raccourcis
        Shortcuts.Remove(layout);
        messages.Add("Raccourcis supprimés.");

        // 3. L'installation elle-même (seulement si elle ressemble bien à une installation du launcher)
        if (!Directory.Exists(layout.Root))
        {
            messages.Add("Aucune installation à supprimer.");
        }
        else if (Platform.IsDangerousFolder(layout.Root) || !layout.LooksInstalled)
        {
            messages.Add($"Dossier non supprimé par sécurité (il ne ressemble pas à une installation) : {layout.Root}");
            success = false;
        }
        else if (DeleteTree(layout.Root))
        {
            messages.Add($"Installation supprimée : {layout.Root}");
        }
        else
        {
            // Sous Windows, le démarreur en cours d'exécution ne peut pas se supprimer lui-même.
            DeleteLater(layout.Root);
            messages.Add("Installation supprimée (la fin du nettoyage se fait dans quelques secondes).");
        }

        // 4. Les réglages du démarreur
        try
        {
            if (File.Exists(config.SettingsPath))
                File.Delete(config.SettingsPath);
            var lockFile = Path.Combine(config.ConfigDir, "bootstrap.lock");
            if (File.Exists(lockFile))
                File.Delete(lockFile);
            if (Directory.Exists(config.ConfigDir) && !Directory.EnumerateFileSystemEntries(config.ConfigDir).Any())
                Directory.Delete(config.ConfigDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BootLog.Write($"Réglages du démarreur non supprimés : {ex.Message}");
        }

        return new UninstallReport(success, messages);
    }

    /// <summary>Supprime un dossier. Retourne faux s'il en reste quelque chose (fichier en cours d'utilisation).</summary>
    public static bool DeleteTree(string path)
    {
        if (!Directory.Exists(path))
            return true;
        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BootLog.Write($"Suppression complète impossible ({ex.Message}) : nettoyage fichier par fichier.");
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList())
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // fichier verrouillé : on continue avec les autres
                }
            }
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length).ToList())
            {
                try
                {
                    Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // dossier pas vide : on continue
                }
            }
            Directory.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // il reste des éléments verrouillés
        }
        return !Directory.Exists(path);
    }

    /// <summary>Supprime un dossier quelques secondes plus tard, une fois ce programme terminé.</summary>
    public static void DeleteLater(string path)
    {
        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsWindows())
            {
                info = new ProcessStartInfo("cmd.exe")
                {
                    Arguments = $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            else
            {
                info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add("sleep 2; rm -rf \"$0\"");
                info.ArgumentList.Add(path);
            }
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            BootLog.Write($"Nettoyage différé impossible : {ex.Message}");
        }
    }
}
