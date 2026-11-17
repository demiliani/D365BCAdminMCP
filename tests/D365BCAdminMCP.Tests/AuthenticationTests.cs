using System.Net;

namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class AuthenticationTests : HttpTestBase
{
    [Fact]
    public async Task GetMicrosoftEntraIdToken_ReturnsStatusWithoutTheToken()
    {
        var output = await D365BCAdminService.get_microsoft_entra_id_token(TenantId);

        Assert.DoesNotContain(Token, output);
        var json = Parse(output);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("fromCache").GetBoolean());
        Assert.Equal(TenantId, json.GetProperty("tenantId").GetGuid());
        Assert.True(json.GetProperty("expiresOn").GetDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task OtherTools_StillSendTheCachedTokenAfterSignIn()
    {
        Api.On(HttpMethod.Get, "/environments$", HttpStatusCode.OK, new { value = Array.Empty<object>() });

        await D365BCAdminService.get_microsoft_entra_id_token(TenantId);
        await D365BCAdminService.get_environment_informations(TenantId);

        Assert.Equal($"Bearer {Token}", Assert.Single(Api.Requests).Authorization);
    }

    [Fact]
    public async Task GetTenantIdFromTenantName_ReadsThePublicOpenIdConfigurationWithoutTheToken()
    {
        var tenantId = Guid.NewGuid();
        Api.On(HttpMethod.Get, "^/contoso.onmicrosoft.com/.well-known/openid-configuration$", HttpStatusCode.OK,
            new { token_endpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/token" });

        var json = Parse(await D365BCAdminService.get_tenant_id_from_tenant_name(" contoso.onmicrosoft.com "));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(tenantId, json.GetProperty("tenantId").GetGuid());
        var request = Assert.Single(Api.Requests);
        Assert.Equal("login.microsoftonline.com", request.Uri.Host);
        Assert.Null(request.Authorization);
    }

    [Theory]
    [InlineData("contoso.com/../common")]
    [InlineData("contoso com")]
    [InlineData("")]
    public async Task GetTenantIdFromTenantName_RejectsInvalidNamesWithoutCallingEntra(string tenantName)
    {
        var json = Parse(await D365BCAdminService.get_tenant_id_from_tenant_name(tenantName));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("not a valid tenant name", json.GetProperty("error").GetString());
        Assert.Empty(Api.Requests);
    }

    [Fact]
    public async Task GetTenantIdFromTenantName_ReportsUnknownTenants()
    {
        Api.On(HttpMethod.Get, "/.well-known/openid-configuration$", HttpStatusCode.BadRequest,
            new { error = "invalid_tenant", error_description = "Tenant 'nowhere.example' not found." });

        var json = Parse(await D365BCAdminService.get_tenant_id_from_tenant_name("nowhere.example"));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("HTTP 400", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ClearCachedToken_ForgetsTheTokenAndThePersistedAccount()
    {
        using var folder = new TempFolder();
        var originalDirectory = D365BCAdminService.AuthenticationRecordDirectory;
        D365BCAdminService.AuthenticationRecordDirectory = folder.Path;
        try
        {
            var recordPath = D365BCAdminService.AuthenticationRecordPath(TenantId);
            File.WriteAllText(recordPath, "{}");

            var json = Parse(await D365BCAdminService.clear_cached_token(TenantId));

            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.False(File.Exists(recordPath));
            var status = Parse(await D365BCAdminService.get_token_cache_status());
            Assert.DoesNotContain(TenantId.ToString(), status.ToString());
        }
        finally
        {
            D365BCAdminService.AuthenticationRecordDirectory = originalDirectory;
        }
    }
}
