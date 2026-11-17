using System.Net;
using System.Text.Json;

namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class AdminCenterToolTests : HttpTestBase
{
    private const string EnvironmentPath = "/admin/v2.29/applications/BusinessCentral/environments/Production";

    [Fact]
    public async Task GetAppOperations_WithOperationId_ReturnsTheSingleOperation()
    {
        var appId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps/{appId}/operations/{operationId}$", HttpStatusCode.OK,
            new { id = operationId, status = "succeeded", type = "install", targetVersion = "2.0.0.0" });

        var json = Parse(await D365BCAdminService.get_app_operations(TenantId, "Production", appId, operationId));

        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        var operation = Assert.Single(json.EnumerateArray());
        Assert.Equal(operationId, operation.GetProperty("id").GetGuid());
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetAppOperations_WithoutOperationId_ReturnsTheList()
    {
        var appId = Guid.NewGuid();
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps/{appId}/operations$", HttpStatusCode.OK,
            new { value = new[] { new { id = Guid.NewGuid(), status = "failed" }, new { id = Guid.NewGuid(), status = "succeeded" } } });

        var json = Parse(await D365BCAdminService.get_app_operations(TenantId, "Production", appId));

        Assert.Equal(2, json.GetArrayLength());
    }

    [Fact]
    public async Task Tools_ReturnTheApiErrorInsteadOfAGenericMessage()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/settings/upgrade$", HttpStatusCode.NotFound,
            new { code = "EnvironmentNotFound", message = "Environment 'Production' was not found." });

        var json = Parse(await D365BCAdminService.get_environment_update_window(TenantId, "Production"));

        var error = json.GetProperty("error").GetString();
        Assert.Contains("HTTP 404", error);
        Assert.Contains("EnvironmentNotFound", error);
        Assert.Contains("was not found", error);
    }

    [Fact]
    public async Task Tools_UnwrapODataErrorsFromTheAutomationApi()
    {
        var companyId = Guid.NewGuid();
        Api.On(HttpMethod.Get, "/v2.0/Production/api/microsoft/automation/v2.0/companies.*/features$", HttpStatusCode.Forbidden,
            new { error = new { code = "Authorization_RequestDenied", message = "Insufficient privileges." } });

        var json = Parse(await D365BCAdminService.get_available_features(TenantId, "Production", companyId));

        var error = json.GetProperty("error").GetString();
        Assert.Contains("HTTP 403", error);
        Assert.Contains("Authorization_RequestDenied: Insufficient privileges.", error);
    }

    [Fact]
    public async Task Tools_EscapeTheEnvironmentNameAndSendTheBearerToken()
    {
        Api.On(HttpMethod.Get, "/apps$", HttpStatusCode.OK, new { value = Array.Empty<object>() });

        await D365BCAdminService.get_installed_apps(TenantId, "My Sandbox/1");

        var request = Assert.Single(Api.Requests);
        Assert.Contains("/environments/My%20Sandbox%2F1/apps", request.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {Token}", request.Authorization);
    }

    [Fact]
    public async Task GetEnvironmentInformations_ReportsFailuresInsteadOfAnEmptyList()
    {
        Api.On(HttpMethod.Get, "/admin/v2.29/applications/BusinessCentral/environments$", HttpStatusCode.Unauthorized,
            new { code = "Unauthorized", message = "The token is not valid." });

        var json = Parse(await D365BCAdminService.get_environment_informations(TenantId));

        Assert.Contains("HTTP 401", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task KillActiveSessions_IncludesTheErrorOnFailure()
    {
        Api.On(HttpMethod.Delete, $"{EnvironmentPath}/sessions/42$", HttpStatusCode.NotFound,
            new { code = "SessionNotFound", message = "Session 42 does not exist." });

        var json = Parse(await D365BCAdminService.kill_active_sessions(TenantId, "BusinessCentral", "Production", 42));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(42, json.GetProperty("sessionId").GetInt32());
        Assert.Contains("SessionNotFound", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task KillActiveSessions_SucceedsOnNoContent()
    {
        Api.On(HttpMethod.Delete, $"{EnvironmentPath}/sessions/7$", HttpStatusCode.NoContent);

        var json = Parse(await D365BCAdminService.kill_active_sessions(TenantId, "BusinessCentral", "Production", 7));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.False(json.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task InstallApp_ReturnsMissingRequirementsFromA400()
    {
        var appId = Guid.NewGuid();
        Api.On(HttpMethod.Post, $"{EnvironmentPath}/apps/{appId}/install$", HttpStatusCode.BadRequest, new
        {
            code = "MissingDependencies",
            message = "The app has missing dependencies.",
            data = new { requirements = new[] { new { id = Guid.NewGuid(), name = "Dependency", publisher = "ISV", version = "1.0.0.0", type = "Install" } } }
        });

        var json = Parse(await D365BCAdminService.install_app(TenantId, "Production", appId, installOrUpdateNeededDependencies: false));

        Assert.Equal("MissingDependencies", json.GetProperty("code").GetString());
        Assert.Equal(1, json.GetProperty("data").GetProperty("requirements").GetArrayLength());
    }

    [Fact]
    public async Task ActivateFeature_PostsToTheODataActionAndReportsErrors()
    {
        var companyId = Guid.NewGuid();
        Api.On(HttpMethod.Post, "/features\\('MyFeature'\\)/Microsoft.NAV.activate$", HttpStatusCode.BadRequest,
            new { error = new { code = "BadRequest", message = "Feature already enabled." } });

        var json = Parse(await D365BCAdminService.activate_feature(TenantId, "Production", companyId, "MyFeature"));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("Feature already enabled.", json.GetProperty("error").GetString());
        var body = Parse(Api.Requests.Single().Body!);
        Assert.True(body.GetProperty("updateInBackground").GetBoolean());
    }

    [Fact]
    public async Task Tools_TruncateLongNonJsonErrorBodies()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/usedstorage$", HttpStatusCode.BadGateway, "<html>" + new string('x', 5000) + "</html>");

        var error = Parse(await D365BCAdminService.get_usage_storage_for_environment(TenantId, "Production")).GetProperty("error").GetString()!;

        Assert.StartsWith("HTTP 502 Bad Gateway: <html>", error);
        Assert.EndsWith("...", error);
        Assert.True(error.Length < 600);
    }

    [Fact]
    public async Task Tools_DoNotWriteToStandardOutput()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/updates$", HttpStatusCode.InternalServerError, "not json");
        var originalOut = Console.Out;
        var originalLevel = Log.MinimumLevel;
        using var stdout = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Log.MinimumLevel = LogSeverity.Debug;

            var json = Parse(await D365BCAdminService.get_environment_updates(TenantId, "Production"));

            Assert.Contains("HTTP 500", json.GetProperty("error").GetString());
            Assert.Contains("not json", json.GetProperty("error").GetString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Log.MinimumLevel = originalLevel;
        }

        Assert.Equal(string.Empty, stdout.ToString());
    }
}
