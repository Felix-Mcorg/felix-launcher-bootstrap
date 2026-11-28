namespace Felix.Bootstrap;

/// <summary>Numéro de version du démarreur : fixé automatiquement à la compilation (-p:Version=…).</summary>
public static class AppInfo
{
    public static string Version
    {
        get
        {
            try
            {
                var version = typeof(AppInfo).Assembly.GetName().Version;
                return version is null || (version.Major == 0 && version.Minor == 0 && version.Build <= 0)
                    ? "dev"
                    : version.ToString(3);
            }
            catch
            {
                return "dev";
            }
        }
    }
}

/// <summary>Les commandes du démarreur. Sans argument : met à jour puis lance le launcher.</summary>
public sealed class Cli
{
    public string Command { get; private set; } = "run";
    public string? Argument1 { get; private set; }
    public string? Argument2 { get; private set; }
    public bool Yes { get; private set; }
    public bool NoLaunch { get; private set; }
    public bool ForceUpdate { get; private set; }
    public bool DeleteGameData { get; private set; }
    public int? WaitPid { get; private set; }

    public static Cli Parse(string[] args)
    {
        var cli = new Cli();
        var i = 0;
        string? Next() => i + 1 < args.Length ? args[++i] : null;

        for (; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--menu": cli.Command = "menu"; break;
                case "--uninstall": cli.Command = "uninstall"; break;
                case "--move-install": cli.Command = "move"; cli.Argument1 = Next(); break;
                case "--set": cli.Command = "set"; cli.Argument1 = Next(); cli.Argument2 = Next(); break;
                case "--shortcuts": cli.Command = "shortcuts"; cli.Argument1 = Next(); break;
                case "--version": case "-v": cli.Command = "version"; break;
                case "--help": case "-h": case "/?": cli.Command = "help"; break;
                case "--yes": case "-y": cli.Yes = true; break;
                case "--no-launch": cli.NoLaunch = true; break;
                case "--update": cli.ForceUpdate = true; break;
                case "--delete-game-data": cli.DeleteGameData = true; break;
                case "--wait-pid":
                    if (int.TryParse(Next(), out var pid))
                        cli.WaitPid = pid;
                    break;
            }
        }
        return cli;
    }

    public const string HelpText = """
        Felix Launcher - démarreur

        Sans argument : met à jour le launcher (si les mises à jour automatiques sont activées) puis le lance.

          --menu                     Ouvre les options d'installation
          --update                   Cherche une mise à jour même si les mises à jour automatiques sont désactivées
          --set auto-update on|off   Active ou désactive les mises à jour automatiques
          --set shortcuts on|off     Crée ou supprime les raccourcis
          --shortcuts create|remove  Crée ou supprime les raccourcis
          --move-install <dossier>   Déplace l'installation vers un autre dossier
          --uninstall                Désinstalle (ajoute --delete-game-data pour effacer aussi mods et mondes)
          --yes                      Ne pose aucune question
          --no-launch                Ne lance pas le launcher à la fin
          --wait-pid <n>             Attend la fin du processus n avant d'agir
          --version, --help
        """;
}
