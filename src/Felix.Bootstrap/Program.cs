using System.Diagnostics;
using System.Text;

namespace Felix.Bootstrap;

internal static class Program
{
    // Gardé pour toute la durée du programme : empêche d'ouvrir deux démarreurs en même temps.
    private static Mutex? _singleInstance;

    private static async Task<int> Main()
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Felix.Bootstrap", out var isFirst);
        if (!isFirst)
            return 0;

        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* pas de console : sans importance */ }
        Console.Title = "Felix Launcher";
        Console.WriteLine("Felix Launcher");
        Console.WriteLine();

        var config = BootstrapConfig.FromEnvironment();
        var layout = InstallLayout.Create(config);
        BootLog.Init(layout);
        BootLog.Write("Démarrage du démarreur.");

        try
        {
            using var http = CreateHttpClient();
            using var noRedirect = CreateHttpClient(allowRedirect: false);
            var runner = new BootstrapRunner(
                layout,
                new ReleaseClient(config, http, noRedirect),
                new LauncherInstaller(config, layout, http));

            var progress = new ConsoleProgress();
            var target = await runner.RunAsync(progress);
            progress.Finish();

            if (target.Notice is not null)
                Console.WriteLine(target.Notice);
            Console.WriteLine($"Lancement du Felix Launcher {target.Version}…");
            var bootstrapper = SelfInstaller.EnsureInstalled(layout); // copie + raccourcis au premier lancement
            Launch(target, layout, bootstrapper);
            return 0;
        }
        catch (BootstrapException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            BootLog.Write($"Erreur inattendue : {ex}");
            return Fail($"Erreur inattendue : {ex.Message}\nDétails dans {layout.LogsDir}");
        }
    }

    /// <summary>
    /// Lance le launcher avec des arguments : c'est le « contrat » entre les deux programmes.
    /// --bootstrapped            lancé par le démarreur
    /// --install-root &lt;dossier&gt;     dossier d'installation (FelixLauncher)
    /// --bootstrapper &lt;chemin&gt;      programme à relancer pour mettre à jour le launcher
    /// --bootstrap-version &lt;v&gt;      version du launcher que le démarreur vient de lancer
    /// </summary>
    private static void Launch(LaunchTarget target, InstallLayout layout, string bootstrapper)
    {
        var info = new ProcessStartInfo(target.ExecutablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath)!,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--bootstrapped");
        info.ArgumentList.Add("--install-root");
        info.ArgumentList.Add(layout.Root);
        info.ArgumentList.Add("--bootstrapper");
        info.ArgumentList.Add(bootstrapper);
        info.ArgumentList.Add("--bootstrap-version");
        info.ArgumentList.Add(target.Version);

        BootLog.Write($"Lancement de {target.ExecutablePath}");
        Process.Start(info);
    }

    private static int Fail(string message)
    {
        BootLog.Write($"Échec : {message}");
        Console.WriteLine();
        Console.WriteLine(message);
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("Appuie sur Entrée pour fermer.");
            Console.ReadLine();
        }
        return 1;
    }

    private static HttpClient CreateHttpClient(bool allowRedirect = true)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowRedirect,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FelixBootstrap/1.0");
        return client;
    }
}
