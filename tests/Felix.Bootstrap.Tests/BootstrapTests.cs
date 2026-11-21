using System.Net;
using System.Text;
using System.Text.Json;
using static Felix.Bootstrap.Tests.TestData;

namespace Felix.Bootstrap.Tests;

public class VersionHelperTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3")]
    [InlineData("2", "2.0")]
    public void TryParse_ReadsVersions(string text, string expected)
    {
        Assert.True(VersionHelper.TryParse(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Fact]
    public void TryParse_Garbage_ReturnsFalse() => Assert.False(VersionHelper.TryParse("abc", out _));
}

public class ReleaseClientTests
{
    [Fact]
    public void ParseRelease_FindsZipHashFileAndDigest()
    {
        using var document = JsonDocument.Parse("""
            {
              "tag_name": "v1.4.0",
              "assets": [
                { "name": "FelixLauncher-win-x64.zip", "browser_download_url": "https://x/z.zip", "digest": "sha256:ABCDEF" },
                { "name": "FelixLauncher-win-x64.zip.sha256", "browser_download_url": "https://x/z.sha256" }
              ]
            }
            """);

        var release = ReleaseClient.ParseRelease(document.RootElement, Config);

        Assert.Equal(new Version(1, 4, 0), release.Version);
        Assert.Equal("https://x/z.zip", release.ZipUrl);
        Assert.Equal("https://x/z.sha256", release.Sha256Url);
        Assert.Equal("abcdef", release.Sha256Inline);
    }

    [Fact]
    public void ParseRelease_MissingZip_Throws()
    {
        using var document = JsonDocument.Parse("""{ "tag_name": "v1.0.0", "assets": [] }""");
        Assert.Throws<BootstrapException>(() => ReleaseClient.ParseRelease(document.RootElement, Config));
    }

    [Fact]
    public async Task GetLatest_ApiFails_FallsBackToRedirect()
    {
        var api = new HttpClient(new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var redirect = new HttpClient(new FuncHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("/Felix-Mcorg/felix-launcher/releases/tag/v1.4.0", UriKind.Relative);
            return response;
        }));

        var release = await new ReleaseClient(Config, api, redirect).GetLatestAsync();

        Assert.Equal("v1.4.0", release.Tag);
        Assert.Equal(
            "https://github.com/Felix-Mcorg/felix-launcher/releases/download/v1.4.0/FelixLauncher-win-x64.zip",
            release.ZipUrl);
    }
}

public class LauncherInstallerTests
{
    [Fact]
    public async Task Install_KeepsOnlyCurrentAndPreviousVersion()
    {
        using var dir = new TempDir();
        var layout = new InstallLayout(dir.Path);
        var files = new Dictionary<string, byte[]>();
        var versions = new[] { "1.0.0", "1.1.0", "1.2.0" };
        foreach (var v in versions)
            files[$"https://x/{v}.zip"] = MakeZip("exe " + v);
        var installer = new LauncherInstaller(Config, layout, ClientFor(files));

        foreach (var v in versions)
        {
            var url = $"https://x/{v}.zip";
            await installer.InstallAsync(new ReleaseInfo("v" + v, Version.Parse(v), url, null, Sha(files[url])), NoProgress);
        }

        var current = CurrentStore.Read(layout);
        Assert.NotNull(current);
        Assert.Equal("1.2.0", current!.Version);
        Assert.Equal("1.1.0", current.Previous);
        Assert.NotNull(CurrentStore.ResolveExecutable(layout, current));

        var folders = Directory.GetDirectories(layout.AppDir).Select(d => Path.GetFileName(d)).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "1.1.0", "1.2.0" }, folders);
    }

    [Fact]
    public async Task Install_WrongHash_ThrowsAndInstallsNothing()
    {
        using var dir = new TempDir();
        var layout = new InstallLayout(dir.Path);
        var files = new Dictionary<string, byte[]> { ["https://x/1.zip"] = MakeZip("exe") };
        var installer = new LauncherInstaller(Config, layout, ClientFor(files));
        var release = new ReleaseInfo("v1.0.0", new Version(1, 0, 0), "https://x/1.zip", null, new string('0', 64));

        await Assert.ThrowsAsync<BootstrapException>(() => installer.InstallAsync(release, NoProgress));

        Assert.Null(CurrentStore.Read(layout));
        Assert.Empty(Directory.GetDirectories(layout.AppDir));
    }

    [Fact]
    public async Task Install_HashFromSha256File_IsAccepted()
    {
        using var dir = new TempDir();
        var layout = new InstallLayout(dir.Path);
        var zip = MakeZip("exe");
        var files = new Dictionary<string, byte[]>
        {
            ["https://x/1.zip"] = zip,
            ["https://x/1.zip.sha256"] = Encoding.UTF8.GetBytes(Sha(zip) + "  FelixLauncher-win-x64.zip\n"),
        };
        var installer = new LauncherInstaller(Config, layout, ClientFor(files));
        var release = new ReleaseInfo("v1.0.0", new Version(1, 0, 0), "https://x/1.zip", "https://x/1.zip.sha256", null);

        var exe = await installer.InstallAsync(release, NoProgress);

        Assert.True(File.Exists(exe));
    }
}

