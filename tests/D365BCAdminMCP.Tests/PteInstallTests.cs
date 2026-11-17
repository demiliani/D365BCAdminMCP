using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class PteInstallTests : HttpTestBase
{
    private const string EnvironmentPath = "/admin/v2.29/applications/BusinessCentral/environments/Sandbox";
    private static readonly Guid BaseApplicationId = Guid.Parse("437dbf0e-84ff-417a-965d-ed2bb9650972");

    private static PteInstallOptions Options(bool installDependencies = true) =>
        D365BCAdminService.BuildPteInstallOptions("Immediate", "Add", installDependencies, true, null, out _)!;

    [Fact]
    public async Task EnsureExternalDependencies_InstallsMissingAppSourceApps()
    {
        var appSourceId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var installed = false;
        Api.On(HttpMethod.Post, $"{EnvironmentPath}/apps/{appSourceId}/install$", _ =>
            {
                installed = true;
                return FakeAdminApi.Json(HttpStatusCode.OK, new { id = operationId, status = "scheduled" });
            })
            .On(HttpMethod.Get, $"{EnvironmentPath}/apps/{appSourceId}/operations/{operationId}$", HttpStatusCode.OK, new { id = operationId, status = "succeeded" })
            .On(HttpMethod.Get, $"{EnvironmentPath}/apps$", _ => FakeAdminApi.Json(HttpStatusCode.OK, new
            {
                value = installed ? new[] { new { id = appSourceId, name = "AppSource App", version = "3.0.0.0" } } : Array.Empty<object>()
            }));

        var pte = new PteAppInfo
        {
            Id = Guid.NewGuid(),
            Name = "Shop",
            FilePath = "Shop.app",
            Dependencies = { new PteAppDependency { Id = appSourceId, Name = "AppSource App", Publisher = "ISV", Version = "2.0.0.0" } }
        };

        var service = new D365BCAdminService(Token);
        var result = await service.ensureExternalDependencies("Sandbox", new List<PteAppInfo> { pte }, new List<InstalledApp>(), Options(), 5, 10);

        Assert.Null(result.FatalError);
        Assert.Empty(result.FailedDependencyIds);
        var dependency = Assert.Single(result.Dependencies);
        Assert.Equal("installed", dependency.Status);
        Assert.Equal("3.0.0.0", dependency.InstalledVersion);

        var installBody = Parse(Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!);
        Assert.True(installBody.GetProperty("acceptIsvEula").GetBoolean());
        Assert.False(installBody.TryGetProperty("targetVersion", out _));
    }

    [Fact]
    public async Task EnsureExternalDependencies_StopsWhenInstallingDependenciesIsDisabled()
    {
        var pte = new PteAppInfo
        {
            Id = Guid.NewGuid(),
            Name = "Shop",
            Dependencies = { new PteAppDependency { Id = Guid.NewGuid(), Name = "AppSource App", Version = "1.0.0.0" } }
        };

        var service = new D365BCAdminService(Token);
        var result = await service.ensureExternalDependencies("Sandbox", new List<PteAppInfo> { pte }, new List<InstalledApp>(), Options(installDependencies: false), 5, 10);

        Assert.NotNull(result.FatalError);
        Assert.Empty(Api.Requests);
    }

    [Fact]
    public async Task EnsureExternalDependencies_AssumesUnlistedPlatformAppsArePresent()
    {
        var pte = new PteAppInfo
        {
            Id = Guid.NewGuid(),
            Name = "Shop",
            Dependencies = { new PteAppDependency { Id = BaseApplicationId, Name = "Base Application", Publisher = "Microsoft", Version = "26.0.0.0" } }
        };

        var service = new D365BCAdminService(Token);
        var result = await service.ensureExternalDependencies("Sandbox", new List<PteAppInfo> { pte }, new List<InstalledApp>(), Options(), 5, 10);

        Assert.Null(result.FatalError);
        Assert.Equal("satisfied", Assert.Single(result.Dependencies).Status);
        Assert.Empty(Api.Requests);
    }

    [Fact]
    public async Task InstallPtesFromFolder_UploadsInDependencyOrderAndSkipsDependentsOfFailures()
    {
        using var folder = new TempFolder();
        var baseId = Guid.NewGuid();
        var topId = Guid.NewGuid();
        var standaloneId = Guid.NewGuid();
        AppPackageBuilder.WriteAppJsonPackage(folder.File("A_Top.app"), topId, "Top", "1.0.0.0", new DependencySpec(baseId, "Base", "1.0.0.0"));
        AppPackageBuilder.WriteAppJsonPackage(folder.File("B_Standalone.app"), standaloneId, "Standalone", "1.0.0.0");
        AppPackageBuilder.WriteManifestPackage(folder.File("Z_Base.app"), baseId, "Base", "1.0.0.0");

        var uploads = new List<Guid>();
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps$", HttpStatusCode.OK, new { value = Array.Empty<object>() })
            .On(HttpMethod.Post, $"{EnvironmentPath}/apps/pteInstall$", request =>
            {
                var appId = request.Body!.Contains("Z_Base.app") ? baseId
                    : request.Body.Contains("A_Top.app") ? topId : standaloneId;
                uploads.Add(appId);
                return FakeAdminApi.Json(HttpStatusCode.OK, new { id = Guid.NewGuid(), appId, status = "running" });
            })
            .On(HttpMethod.Get, $"{EnvironmentPath}/apps/{baseId}/operations/", HttpStatusCode.OK,
                new { status = "failed", errorMessage = "Compilation failed" })
            .On(HttpMethod.Get, $"{EnvironmentPath}/apps/{standaloneId}/operations/", HttpStatusCode.OK,
                new { status = "succeeded" });

        var json = Parse(await D365BCAdminService.install_ptes_from_folder(TenantId, "Sandbox", folder.Path, maxWaitSeconds: 5, pollIntervalMs: 10));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { standaloneId, baseId }, uploads);
        Assert.Equal(new[] { "B_Standalone.app", "Z_Base.app", "A_Top.app" },
            json.GetProperty("installOrder").EnumerateArray().Select(a => a.GetProperty("file").GetString()));

        var results = json.GetProperty("results").EnumerateArray().ToDictionary(r => r.GetProperty("file").GetString()!, r => r.GetProperty("status").GetString());
        Assert.Equal("succeeded", results["B_Standalone.app"]);
        Assert.Equal("failed", results["Z_Base.app"]);
        Assert.Equal("skipped", results["A_Top.app"]);

        var upload = Api.Requests.First(r => r.Uri.AbsolutePath.EndsWith("/pteInstall"));
        Assert.Equal("multipart/form-data", upload.ContentType);
        Assert.Matches(new Regex("name=\"?deploymentSchedule\"?\\s+Immediate"), upload.Body);
        Assert.Matches(new Regex("name=\"?syncMode\"?\\s+Add"), upload.Body);
        Assert.Matches(new Regex("name=\"?extensionFile\"?; filename=\"?B_Standalone.app\"?"), upload.Body);
    }

    [Fact]
    public async Task InstallPte_RejectsInvalidScheduleBeforeCallingTheApi()
    {
        var json = Parse(await D365BCAdminService.install_pte(TenantId, "Sandbox", "missing.app", deploymentSchedule: "Sometime"));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("deploymentSchedule", json.GetProperty("error").GetString());
        Assert.Empty(Api.Requests);
    }
}
