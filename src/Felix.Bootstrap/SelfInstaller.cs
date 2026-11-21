using System.Diagnostics;
using System.Text;

namespace Felix.Bootstrap;

/// <summary>
/// Au premier lancement : copie le démarreur dans le dossier d'installation (bootstrap\FelixBootstrap.exe)
/// et crée les raccourcis (bureau et menu Démarrer) vers cette copie. Ainsi le joueur peut supprimer le
/// fichier téléchargé : le launcher continue de se mettre à jour depuis la copie installée.
/// </summary>
public static class SelfInstaller
{
    public static string InstalledPath(InstallLayout layout) =>
        Path.Combine(layout.Root, "bootstrap", OperatingSystem.IsWindows() ? "FelixBootstrap.exe" : "FelixBootstrap");

    /// <summary>Retourne le chemin du démarreur à transmettre au launcher (pour qu'il puisse le relancer).</summary>
    public static string EnsureInstalled(InstallLayout layout)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current))
            return "";

        var target = InstalledPath(layout);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!SamePath(current, target))
                File.Copy(current, target, overwrite: true); // un démarreur plus récent remplace l'ancien
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BootLog.Write($"Copie du démarreur impossible : {ex.Message}");
            return current;
        }

        if (OperatingSystem.IsWindows())
            TryCreateShortcuts(layout, target);
        return target;
    }

    /// <summary>Raccourcis créés une seule fois : si le joueur en supprime un, on ne le recrée pas.</summary>
    private static void TryCreateShortcuts(InstallLayout layout, string target)
    {
        var flag = Path.Combine(layout.Root, "shortcuts.created");
        if (File.Exists(flag))
            return;

        try
        {
            var desktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Felix Launcher.lnk");
            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Felix Launcher.lnk");

            var script = new StringBuilder();
            script.AppendLine("$w = New-Object -ComObject WScript.Shell");
            foreach (var shortcut in new[] { desktop, startMenu })
            {
                script.AppendLine($"$s = $w.CreateShortcut('{Escape(shortcut)}')");
                script.AppendLine($"$s.TargetPath = '{Escape(target)}'");
                script.AppendLine($"$s.WorkingDirectory = '{Escape(Path.GetDirectoryName(target)!)}'");
                script.AppendLine("$s.Description = 'Felix Launcher'");
                script.AppendLine($"$s.IconLocation = '{Escape(target)},0'");
                script.AppendLine("$s.Save()");
            }

            // « -EncodedCommand » évite tout problème de guillemets dans les chemins.
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
            var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded })
                info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is not null && process.WaitForExit(15_000) && process.ExitCode == 0)
                File.WriteAllText(flag, DateTime.UtcNow.ToString("O"));
            else
                BootLog.Write("Création des raccourcis : échec ou délai dépassé.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            BootLog.Write($"Création des raccourcis impossible : {ex.Message}");
        }
    }

    private static string Escape(string text) => text.Replace("'", "''");

    private static bool SamePath(string a, string b) => string.Equals(
        Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
