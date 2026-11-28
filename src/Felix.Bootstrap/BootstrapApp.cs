using System.Diagnostics;

namespace Felix.Bootstrap;

internal enum MenuResult { Launch, Quit }

/// <summary>Le déroulé du démarreur : premier lancement, mise à jour + lancement, menu d'options, déplacement, désinstallation.</summary>
internal sealed class BootstrapApp
{
    private readonly BootstrapConfig _config;
    private readonly Cli _cli;
    private BootSettings _settings;
    private InstallLayout _layout;

    public BootstrapApp(BootstrapConfig config, Cli cli)
    {
        _config = config;
        _cli = cli;
        _settings = BootSettingsStore.Load(config.SettingsPath);
        _layout = InstallLayout.Create(config, _settings);
    }

    private bool Interactive => !_cli.Yes && Ui.Interactive;

    public async Task<int> RunAsync()
    {
        switch (_cli.Command)
        {
            case "version":
                Console.WriteLine($"Felix Launcher - démarreur {AppInfo.Version} ({Platform.Rid})");
                return 0;
            case "help":
                Console.WriteLine(Cli.HelpText);
                return 0;
        }

        BootLog.Init(_layout);

        switch (_cli.Command)
        {
            case "set": return SetOption();
            case "shortcuts": return ShortcutsCommand();
            case "move": return await MoveCommandAsync();
            case "uninstall": return await UninstallCommandAsync();
        }

        // « run » et « menu » : une seule instance à la fois (verrou dans le dossier des réglages, hors de l'installation).
        using var instanceLock = TryAcquireLock();
        if (instanceLock is null)
        {
            Console.WriteLine("Le démarreur est déjà ouvert.");
            return 0;
        }

        return _cli.Command == "menu" ? await MenuCommandAsync() : await RunNormalAsync();
    }

    // ---------------------------------------------------------------- lancement normal

    private async Task<int> RunNormalAsync()
    {
        WaitForProcess();

        if (!_settings.Configured)
        {
            if (Interactive)
                FirstRunWizard();
            else
            {
                _settings.Configured = true;
                SaveSettings();
            }
            BootLog.Init(_layout);
        }

        SetTitle();
        Console.WriteLine($"Felix Launcher · démarreur {AppInfo.Version}");
        using var keys = Interactive ? new KeyWatcher() : null;
        if (Interactive)
            Console.WriteLine("(Appuie sur M pour ouvrir les options d'installation)");

        var progress = new ConsoleProgress();
        LaunchTarget target;
        try
        {
            target = await CreateRunner().RunAsync(progress, checkForUpdates: _settings.AutoUpdate || _cli.ForceUpdate);
            progress.Finish();
        }
        catch (BootstrapException ex)
        {
            progress.Finish();
            return Fail(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            progress.Finish();
            BootLog.Write(ex.ToString());
            return Fail($"Erreur : {ex.Message}");
        }

        if (target.Notice is not null)
            Console.WriteLine(target.Notice);

        var bootstrapperPath = SelfInstaller.EnsureInstalled(_layout);
        if (_settings.Shortcuts)
            Shortcuts.EnsureOnce(_layout, bootstrapperPath);

        if (keys?.Pressed == true)
        {
            if (await MenuLoopAsync() == MenuResult.Quit)
                return 0;
            var refreshed = CurrentTarget();
            if (refreshed is null)
                return Fail("Le launcher n'est pas installé.");
            target = refreshed;
            bootstrapperPath = SelfInstaller.EnsureInstalled(_layout);
        }

        return _cli.NoLaunch ? 0 : Launch(target, bootstrapperPath);
    }

    private BootstrapRunner CreateRunner()
    {
        _layout.EnsureCreated();
        var http = CreateHttpClient(allowRedirects: true);
        var noRedirect = CreateHttpClient(allowRedirects: false);
        return new BootstrapRunner(_layout, new ReleaseClient(_config, http, noRedirect), new LauncherInstaller(_config, _layout, http));
    }

    private static HttpClient CreateHttpClient(bool allowRedirects)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowRedirects,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FelixBootstrap/{AppInfo.Version}");
        return client;
    }

