using System.Text;

namespace Felix.Bootstrap;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch
        {
            // console qui n'accepte pas l'UTF-8 : on garde l'encodage par défaut
        }

        try
        {
            var app = new BootstrapApp(BootstrapConfig.FromEnvironment(), Cli.Parse(args));
            return await app.RunAsync();
        }
        catch (Exception ex)
        {
            BootLog.Write($"Erreur inattendue : {ex}");
            Console.Error.WriteLine($"Erreur inattendue : {ex.Message}");
            Ui.Pause();
            return 1;
        }
    }
}