public class BootstrapRunnerTests
{
    private static async Task<(InstallLayout Layout, LauncherInstaller Installer)> WithInstalled100Async(TempDir dir)
    {
        var layout = new InstallLayout(dir.Path);
        var zip = MakeZip("exe 1.0.0");
        var installer = new LauncherInstaller(Config, layout, ClientFor(new() { ["https://x/1.zip"] = zip }));
        await installer.InstallAsync(
            new ReleaseInfo("v1.0.0", new Version(1, 0, 0), "https://x/1.zip", null, Sha(zip)), NoProgress);
        return (layout, installer);
    }

    [Fact]
    public async Task Run_Offline_LaunchesInstalledVersion()
    {
        using var dir = new TempDir();
        var (layout, installer) = await WithInstalled100Async(dir);
        var releases = new ReleaseClient(Config, new HttpClient(new ThrowingHandler()), new HttpClient(new ThrowingHandler()));

        var target = await new BootstrapRunner(layout, releases, installer).RunAsync(NoProgress);

        Assert.False(target.Updated);
        Assert.NotNull(target.Notice);
        Assert.True(File.Exists(target.ExecutablePath));
    }

    [Fact]
    public async Task Run_NothingInstalled_Offline_Throws()
    {
        using var dir = new TempDir();
        var layout = new InstallLayout(dir.Path);
        var offline = new HttpClient(new ThrowingHandler());
        var releases = new ReleaseClient(Config, offline, new HttpClient(new ThrowingHandler()));
        var installer = new LauncherInstaller(Config, layout, offline);

        await Assert.ThrowsAsync<BootstrapException>(() =>
            new BootstrapRunner(layout, releases, installer).RunAsync(NoProgress));
    }

    [Fact]
    public async Task Run_NewerRelease_InstallsIt()
    {
        using var dir = new TempDir();
        var (layout, _) = await WithInstalled100Async(dir);

        var newZip = MakeZip("exe 1.1.0");
        var releaseJson = $$"""
{ "tag_name": "v1.1.0", "assets": [ { "name": "FelixLauncher-win-x64.zip", "browser_download_url": "https://x/new.zip", "digest": "sha256:{{Sha(newZip)}}" } ] }
""";
        var online = new HttpClient(new FuncHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.github.com/"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releaseJson) };
            if (url == "https://x/new.zip")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(newZip) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var releases = new ReleaseClient(Config, online, new HttpClient(new ThrowingHandler()));
        var installer = new LauncherInstaller(Config, layout, online);

        var target = await new BootstrapRunner(layout, releases, installer).RunAsync(NoProgress);

        Assert.True(target.Updated);
        Assert.Equal("1.1.0", target.Version);
        Assert.Equal("1.1.0", CurrentStore.Read(layout)!.Version);
    }
}