    private LaunchTarget? CurrentTarget()
    {
        var current = CurrentStore.Read(_layout);
        var exe = current is null ? null : CurrentStore.ResolveExecutable(_layout, current);
        return exe is null ? null : new LaunchTarget(exe, current!.Version, Updated: false, Notice: null);
    }

    private int Launch(LaunchTarget target, string bootstrapperPath)
    {
        var arguments = new List<string>
        {
            "--bootstrapped",
            "--install-root", _layout.Root,
            "--bootstrapper", bootstrapperPath,
            "--bootstrap-version", target.Version,
            "--bootstrap-config", _config.SettingsPath,
        };
        var workDir = Path.GetDirectoryName(target.ExecutablePath)!;

        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsWindows())
            {
                info = new ProcessStartInfo(target.ExecutablePath) { WorkingDirectory = workDir, UseShellExecute = false };
                foreach (var argument in arguments)
                    info.ArgumentList.Add(argument);
            }
            else
            {
                // Linux/macOS : « nohup … & » détache le launcher du terminal (le fermer ne ferme pas le launcher).
                info = new ProcessStartInfo("/bin/sh") { WorkingDirectory = workDir, UseShellExecute = false };
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add("nohup \"$@\" >/dev/null 2>&1 &");
                info.ArgumentList.Add("felix");
                info.ArgumentList.Add(target.ExecutablePath);
                foreach (var argument in arguments)
                    info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process is null)
                return Fail("Impossible de lancer le launcher.");
            BootLog.Write($"Launcher {target.Version} lancé.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            BootLog.Write(ex.ToString());
            return Fail($"Impossible de lancer le launcher : {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- premier lancement

    private void FirstRunWizard()
    {
        SetTitle();
        Console.WriteLine("=== Felix Launcher : installation ===");
        Console.WriteLine("Quelques choix avant de commencer (Entrée = valeur proposée).");
        Console.WriteLine();

        while (true)
        {
            var answer = Ui.Ask("Dossier d'installation du launcher", _layout.Root);
            var chosen = InstallMover.Normalize(answer, _config.AppFolderName, out var error);
            if (chosen is null)
            {
                Console.WriteLine(error);
                continue;
            }

            _settings.InstallRoot = Platform.SamePath(chosen, Platform.DefaultInstallRoot(_config.AppFolderName)) ? null : chosen;
            break;
        }

        _settings.Shortcuts = Ui.Confirm("Créer des raccourcis (bureau et menu) ?", true);
        _settings.AutoUpdate = Ui.Confirm("Mettre à jour le launcher automatiquement à chaque ouverture ?", true);
        _settings.Configured = true;
        SaveSettings();
        _layout = InstallLayout.Create(_config, _settings);
        Console.WriteLine();
        Console.WriteLine("Tu pourras tout modifier plus tard : lance le démarreur et appuie sur M, ou ouvre Paramètres > Installation dans le launcher.");
        Console.WriteLine();
    }

    // ---------------------------------------------------------------- menu d'options

    private async Task<int> MenuCommandAsync()
    {
        if (!Interactive)
            return Fail("Le menu demande une console. Lance le démarreur depuis un terminal.");
        var result = await MenuLoopAsync();
        if (result == MenuResult.Quit)
            return 0;

        var target = CurrentTarget();
        if (target is null)
            return Fail("Le launcher n'est pas installé.");
        return Launch(target, SelfInstaller.EnsureInstalled(_layout));
    }

    private async Task<MenuResult> MenuLoopAsync()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"=== Felix Launcher · options d'installation (démarreur {AppInfo.Version}) ===");
            Console.WriteLine($"  Dossier d'installation    : {_layout.Root}");
            Console.WriteLine($"  Mises à jour automatiques : {(_settings.AutoUpdate ? "oui" : "non")}");
            Console.WriteLine($"  Raccourcis                : {(_settings.Shortcuts ? "oui" : "non")}");
            Console.WriteLine();
            Console.WriteLine("  1) Lancer Felix Launcher");
            Console.WriteLine("  2) Changer le dossier d'installation");
            Console.WriteLine($"  3) {(_settings.AutoUpdate ? "Désactiver" : "Activer")} les mises à jour automatiques");
            Console.WriteLine($"  4) {(_settings.Shortcuts ? "Supprimer" : "Créer")} les raccourcis");
            Console.WriteLine("  5) Réparer (retélécharger le launcher)");
            Console.WriteLine("  6) Désinstaller");
            Console.WriteLine("  0) Quitter");

            switch (Ui.Ask("Ton choix", "1"))
            {
                case "1":
                    return MenuResult.Launch;
                case "2":
                    var folder = Ui.Ask("Nouveau dossier d'installation", _layout.Root);
                    ApplyMove(folder);
                    break;
                case "3":
                    _settings.AutoUpdate = !_settings.AutoUpdate;
                    SaveSettings();
                    break;
                case "4":
                    ToggleShortcuts(!_settings.Shortcuts);
                    break;
                case "5":
                    await RepairAsync();
                    break;
                case "6":
                    if (await UninstallAsync(deleteGameData: null, confirm: true))
                        return MenuResult.Quit;
                    break;
                case "0":
                    return MenuResult.Quit;
            }
        }
    }

    private async Task RepairAsync()
    {
        try
        {
            if (File.Exists(_layout.CurrentFile))
                File.Delete(_layout.CurrentFile);
            Uninstaller.DeleteTree(_layout.AppDir);
            var progress = new ConsoleProgress();
            await CreateRunner().RunAsync(progress, checkForUpdates: true);
            progress.Finish();
            Console.WriteLine("Le launcher a été réinstallé.");
        }
        catch (Exception ex) when (ex is BootstrapException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Réparation impossible : {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- commandes sans interface

    private int SetOption()
    {
        var value = _cli.Argument2?.ToLowerInvariant();
        bool enable;
        if (value is "on" or "oui" or "true" or "1")
            enable = true;
        else if (value is "off" or "non" or "false" or "0")
            enable = false;
        else
            return Fail("Valeur attendue : on ou off.");

        switch (_cli.Argument1?.ToLowerInvariant())
        {
            case "auto-update":
                _settings.AutoUpdate = enable;
                SaveSettings();
                return 0;
            case "shortcuts":
                ToggleShortcuts(enable);
                return 0;
            default:
                return Fail("Option inconnue. Options : auto-update, shortcuts.");
        }
    }

    private int ShortcutsCommand()
    {
        switch (_cli.Argument1?.ToLowerInvariant())
        {
            case "create":
                ToggleShortcuts(true);
                return 0;
            case "remove":
                ToggleShortcuts(false);
                return 0;
            default:
                return Fail("Valeur attendue : create ou remove.");
        }
    }

    private void ToggleShortcuts(bool enable)
    {
        _settings.Shortcuts = enable;
        SaveSettings();
        if (enable)
        {
            var path = SelfInstaller.EnsureInstalled(_layout);
            Console.WriteLine(Shortcuts.Create(_layout, path) ? "Raccourcis créés." : "Impossible de créer les raccourcis.");
        }
        else
        {
            Shortcuts.Remove(_layout);
            Console.WriteLine("Raccourcis supprimés.");
        }
    }

    private async Task<int> MoveCommandAsync()
    {
        WaitForProcess();
        if (!ApplyMove(_cli.Argument1 ?? ""))
            return 1;
        if (_cli.NoLaunch)
            return 0;

        var target = CurrentTarget();
        if (target is null)
            return Fail("Le launcher n'est pas installé.");
        await Task.CompletedTask;
        return Launch(target, SelfInstaller.EnsureInstalled(_layout));
    }

    /// <summary>Déplace l'installation et recrée les raccourcis vers le nouvel emplacement.</summary>
    private bool ApplyMove(string requested)
    {
        var result = InstallMover.Move(_layout, requested, _settings, _config);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Message);
            BootLog.Write(result.Message);
            return false;
        }

        _layout = result.NewLayout!;
        BootLog.Init(_layout);
        BootLog.Write(result.Message);
        Console.WriteLine(result.Message);

        var path = SelfInstaller.EnsureInstalled(_layout);
        if (_settings.Shortcuts)
            Shortcuts.Create(_layout, path);
        return true;
    }

    private async Task<int> UninstallCommandAsync()
    {
        WaitForProcess();
        var done = await UninstallAsync(_cli.DeleteGameData ? true : null, confirm: Interactive);
        return done ? 0 : 1;
    }

    /// <param name="deleteGameData">Vrai/faux = choix déjà fait ; null = on demande au joueur.</param>
    private async Task<bool> UninstallAsync(bool? deleteGameData, bool confirm)
    {
        var gameDirs = Uninstaller.FindGameDirs(_layout);
        var deleteGame = deleteGameData ?? false;

        if (confirm)
        {
            Console.WriteLine();
            Console.WriteLine("Désinstaller supprimera le launcher, le démarreur et les raccourcis :");
            Console.WriteLine($"  {_layout.Root}");
            if (!Ui.Confirm("Continuer ?", false))
                return false;

            if (deleteGameData is null && gameDirs.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Tes fichiers de jeu (mods, mondes, configurations) se trouvent ici :");
                foreach (var dir in gameDirs)
                    Console.WriteLine($"  {dir}");
                Console.WriteLine("Par défaut ils sont CONSERVÉS.");
                if (Ui.Confirm("Les supprimer aussi ? (mondes compris, c'est définitif)", false))
                {
                    Console.Write("Tape SUPPRIMER pour confirmer : ");
                    deleteGame = string.Equals(Console.ReadLine()?.Trim(), "SUPPRIMER", StringComparison.Ordinal);
                    if (!deleteGame)
                        Console.WriteLine("Confirmation incorrecte : les fichiers de jeu sont conservés.");
                }
            }
        }

        var report = Uninstaller.Run(_layout, _config, deleteGame);
        foreach (var message in report.Messages)
            Console.WriteLine(message);
        Console.WriteLine(report.Success ? "Felix Launcher est désinstallé." : "La désinstallation est incomplète (voir ci-dessus).");
        await Task.CompletedTask;
        return report.Success;
    }

    // ---------------------------------------------------------------- outils

    private void SaveSettings()
    {
        try
        {
            BootSettingsStore.Save(_config.SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BootLog.Write($"Réglages non enregistrés : {ex.Message}");
        }
    }

    /// <summary>Attend la fin du launcher qui nous a appelés (il ne doit plus utiliser les fichiers qu'on va modifier).</summary>
    private void WaitForProcess()
    {
        if (_cli.WaitPid is not { } pid)
            return;
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(20_000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // le processus est déjà terminé
        }
    }

    private FileStream? TryAcquireLock()
    {
        try
        {
            Directory.CreateDirectory(_config.ConfigDir);
            return new FileStream(Path.Combine(_config.ConfigDir, "bootstrap.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null; // un autre démarreur tourne déjà
        }
        catch (UnauthorizedAccessException)
        {
            return new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose); // pas de verrou possible : on continue quand même
        }
    }

    private static void SetTitle()
    {
        try
        {
            Console.Title = $"Felix Launcher {AppInfo.Version}";
        }
        catch
        {
            // titre non supporté dans ce terminal
        }
    }

    private int Fail(string message)
    {
        BootLog.Write($"ERREUR : {message}");
        Console.Error.WriteLine(message);
        Ui.Pause();
        return 1;
    }
}
