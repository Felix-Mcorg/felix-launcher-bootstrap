namespace Felix.Bootstrap;

/// <summary>Questions posées dans la console (jamais appelées quand --yes est donné ou sans console).</summary>
public static class Ui
{
    public static bool Interactive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public static string Ask(string question, string defaultValue)
    {
        Console.Write($"{question} [{defaultValue}] : ");
        var line = Console.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? defaultValue : line.Trim().Trim('"');
    }

    public static bool Confirm(string question, bool defaultYes)
    {
        var hint = defaultYes ? "O/n" : "o/N";
        while (true)
        {
            Console.Write($"{question} ({hint}) : ");
            var line = Console.ReadLine();
            if (line is null || string.IsNullOrWhiteSpace(line))
                return defaultYes;
            switch (line.Trim().ToLowerInvariant())
            {
                case "o": case "oui": case "y": case "yes": return true;
                case "n": case "non": case "no": return false;
            }
        }
    }

    public static void Pause()
    {
        if (!Interactive)
            return;
        Console.WriteLine();
        Console.Write("Appuie sur Entrée pour fermer.");
        Console.ReadLine();
    }
}

/// <summary>Écoute la touche « M » pendant la mise à jour, sans ralentir le lancement.</summary>
internal sealed class KeyWatcher : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _pressed;

    public KeyWatcher()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.M)
                    {
                        _pressed = true;
                        return;
                    }
                    await Task.Delay(50);
                }
            }
            catch
            {
                // pas de clavier utilisable (console redirigée…) : on n'écoute plus
            }
        });
    }

    public bool Pressed => _pressed;

    public void Dispose() => _cts.Cancel();
}
