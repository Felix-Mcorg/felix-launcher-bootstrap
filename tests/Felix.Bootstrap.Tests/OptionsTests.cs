using static Felix.Bootstrap.Tests.TestData;

namespace Felix.Bootstrap.Tests;

public class CliTests
{
    [Fact]
    public void Parse_NoArguments_IsNormalRun() => Assert.Equal("run", Cli.Parse(Array.Empty<string>()).Command);

    [Fact]
    public void Parse_Uninstall_ReadsFlags()
    {
        var cli = Cli.Parse(new[] { "--uninstall", "--yes", "--delete-game-data", "--wait-pid", "123" });

        Assert.Equal("uninstall", cli.Command);
        Assert.True(cli.Yes);
        Assert.True(cli.DeleteGameData);
        Assert.Equal(123, cli.WaitPid);
    }

    [Fact]
    public void Parse_MoveInstall_ReadsPath()
    {
        var cli = Cli.Parse(new[] { "--move-install", "D:/Jeux/Felix", "--yes" });

        Assert.Equal("move", cli.Command);
        Assert.Equal("D:/Jeux/Felix", cli.Argument1);
    }

    [Fact]
    public void Parse_Set_ReadsKeyAndValue()
    {
        var cli = Cli.Parse(new[] { "--set", "auto-update", "off" });

        Assert.Equal("set", cli.Command);
        Assert.Equal("auto-update", cli.Argument1);
        Assert.Equal("off", cli.Argument2);
    }
}

public class SettingsTests
{
    [Fact]
    public void Save_Then_Load_KeepsChoices()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "bootstrap.json");

        BootSettingsStore.Save(path, new BootSettings { InstallRoot = "/data/felix", AutoUpdate = false, Shortcuts = false, Configured = true });
        var loaded = BootSettingsStore.Load(path);

        Assert.Equal("/data/felix", loaded.InstallRoot);
        Assert.False(loaded.AutoUpdate);
        Assert.False(loaded.Shortcuts);
        Assert.True(loaded.Configured);
    }

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        using var dir = new TempDir();

        var loaded = BootSettingsStore.Load(Path.Combine(dir.Path, "absent.json"));

        Assert.True(loaded.AutoUpdate);
        Assert.True(loaded.Shortcuts);
        Assert.False(loaded.Configured);
        Assert.Null(loaded.InstallRoot);
    }
}

public class InstallMoverTests
{
    private static InstallLayout FakeInstall(string root)
    {
        var layout = new InstallLayout(root);
        layout.EnsureCreated();
        File.WriteAllText(layout.CurrentFile, "{}");
        File.WriteAllText(Path.Combine(layout.AppDir, "marker.txt"), "x");
        return layout;
    }

    [Fact]
    public void Move_MovesInstallationAndRemembersIt()
    {
        using var dir = new TempDir();
        var config = new BootstrapConfig { ConfigDirOverride = Path.Combine(dir.Path, "cfg") };
        var old = FakeInstall(Path.Combine(dir.Path, "old"));
        var target = Path.Combine(dir.Path, "new");

        var result = InstallMover.Move(old, target, new BootSettings(), config);

        Assert.True(result.Success, result.Message);
        Assert.Equal(Path.GetFullPath(target), result.NewLayout!.Root);
        Assert.True(File.Exists(Path.Combine(target, "app", "marker.txt")));
        Assert.False(Directory.Exists(old.Root));
        Assert.Equal(Path.GetFullPath(target), BootSettingsStore.Load(config.SettingsPath).InstallRoot);
    }

    [Fact]
    public void Move_NonEmptyTarget_UsesASubfolder()
    {
        using var dir = new TempDir();
        var config = new BootstrapConfig { ConfigDirOverride = Path.Combine(dir.Path, "cfg") };
        var old = FakeInstall(Path.Combine(dir.Path, "old"));
        var target = Path.Combine(dir.Path, "occupied");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "autre.txt"), "x");

        var result = InstallMover.Move(old, target, new BootSettings(), config);

        Assert.True(result.Success, result.Message);
        Assert.Equal(Path.Combine(Path.GetFullPath(target), "FelixLauncher"), result.NewLayout!.Root);
        Assert.True(File.Exists(Path.Combine(target, "autre.txt")));
    }

    [Fact]
    public void Move_InsideOldInstallation_IsRefused()
    {
        using var dir = new TempDir();
        var config = new BootstrapConfig { ConfigDirOverride = Path.Combine(dir.Path, "cfg") };
        var old = FakeInstall(Path.Combine(dir.Path, "old"));

        var result = InstallMover.Move(old, Path.Combine(old.Root, "sous-dossier"), new BootSettings(), config);

        Assert.False(result.Success);
        Assert.True(Directory.Exists(old.Root));
    }
}

public class UninstallerTests
{
    [Fact]
    public void DeleteTree_RemovesNestedFolders()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "a");
        Directory.CreateDirectory(Path.Combine(root, "b", "c"));
        File.WriteAllText(Path.Combine(root, "b", "c", "f.txt"), "x");

        Assert.True(Uninstaller.DeleteTree(root));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void FindGameDirs_ReadsFoldersChosenInTheLauncher()
    {
        using var dir = new TempDir();
        var layout = new InstallLayout(Path.Combine(dir.Path, "install"));
        Directory.CreateDirectory(layout.Root);
        var gameDir = Path.Combine(dir.Path, "mon-jeu");
        Directory.CreateDirectory(gameDir);
        var json = "{ \"servers\": [ { \"id\": \"a\", \"gameDir\": " + System.Text.Json.JsonSerializer.Serialize(gameDir) + " } ] }";
        File.WriteAllText(layout.LauncherSettingsFile, json);

        Assert.Contains(Uninstaller.FindGameDirs(layout), d => Platform.SamePath(d, gameDir));
    }

    [Fact]
    public void RootAndHomeFoldersAreConsideredDangerous()
    {
        Assert.True(Platform.IsDangerousFolder(Path.GetPathRoot(Path.GetTempPath())!));
        Assert.True(Platform.IsDangerousFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    }
}

public class AutoUpdateTests
{
    [Fact]
    public async Task Run_AutoUpdateOff_DoesNotContactGitHub()
    {
        using var dir = new TempDir();
        var layout = new InstallLayout(dir.Path);
        var zip = MakeZip("exe 1.0.0");
        var installer = new LauncherInstaller(Config, layout, ClientFor(new() { ["https://x/1.zip"] = zip }));
        await installer.InstallAsync(
            new ReleaseInfo("v1.0.0", new Version(1, 0, 0), "https://x/1.zip", null, Sha(zip)), NoProgress);
        var offline = new ReleaseClient(Config, new HttpClient(new ThrowingHandler()), new HttpClient(new ThrowingHandler()));

        var target = await new BootstrapRunner(layout, offline, installer).RunAsync(NoProgress, checkForUpdates: false);

        Assert.False(target.Updated);
        Assert.Null(target.Notice);
        Assert.Equal("1.0.0", target.Version);
    }
}
