namespace Felix.Bootstrap;

public sealed record MoveResult(bool Success, string Message, InstallLayout? NewLayout);

/// <summary>Déplace l'installation vers un autre dossier, sans toucher aux fichiers de jeu (mods, mondes).</summary>
public static class InstallMover
{
    /// <summary>
    /// Transforme le dossier demandé en dossier d'installation valide : si le joueur choisit un dossier qui contient déjà
    /// des fichiers (ou un dossier sensible comme Documents), on crée un sous-dossier « FelixLauncher ».
    /// </summary>
    public static string? Normalize(string requested, string appFolder, out string error)
    {
        error = "";
        string path;
        try
        {
            if (string.IsNullOrWhiteSpace(requested))
                throw new ArgumentException("vide");
            path = Path.GetFullPath(requested.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Ce chemin n'est pas valide.";
            return null;
        }

        if (Platform.IsDangerousFolder(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
            path = Path.Combine(path, appFolder);

        if (Platform.IsDangerousFolder(path))
        {
            error = "Choisis un dossier dédié (pas la racine du disque ni ton dossier personnel).";
            return null;
        }
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            error = $"Le dossier {path} n'est pas vide.";
            return null;
        }
        return path;
    }

    public static MoveResult Move(InstallLayout old, string requested, BootSettings settings, BootstrapConfig config)
    {
        var newRoot = Normalize(requested, config.AppFolderName, out var error);
        if (newRoot is null)
            return new MoveResult(false, error, null);

        if (Platform.SamePath(newRoot, old.Root))
            return new MoveResult(false, "C'est déjà le dossier d'installation.", null);
        if (Platform.IsInside(newRoot, old.Root) || Platform.IsInside(old.Root, newRoot))
            return new MoveResult(false, "Le nouveau dossier ne peut pas être à l'intérieur de l'ancien (ni l'inverse).", null);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newRoot)!);

            var moved = false;
            if (!Directory.Exists(newRoot) && Directory.Exists(old.Root))
            {
                try
                {
                    Directory.Move(old.Root, newRoot); // rapide sur le même disque
                    moved = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    BootLog.Write($"Déplacement direct impossible ({ex.Message}) : copie.");
                }
            }

            if (!moved)
            {
                if (Directory.Exists(old.Root))
                    CopyDirectory(old.Root, newRoot);
                else
                    Directory.CreateDirectory(newRoot);
            }

            settings.InstallRoot = newRoot;
            BootSettingsStore.Save(config.SettingsPath, settings);

            if (!moved && Directory.Exists(old.Root) && !Platform.IsDangerousFolder(old.Root) && old.LooksInstalled
                && !Uninstaller.DeleteTree(old.Root))
                Uninstaller.DeleteLater(old.Root); // le démarreur en cours d'exécution est dans l'ancien dossier
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MoveResult(false, $"Déplacement impossible : {ex.Message}", null);
        }

        return new MoveResult(true, $"Installation déplacée vers {newRoot}", new InstallLayout(newRoot));
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            if (Path.GetFileName(dir).Equals("tmp", StringComparison.OrdinalIgnoreCase))
                continue; // téléchargements en cours : inutiles
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
