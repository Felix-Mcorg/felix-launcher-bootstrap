namespace Felix.Bootstrap;

/// <summary>
/// Copie le démarreur dans le dossier d'installation (bootstrap/FelixBootstrap) : les raccourcis visent cette copie.
/// Le joueur peut donc supprimer le fichier téléchargé, le launcher continue de se mettre à jour.
/// </summary>
public static class SelfInstaller
{
    public static string InstalledPath(InstallLayout layout) => Path.Combine(layout.BootstrapDir, Platform.BootstrapExecutable);

    /// <summary>Retourne le chemin du démarreur à transmettre au launcher (pour qu'il puisse le relancer).</summary>
    public static string EnsureInstalled(InstallLayout layout)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current))
            return "";

        var target = InstalledPath(layout);
        try
        {
            Directory.CreateDirectory(layout.BootstrapDir);
            if (!Platform.SamePath(current, target))
                File.Copy(current, target, overwrite: true); // un démarreur plus récent remplace l'ancien
            Platform.MakeExecutable(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BootLog.Write($"Copie du démarreur impossible : {ex.Message}");
            return current;
        }
        return target;
    }
}
