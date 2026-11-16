using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace D365BCAdminMCP;

public class PteAppDependency
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Publisher { get; set; }
    public string? Version { get; set; }
}

public class PteAppInfo
{
    public string FilePath { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Publisher { get; set; }
    public string? Version { get; set; }
    public string? MetadataSource { get; set; }
    public List<PteAppDependency> Dependencies { get; set; } = new();
}

public class PteDependencyOrder
{
    public List<PteAppInfo> Ordered { get; set; } = new();
    public List<PteAppInfo> Cyclic { get; set; } = new();
}

public static class PtePackageReader
{
    // .app files start with a "NAVX" header whose length is stored right after the magic; the zip archive follows it.
    private static readonly byte[] NavxMagic = "NAVX"u8.ToArray();

    public static PteAppInfo ReadAppInfo(string appFilePath)
    {
        using var zipStream = OpenPackageZip(appFilePath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var appJsonEntry = FindEntry(archive, "app.json");
        if (appJsonEntry != null)
        {
            var info = ParseAppJson(appJsonEntry);
            info.FilePath = appFilePath;
            return info;
        }

        var manifestEntry = FindEntry(archive, "NavxManifest.xml");
        if (manifestEntry != null)
        {
            var info = ParseNavxManifest(manifestEntry);
            info.FilePath = appFilePath;
            return info;
        }

        throw new InvalidDataException("Package contains neither app.json nor NavxManifest.xml (runtime packages are not supported for dependency ordering)");
    }

    // Topological sort: dependencies that are part of the same set come first; external dependencies are ignored.
    // Ties are broken by file name so the order is deterministic.
    public static PteDependencyOrder SortByDependencies(IEnumerable<PteAppInfo> apps)
    {
        var appList = apps.OrderBy(a => Path.GetFileName(a.FilePath), StringComparer.OrdinalIgnoreCase).ToList();
        var byId = appList.ToDictionary(a => a.Id);
        var remainingDeps = appList.ToDictionary(
            a => a.Id,
            a => a.Dependencies.Select(d => d.Id).Where(id => id != a.Id && byId.ContainsKey(id)).ToHashSet());

        var result = new PteDependencyOrder();
        var pending = new List<PteAppInfo>(appList);

        while (pending.Count > 0)
        {
            var ready = pending.FirstOrDefault(a => remainingDeps[a.Id].Count == 0);
            if (ready == null)
            {
                result.Cyclic.AddRange(pending);
                break;
            }

            result.Ordered.Add(ready);
            pending.Remove(ready);
            foreach (var deps in remainingDeps.Values)
            {
                deps.Remove(ready.Id);
            }
        }

        return result;
    }

    private static Stream OpenPackageZip(string appFilePath)
    {
        var bytes = File.ReadAllBytes(appFilePath);
        var offset = 0;

        if (bytes.Length >= 8 && bytes.AsSpan(0, 4).SequenceEqual(NavxMagic))
        {
            offset = (int)BitConverter.ToUInt32(bytes, 4);
            if (offset <= 0 || offset >= bytes.Length)
            {
                throw new InvalidDataException("Invalid NAVX header length");
            }
        }

        return new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name) =>
        archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));

    private static PteAppInfo ParseAppJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var info = new PteAppInfo
        {
            Id = Guid.Parse(GetString(root, "id") ?? throw new InvalidDataException("app.json has no id")),
            Name = GetString(root, "name"),
            Publisher = GetString(root, "publisher"),
            Version = GetString(root, "version"),
            MetadataSource = "app.json"
        };

        if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
        {
            foreach (var dep in deps.EnumerateArray())
            {
                // Older app.json schemas use "appId" instead of "id".
                var depId = GetString(dep, "id") ?? GetString(dep, "appId");
                if (Guid.TryParse(depId, out var parsedId))
                {
                    info.Dependencies.Add(new PteAppDependency
                    {
                        Id = parsedId,
                        Name = GetString(dep, "name"),
                        Publisher = GetString(dep, "publisher"),
                        Version = GetString(dep, "version")
                    });
                }
            }
        }

        return info;
    }

    private static PteAppInfo ParseNavxManifest(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var app = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "App")
            ?? throw new InvalidDataException("NavxManifest.xml has no App element");

        var info = new PteAppInfo
        {
            Id = Guid.Parse(app.Attribute("Id")?.Value ?? throw new InvalidDataException("NavxManifest.xml has no App Id")),
            Name = app.Attribute("Name")?.Value,
            Publisher = app.Attribute("Publisher")?.Value,
            Version = app.Attribute("Version")?.Value,
            MetadataSource = "NavxManifest.xml"
        };

        var dependencies = doc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "Dependencies");
        foreach (var dep in dependencies?.Elements().Where(e => e.Name.LocalName == "Dependency") ?? Enumerable.Empty<XElement>())
        {
            var depId = dep.Attribute("Id")?.Value ?? dep.Attribute("AppId")?.Value;
            if (Guid.TryParse(depId, out var parsedId))
            {
                info.Dependencies.Add(new PteAppDependency
                {
                    Id = parsedId,
                    Name = dep.Attribute("Name")?.Value,
                    Publisher = dep.Attribute("Publisher")?.Value,
                    Version = dep.Attribute("MinVersion")?.Value ?? dep.Attribute("Version")?.Value
                });
            }
        }

        return info;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }
        return null;
    }
}
