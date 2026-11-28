using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace Felix.Bootstrap.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "felix-boot-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* sans importance */ }
    }
}

/// <summary>Faux serveur HTTP piloté par une fonction.</summary>
internal sealed class FuncHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(_handler(request));
}

/// <summary>Simule l'absence de connexion internet.</summary>
internal sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        throw new HttpRequestException("hors ligne");
}

internal static class TestData
{
    // Noms fixés : les tests donnent le même résultat sous Windows, Linux et macOS.
    public static readonly BootstrapConfig Config = new()
    {
        Owner = "Felix-Mcorg",
        Repository = "felix-launcher",
        ZipAssetName = "FelixLauncher-win-x64.zip",
        ExecutableName = "FelixLauncher.exe",
    };

    public static byte[] MakeZip(string exeContent)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("FelixLauncher.exe");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(exeContent);
        }
        return stream.ToArray();
    }

    public static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static HttpClient ClientFor(Dictionary<string, byte[]> files) => new(new FuncHandler(request =>
        files.TryGetValue(request.RequestUri!.ToString(), out var data)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }
            : new HttpResponseMessage(HttpStatusCode.NotFound)));

    public static IProgress<BootProgress> NoProgress { get; } = new Progress<BootProgress>(_ => { });
}
