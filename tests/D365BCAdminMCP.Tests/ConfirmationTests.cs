using System.Net;

namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class ConfirmationTests : HttpTestBase
{
    private const string EnvironmentsPath = "/admin/v2.29/applications/BusinessCentral/environments";

    private void EnvironmentIs(string name, string type) =>
        Api.On(HttpMethod.Get, $"{EnvironmentsPath}/{name}$", HttpStatusCode.OK, new { name, type });

    private bool Called(HttpMethod method) => Api.Requests.Any(r => r.Method == method);

    [Fact]
    public async Task DeletingASandbox_NeedsNoConfirmation()
    {
        EnvironmentIs("Old-Test", "Sandbox");
        Api.On(HttpMethod.Delete, $"{EnvironmentsPath}/Old-Test$", HttpStatusCode.Accepted, new { status = "softDeleting" });

        await D365BCAdminService.delete_environment(TenantId, "Old-Test");

        Assert.True(Called(HttpMethod.Delete));
    }

    [Fact]
    public async Task DeletingProduction_WithoutConfirmation_IsRefusedAndNothingIsDeleted()
    {
        EnvironmentIs("Production", "Production");

        var json = Parse(await D365BCAdminService.delete_environment(TenantId, "Production"));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("confirmationRequired").GetBoolean());
        Assert.False(Called(HttpMethod.Delete));
    }

    [Fact]
    public async Task DeletingProduction_WithTheTypedName_GoesAhead()
    {
        EnvironmentIs("Production", "Production");
        Api.On(HttpMethod.Delete, $"{EnvironmentsPath}/Production$", HttpStatusCode.Accepted, new { status = "softDeleting" });

        await D365BCAdminService.delete_environment(TenantId, "Production", confirmEnvironmentName: " production ");

        Assert.True(Called(HttpMethod.Delete));
    }

    [Fact]
    public async Task RenamingProduction_WithAWrongName_IsRefused()
    {
        EnvironmentIs("Production", "Production");

        var json = Parse(await D365BCAdminService.rename_environment(TenantId, "Production", "Prod", confirmEnvironmentName: "Prod"));

        Assert.True(json.GetProperty("confirmationRequired").GetBoolean());
        Assert.False(Called(HttpMethod.Post));
    }

    [Fact]
    public async Task UninstallingWithoutDeletingData_NeedsNoConfirmation()
    {
        var appId = Guid.NewGuid();
        Api.On(HttpMethod.Post, $"{EnvironmentsPath}/Production/apps/{appId}/uninstall$", HttpStatusCode.OK, new { id = Guid.NewGuid(), status = "scheduled" });

        await D365BCAdminService.uninstall_app(TenantId, "Production", appId, deleteData: false);

        Assert.Single(Api.Requests);
    }

    [Fact]
    public async Task UninstallingAndDeletingDataInProduction_NeedsConfirmation()
    {
        EnvironmentIs("Production", "Production");

        var json = Parse(await D365BCAdminService.uninstall_app(TenantId, "Production", Guid.NewGuid(), deleteData: true));

        Assert.True(json.GetProperty("confirmationRequired").GetBoolean());
        Assert.False(Called(HttpMethod.Post));
    }

    [Fact]
    public async Task UnknownEnvironment_ReturnsTheApiErrorBeforeAnyChange()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentsPath}/Nope$", HttpStatusCode.NotFound, new { code = "EnvironmentNotFound", message = "Not found" });

        var json = Parse(await D365BCAdminService.delete_environment(TenantId, "Nope"));

        Assert.Contains("EnvironmentNotFound", json.GetProperty("error").GetString());
        Assert.False(Called(HttpMethod.Delete));
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("production ", true)]
    [InlineData("Prod", false)]
    [InlineData(null, false)]
    public async Task WithAPrompt_OnlyTheNameTypedByTheUserCounts(string? typed, bool allowed)
    {
        EnvironmentIs("Production", "Production");
        var service = new D365BCAdminService(Token);
        string? prompt = null;

        var refusal = await service.ConfirmProductionActionAsync("Production", "DELETE", confirmEnvironmentName: "Production", (message, _) =>
        {
            prompt = message;
            return Task.FromResult(typed);
        });

        Assert.Contains("PRODUCTION environment 'Production'", prompt);
        Assert.Equal(allowed, refusal == null);
        if (!allowed)
        {
            Assert.True(Parse(refusal!).GetProperty("cancelled").GetBoolean());
        }
    }

    [Fact]
    public async Task WithAPrompt_SandboxesAreNotPrompted()
    {
        EnvironmentIs("Dev", "Sandbox");
        var prompted = false;

        var refusal = await new D365BCAdminService(Token).ConfirmProductionActionAsync("Dev", "DELETE", null, (_, _) =>
        {
            prompted = true;
            return Task.FromResult<string?>(null);
        });

        Assert.Null(refusal);
        Assert.False(prompted);
    }
}
