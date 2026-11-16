using System.Net;

namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class AccessAndExportToolTests : HttpTestBase
{
    private const string SettingsPath = "/admin/v2.29/applications/BusinessCentral/environments/Production/settings";
    private const string ExportPath = "/admin/v2.29/exports/applications/BusinessCentral/environments/Production";
    private const string SasUri = "https://contosoexports.blob.core.windows.net/?sv=2024-11-04&ss=b&srt=co&sp=rwdc&se=2026-11-17T00:00:00Z&sig=c2VjcmV0";

    [Fact]
    public async Task StartDatabaseExport_PostsTheStorageTargetAndNeverEchoesTheSasUri()
    {
        Api.On(HttpMethod.Post, $"{ExportPath}$", HttpStatusCode.OK);

        var output = await D365BCAdminService.start_database_export(TenantId, "Production", SasUri, "bc-exports", "prod.bacpac");

        var json = Parse(output);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("sig=", output);
        var body = Parse(Assert.Single(Api.Requests).Body!);
        Assert.Equal(SasUri, body.GetProperty("storageAccountSasUri").GetString());
        Assert.Equal("bc-exports", body.GetProperty("container").GetString());
        Assert.Equal("prod.bacpac", body.GetProperty("blob").GetString());
    }

    [Fact]
    public async Task StartDatabaseExport_DefaultsTheBlobNameToTheEnvironmentAndTimestamp()
    {
        Api.On(HttpMethod.Post, $"{ExportPath}$", HttpStatusCode.OK);

        var json = Parse(await D365BCAdminService.start_database_export(TenantId, "Production", SasUri, "bc-exports"));

        Assert.Matches("^Production-\\d{8}-\\d{6}\\.bacpac$", json.GetProperty("blob").GetString());
    }

    [Theory]
    [InlineData("http://contosoexports.blob.core.windows.net/?sig=x", "bc-exports", "https SAS URI")]
    [InlineData("https://contosoexports.blob.core.windows.net/", "bc-exports", "https SAS URI")]
    [InlineData(SasUri, "BC_Exports", "not a valid container name")]
    [InlineData(SasUri, "bc--exports", "not a valid container name")]
    [InlineData(SasUri, "bc", "not a valid container name")]
    public async Task StartDatabaseExport_RejectsInvalidTargetsWithoutCallingTheApi(string sasUri, string container, string expectedError)
    {
        var json = Parse(await D365BCAdminService.start_database_export(TenantId, "Production", sasUri, container));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains(expectedError, json.GetProperty("error").GetString());
        Assert.Empty(Api.Requests);
    }

    [Fact]
    public async Task GetDatabaseExportMetrics_ReturnsTheMonthlyQuota()
    {
        Api.On(HttpMethod.Get, $"{ExportPath}/metrics$", HttpStatusCode.OK, new { exportsPerMonth = 10, exportsRemainingThisMonth = 7 });

        var json = Parse(await D365BCAdminService.get_database_export_metrics(TenantId, "Production"));

        Assert.Equal(10, json.GetProperty("exportsPerMonth").GetInt32());
        Assert.Equal(7, json.GetProperty("exportsRemainingThisMonth").GetInt32());
    }

    [Fact]
    public async Task GetEnvironmentSecurityGroup_ReturnsTheAssignedGroup()
    {
        var groupId = Guid.NewGuid();
        Api.On(HttpMethod.Get, $"{SettingsPath}/securitygroupaccess$", HttpStatusCode.OK, new { id = groupId, displayName = "BC Users" });

        var json = Parse(await D365BCAdminService.get_environment_security_group(TenantId, "Production"));

        Assert.True(json.GetProperty("configured").GetBoolean());
        Assert.Equal(groupId, json.GetProperty("id").GetGuid());
        Assert.Equal("BC Users", json.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task GetEnvironmentSecurityGroup_ReportsNoGroupOnNoContent()
    {
        Api.On(HttpMethod.Get, $"{SettingsPath}/securitygroupaccess$", HttpStatusCode.NoContent);

        var json = Parse(await D365BCAdminService.get_environment_security_group(TenantId, "Production"));

        Assert.False(json.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task SetEnvironmentSecurityGroup_PostsTheGroupId()
    {
        var groupId = Guid.NewGuid();
        Api.On(HttpMethod.Post, $"{SettingsPath}/securitygroupaccess$", HttpStatusCode.OK);

        var json = Parse(await D365BCAdminService.set_environment_security_group(TenantId, "Production", groupId));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(groupId, Parse(Assert.Single(Api.Requests).Body!).GetProperty("Value").GetGuid());
    }

    [Fact]
    public async Task SetEnvironmentSecurityGroup_ReportsUnknownGroups()
    {
        Api.On(HttpMethod.Post, $"{SettingsPath}/securitygroupaccess$", HttpStatusCode.NotFound,
            new { code = "EntityNotFound", message = "Group not found in Microsoft Entra ID." });

        var json = Parse(await D365BCAdminService.set_environment_security_group(TenantId, "Production", Guid.NewGuid()));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("HTTP 404", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ClearEnvironmentSecurityGroup_SendsDelete()
    {
        Api.On(HttpMethod.Delete, $"{SettingsPath}/securitygroupaccess$", HttpStatusCode.OK);

        var json = Parse(await D365BCAdminService.clear_environment_security_group(TenantId, "Production"));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(HttpMethod.Delete, Assert.Single(Api.Requests).Method);
    }

    [Fact]
    public async Task AccessWithM365Licenses_CanBeReadAndChanged()
    {
        Api.On(HttpMethod.Get, $"{SettingsPath}/accesswithm365licenses$", HttpStatusCode.OK, new { enabled = true })
            .On(HttpMethod.Post, $"{SettingsPath}/accesswithm365licenses$", HttpStatusCode.OK);

        var current = Parse(await D365BCAdminService.get_access_with_m365_licenses(TenantId, "Production"));
        var changed = Parse(await D365BCAdminService.set_access_with_m365_licenses(TenantId, "Production", false));

        Assert.True(current.GetProperty("enabled").GetBoolean());
        Assert.True(changed.GetProperty("success").GetBoolean());
        Assert.False(Parse(Api.Requests[1].Body!).GetProperty("enabled").GetBoolean());
    }
}
