using System.Net;
using System.Text.Json;

namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class EnvironmentToolTests : HttpTestBase
{
    private const string EnvironmentsPath = "/admin/v2.29/applications/BusinessCentral/environments";
    private static readonly Guid OperationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static object Operation(string type, string status = "queued") =>
        new { id = OperationId, type, status, productFamily = "BusinessCentral", createdOn = "2026-11-16T08:00:00Z" };

    [Fact]
    public async Task CopyEnvironment_PostsToTheSourceEnvironmentWithTheNewNameInTheBody()
    {
        Api.On(HttpMethod.Post, $"{EnvironmentsPath}/Production/copy$", HttpStatusCode.Accepted, Operation("copy"));

        var json = Parse(await D365BCAdminService.copy_environment(TenantId, "Production", "Testing", "Sandbox"));

        var request = Assert.Single(Api.Requests);
        var body = Parse(request.Body!);
        Assert.Equal("Testing", body.GetProperty("environmentName").GetString());
        Assert.Equal("Sandbox", body.GetProperty("type").GetString());
        Assert.Equal(OperationId, json.GetProperty("id").GetGuid());
        Assert.Equal("copy", json.GetProperty("type").GetString());
        Assert.Equal("queued", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateNewEnvironment_PutsTheRingAndReturnsTheOperation()
    {
        Api.On(HttpMethod.Put, $"{EnvironmentsPath}/Dev-Test$", HttpStatusCode.Accepted, Operation("create"));

        var json = Parse(await D365BCAdminService.create_new_environment(TenantId, "Dev-Test", "Sandbox", "IT", ringName: " Preview "));

        var body = Parse(Assert.Single(Api.Requests).Body!);
        Assert.Equal("Sandbox", body.GetProperty("environmentType").GetString());
        Assert.Equal("IT", body.GetProperty("countryCode").GetString());
        Assert.Equal("Preview", body.GetProperty("ringName").GetString());
        Assert.False(body.TryGetProperty("applicationVersion", out _));
        Assert.Equal(OperationId, json.GetProperty("id").GetGuid());
        Assert.Equal("create", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RenameEnvironment_PostsTheNewName()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentsPath}/Testing$", HttpStatusCode.OK, new { name = "Testing", type = "Sandbox" })
            .On(HttpMethod.Post, $"{EnvironmentsPath}/Testing/rename$", HttpStatusCode.Accepted, Operation("environmentRename"));

        var json = Parse(await D365BCAdminService.rename_environment(TenantId, "Testing", "UAT"));

        var rename = Api.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("UAT", Parse(rename.Body!).GetProperty("NewEnvironmentName").GetString());
        Assert.Equal("environmentRename", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RestoreEnvironment_SendsThePointInTimeInUtc()
    {
        Api.On(HttpMethod.Post, $"{EnvironmentsPath}/Production/restore$", HttpStatusCode.Accepted, Operation("pitRestore"));
        var pointInTime = new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.FromHours(1)).UtcDateTime;

        await D365BCAdminService.restore_environment(TenantId, "Production", "Prod-Restore", "Sandbox", pointInTime, skipInstallingPtes: true);

        var body = Parse(Assert.Single(Api.Requests).Body!);
        Assert.Equal("Prod-Restore", body.GetProperty("EnvironmentName").GetString());
        Assert.Equal("Sandbox", body.GetProperty("EnvironmentType").GetString());
        Assert.Equal("2026-11-15T08:30:00Z", body.GetProperty("PointInTime").GetString());
        Assert.True(body.GetProperty("SkipInstallingPTEs").GetBoolean());
        Assert.False(body.GetProperty("SkipInstallingThirdPartyGlobalApps").GetBoolean());
        Assert.False(body.GetProperty("SkipEnvironmentCleanup").GetBoolean());
    }

    [Fact]
    public async Task RecoverEnvironment_PostsToTheRecoverEndpoint()
    {
        Api.On(HttpMethod.Post, $"{EnvironmentsPath}/Old-Test/recover$", HttpStatusCode.Accepted, Operation("recover"));

        var json = Parse(await D365BCAdminService.recover_environment(TenantId, "Old-Test"));

        Assert.Single(Api.Requests);
        Assert.Equal("recover", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetAvailableRestorePeriods_ReturnsThePeriods()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentsPath}/Production/availableRestorePeriods$", HttpStatusCode.OK, new
        {
            value = new[] { new { from = "2026-10-17T00:00:00Z", to = "2026-11-16T08:00:00Z", correspondingApplicationPackageVersion = "27.1.1.0" } }
        });

        var json = Parse(await D365BCAdminService.get_available_restore_periods(TenantId, "Production"));

        var period = Assert.Single(json.EnumerateArray());
        Assert.Equal("27.1.1.0", period.GetProperty("correspondingApplicationPackageVersion").GetString());
    }

    [Theory]
    [InlineData(null, false, "/admin/v2.29/environments/operations")]
    [InlineData("Production", false, EnvironmentsPath + "/Production/operations")]
    [InlineData(null, true, "/admin/v2.29/environments/operations/" + "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    public async Task GetEnvironmentOperations_UsesTheTenantOrEnvironmentEndpoint(string? environmentName, bool withOperationId, string expectedPath)
    {
        Api.On(HttpMethod.Get, "/operations$", HttpStatusCode.OK, new { value = new[] { Operation("copy", "running") } })
            .On(HttpMethod.Get, $"/operations/{OperationId}$", HttpStatusCode.OK, Operation("copy", "running"));

        var json = Parse(await D365BCAdminService.get_environment_operations(TenantId, environmentName, withOperationId ? OperationId : null));

        Assert.Equal(expectedPath, Assert.Single(Api.Requests).Uri.AbsolutePath);
        Assert.Equal("running", Assert.Single(json.EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetEnvironmentQuotas_ReturnsTheQuotas()
    {
        Api.On(HttpMethod.Get, "/admin/v2.29/environments/quotas$", HttpStatusCode.OK, new
        {
            environmentsCount = new { production = 3, sandbox = 3 },
            storageInKilobytes = new { @default = 83886080, userLicenses = 0, additionalCapacity = 0, total = 83886080 }
        });

        var json = Parse(await D365BCAdminService.get_environment_quotas(TenantId));

        Assert.Equal(3, json.GetProperty("environmentsCount").GetProperty("sandbox").GetInt32());
        Assert.Equal(83886080, json.GetProperty("storageInKilobytes").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task GetInstalledApps_ExposesTheIdentifierAsIdWhicheverNameTheApiUses()
    {
        var idApp = Guid.NewGuid();
        var appIdApp = Guid.NewGuid();
        Api.On(HttpMethod.Get, $"{EnvironmentsPath}/Production/apps$", HttpStatusCode.OK, new
        {
            value = new object[] { new { id = idApp, name = "A" }, new { appId = appIdApp, name = "B" } }
        });

        var apps = Parse(await D365BCAdminService.get_installed_apps(TenantId, "Production")).EnumerateArray().ToList();

        Assert.Equal(new[] { idApp, appIdApp }, apps.Select(a => a.GetProperty("id").GetGuid()));
        Assert.All(apps, a => Assert.False(a.TryGetProperty("appId", out _)));
    }
}
