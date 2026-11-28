using System.Diagnostics;
using System.Text;

namespace Felix.Bootstrap;

/// <summary>
/// Raccourcis vers le démarreur installé : Windows (.lnk bureau + menu Démarrer),
/// Linux (.desktop dans le menu des applications et sur le bureau), macOS (petite application dans ~/Applications).
/// </summary>
public static class Shortcuts
{
    private static string FlagFile(InstallLayout layout) => Path.Combine(layout.Root, "shortcuts.created");

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Crée les raccourcis une seule fois : si le joueur en supprime un, on ne le recrée pas.</summary>
    public static void EnsureOnce(InstallLayout layout, string bootstrapPath)
    {
        if (!File.Exists(FlagFile(layout)))
            Create(layout, bootstrapPath);
    }

    public static bool Create(InstallLayout layout, string bootstrapPath)
    {
        if (string.IsNullOrEmpty(bootstrapPath) || !File.Exists(bootstrapPath))
            return false;

        bool ok;
        try
        {
            ok = OperatingSystem.IsWindows() ? CreateWindows(bootstrapPath)
                : OperatingSystem.IsMacOS() ? CreateMac(bootstrapPath)
                : CreateLinux(bootstrapPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            BootLog.Write($"Création des raccourcis impossible : {ex.Message}");
            return false;
        }

        if (ok)
        {
            Directory.CreateDirectory(layout.Root);
            File.WriteAllText(FlagFile(layout), DateTime.UtcNow.ToString("O"));
        }
        return ok;
    }

    public static void Remove(InstallLayout layout)
    {
        foreach (var path in AllShortcutPaths())
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                BootLog.Write($"Raccourci non supprimé ({path}) : {ex.Message}");
            }
        }

        try
        {
            if (File.Exists(FlagFile(layout)))
                File.Delete(FlagFile(layout));
        }
        catch (IOException)
        {
            // sans importance
        }
    }

    private static IEnumerable<string> AllShortcutPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Platform.DesktopDir, "Felix Launcher.lnk");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Felix Launcher.lnk");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(Home, "Applications", "Felix Launcher.app");
        }
        else
        {
            yield return Path.Combine(LinuxApplicationsDir(), "felix-launcher.desktop");
            yield return Path.Combine(Platform.DesktopDir, "Felix Launcher.desktop");
        }
    }

    // ---------------------------------------------------------------- Windows

    private static bool CreateWindows(string target)
    {
        var desktop = Path.Combine(Platform.DesktopDir, "Felix Launcher.lnk");
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Felix Launcher.lnk");

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
        return process is not null && process.WaitForExit(15_000) && process.ExitCode == 0;
    }

    private static string Escape(string text) => text.Replace("'", "''");

    // ---------------------------------------------------------------- Linux

    private static string LinuxApplicationsDir()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Home, ".local", "share") : xdg, "applications");
    }

    private static bool CreateLinux(string target)
    {
        var content = $"""
            [Desktop Entry]
            Type=Application
            Name=Felix Launcher
            Comment=Launcher Minecraft
            Exec="{target.Replace("%", "%%")}"
            Path={Path.GetDirectoryName(target)}
            Terminal=false
            Categories=Game;
            """ + "\n";

        var applications = LinuxApplicationsDir();
        Directory.CreateDirectory(applications);
        var menuEntry = Path.Combine(applications, "felix-launcher.desktop");
        File.WriteAllText(menuEntry, content);
        Platform.MakeExecutable(menuEntry);

        if (!string.IsNullOrEmpty(Platform.DesktopDir) && Directory.Exists(Platform.DesktopDir))
        {
            var desktopEntry = Path.Combine(Platform.DesktopDir, "Felix Launcher.desktop");
            File.WriteAllText(desktopEntry, content);
            Platform.MakeExecutable(desktopEntry);
        }
        return true;
    }

    // ---------------------------------------------------------------- macOS

    private static bool CreateMac(string target)
    {
        var applications = Path.Combine(Home, "Applications");
        var bundle = Path.Combine(applications, "Felix Launcher.app");
        var macOs = Path.Combine(bundle, "Contents", "MacOS");
        Directory.CreateDirectory(macOs);

        File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>CFBundleName</key><string>Felix Launcher</string>
              <key>CFBundleDisplayName</key><string>Felix Launcher</string>
              <key>CFBundleIdentifier</key><string>fr.felix.launcher</string>
              <key>CFBundleExecutable</key><string>FelixLauncher</string>
              <key>CFBundlePackageType</key><string>APPL</string>
              <key>CFBundleVersion</key><string>1</string>
            </dict>
            </plist>
            """);

        // Un petit script qui relance le démarreur : l'application s'ouvre sans fenêtre de terminal.
        var script = Path.Combine(macOs, "FelixLauncher");
        var escaped = target.Replace("\"", "\\\"");
        File.WriteAllText(script, "#!/bin/bash\nexec \"" + escaped + "\" \"$@\"\n");
        Platform.MakeExecutable(script);
        return true;
    }
}
