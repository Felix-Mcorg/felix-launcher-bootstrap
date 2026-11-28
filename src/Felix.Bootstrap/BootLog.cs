namespace Felix.Bootstrap;

/// <summary>Petit journal dans logs\bootstrap.log (vide si on ne l'initialise pas, comme dans les tests).</summary>
public static class BootLog
{
    private static string? _path;

    public static void Init(InstallLayout layout)
    {
        try
        {
            Directory.CreateDirectory(layout.LogsDir);
            _path = Path.Combine(layout.LogsDir, "bootstrap.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > 512 * 1024)
                File.Delete(_path);
        }
        catch
        {
            _path = null;
        }
    }

    public static void Write(string message)
    {
        if (_path is null)
            return;
        try
        {
            File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // un journal ne doit jamais faire planter le démarreur
        }
    }
}
