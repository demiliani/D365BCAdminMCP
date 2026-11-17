using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D365BCAdminMCP.Tests;

// The service uses a static HttpClient and token cache, so tests touching them must not run in parallel.
[CollectionDefinition(Name, DisableParallelization = true)]
public class HttpCollection
{
    public const string Name = "Http";
}

public record DependencySpec(Guid Id, string Name, string Version, string Publisher = "Contoso");

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("d365bcmcp-tests-").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public static class AppPackageBuilder
{
    private const int NavxHeaderLength = 40;

    public static string WriteAppJsonPackage(string filePath, Guid id, string name, string version, params DependencySpec[] dependencies)
    {
        var appJson = JsonSerializer.Serialize(new
        {
            id,
            name,
            publisher = "Contoso",
            version,
            dependencies = dependencies.Select(d => new { id = d.Id, name = d.Name, publisher = d.Publisher, version = d.Version })
        });
        return WritePackage(filePath, ("app.json", appJson));
    }

    public static string WriteManifestPackage(string filePath, Guid id, string name, string version, params DependencySpec[] dependencies)
    {
        var deps = string.Concat(dependencies.Select(d =>
            $"<Dependency Id=\"{d.Id}\" Name=\"{d.Name}\" Publisher=\"{d.Publisher}\" MinVersion=\"{d.Version}\" />"));
        var manifest =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Package xmlns=\"http://schemas.microsoft.com/navx/2015/manifest\">" +
            $"<App Id=\"{id}\" Name=\"{name}\" Publisher=\"Contoso\" Version=\"{version}\" />" +
            $"<Dependencies>{deps}</Dependencies>" +
            "</Package>";
        return WritePackage(filePath, ("NavxManifest.xml", manifest));
    }

    public static string WritePackage(string filePath, params (string Name, string Content)[] entries)
    {
        using var zipBuffer = new MemoryStream();
        using (var archive = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        var header = new byte[NavxHeaderLength];
        Encoding.ASCII.GetBytes("NAVX").CopyTo(header, 0);
        BitConverter.GetBytes((uint)NavxHeaderLength).CopyTo(header, 4);

        File.WriteAllBytes(filePath, header.Concat(zipBuffer.ToArray()).ToArray());
        return filePath;
    }
}

public record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body, string? ContentType, string? IfMatch = null);

public sealed class FakeAdminApi : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, Regex Path, Func<RecordedRequest, HttpResponseMessage> Respond)> _routes = new();

    public List<RecordedRequest> Requests { get; } = new();

    public FakeAdminApi On(HttpMethod method, string pathPattern, HttpStatusCode status, object? body = null) =>
        On(method, pathPattern, _ => Json(status, body));

    public FakeAdminApi On(HttpMethod method, string pathPattern, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _routes.Insert(0, (method, new Regex(pathPattern, RegexOptions.IgnoreCase), respond));
        return this;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object? body)
    {
        var response = new HttpResponseMessage(status);
        if (body != null)
        {
            response.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Content?.Headers.ContentType?.MediaType,
            request.Headers.TryGetValues("If-Match", out var ifMatch) ? string.Join(",", ifMatch) : null);
        Requests.Add(recorded);

        var path = request.RequestUri!.AbsolutePath;
        foreach (var route in _routes)
        {
            if (route.Method == request.Method && route.Path.IsMatch(path))
            {
                return route.Respond(recorded);
            }
        }

        return Json(HttpStatusCode.NotFound, new { code = "RouteNotMocked", message = $"{request.Method} {path}" });
    }
}

public abstract class HttpTestBase
{
    protected static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    protected const string Token = "test-token";
    protected readonly FakeAdminApi Api = new();

    protected HttpTestBase()
    {
        D365BCAdminService.SetHttpMessageHandlerForTesting(Api);
        D365BCAdminService.SetCachedTokenForTesting(TenantId, Token);
        D365BCAdminService.RetryDelay = (delay, _) =>
        {
            RetryDelays.Add(delay);
            return Task.CompletedTask;
        };
    }

    protected List<TimeSpan> RetryDelays { get; } = new();

    protected static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
