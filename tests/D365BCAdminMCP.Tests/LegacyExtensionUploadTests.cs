using System.Net;
using System.Text.RegularExpressions;

namespace D365BCAdminMCP.Tests;

// Deprecated Automation API upload (create_pte_upload): every call must be authenticated and failures must carry the API error.
[Collection(HttpCollection.Name)]
public class LegacyExtensionUploadTests : HttpTestBase, IDisposable
{
    private static readonly Guid CompanyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string UploadId = "99999999-8888-7777-6666-555555555555";
    private static readonly string UploadPath = Regex.Escape($"/v2.0/Production/api/microsoft/automation/v2.0/companies({CompanyId})/extensionUpload");
    private static readonly string RecordPath = UploadPath + Regex.Escape($"({UploadId})");

    private readonly TempFolder _folder = new();
    private readonly string _appFile;

    public LegacyExtensionUploadTests()
    {
        _appFile = _folder.File("MyPTE_1.0.0.0.app");
        File.WriteAllBytes(_appFile, new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose() => _folder.Dispose();

    private LegacyExtensionUploadTests MockRecordAndContent()
    {
        Api.On(HttpMethod.Get, $"{RecordPath}$", HttpStatusCode.OK, new Dictionary<string, object>
        {
            ["@odata.etag"] = "W/\"JzE5OzEyMzQ1Njc4OTAxMjM0NTY3ODkwMTsn\"",
            ["systemId"] = UploadId,
            ["schedule"] = "Current version",
            ["schemaSyncMode"] = "Add"
        });
        Api.On(HttpMethod.Patch, $"{RecordPath}/extensionContent$", HttpStatusCode.NoContent);
        Api.On(HttpMethod.Post, $"{RecordPath}/Microsoft\\.NAV\\.upload$", HttpStatusCode.NoContent);
        return this;
    }

    [Fact]
    public async Task CreatePteUpload_AuthenticatesEveryCallAndSendsTheEtag()
    {
        Api.On(HttpMethod.Post, $"{UploadPath}$", HttpStatusCode.Created, new { systemId = UploadId, schedule = "Current version", schemaSyncMode = "Add" });
        MockRecordAndContent();

        var json = Parse(await D365BCAdminService.create_pte_upload(TenantId, "Production", CompanyId, _appFile));

        Assert.True(json.GetProperty("success").GetBoolean(), json.ToString());
        Assert.Equal(UploadId, json.GetProperty("extensionUploadId").GetString());
        Assert.Equal(4, Api.Requests.Count);
        Assert.All(Api.Requests, r => Assert.Equal($"Bearer {Token}", r.Authorization));

        var patch = Assert.Single(Api.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Equal("application/octet-stream", patch.ContentType);
        Assert.Equal("W/\"JzE5OzEyMzQ1Njc4OTAxMjM0NTY3ODkwMTsn\"", patch.IfMatch);
    }

    [Fact]
    public async Task CreatePteUpload_ReusesTheExistingUploadRecordWhenThePostIsRejected()
    {
        Api.On(HttpMethod.Post, $"{UploadPath}$", HttpStatusCode.BadRequest, new { error = new { code = "Internal_EntityWithSameKeyExists", message = "The record already exists." } });
        Api.On(HttpMethod.Get, $"{UploadPath}$", HttpStatusCode.OK, new { value = new[] { new { systemId = UploadId, schedule = "Current version", schemaSyncMode = "Add" } } });
        MockRecordAndContent();

        var json = Parse(await D365BCAdminService.create_pte_upload(TenantId, "Production", CompanyId, _appFile));

        Assert.True(json.GetProperty("success").GetBoolean(), json.ToString());
        Assert.Equal(UploadId, json.GetProperty("extensionUploadId").GetString());
        Assert.All(Api.Requests, r => Assert.Equal($"Bearer {Token}", r.Authorization));
    }

    [Fact]
    public async Task CreatePteUpload_ReturnsTheApiErrorWhenTheContentUploadFails()
    {
        Api.On(HttpMethod.Post, $"{UploadPath}$", HttpStatusCode.Created, new { systemId = UploadId });
        MockRecordAndContent();
        Api.On(HttpMethod.Patch, $"{RecordPath}/extensionContent$", HttpStatusCode.BadRequest,
            new { error = new { code = "BadRequest", message = "The extension could not be deployed because it is not compatible." } });

        var json = Parse(await D365BCAdminService.create_pte_upload(TenantId, "Production", CompanyId, _appFile));

        Assert.False(json.GetProperty("success").GetBoolean());
        var error = json.GetProperty("error").GetString();
        Assert.Contains("Failed to upload extension content", error);
        Assert.Contains("HTTP 400", error);
        Assert.Contains("not compatible", error);
        Assert.DoesNotContain(Api.Requests, r => r.Uri.AbsolutePath.EndsWith("Microsoft.NAV.upload"));
    }

    [Fact]
    public async Task CreatePteUpload_ReturnsTheApiErrorWhenTheTaskCannotBeCreated()
    {
        Api.On(HttpMethod.Post, $"{UploadPath}$", HttpStatusCode.Forbidden,
            new { error = new { code = "Authorization_RequestDenied", message = "You do not have permission to upload extensions." } });

        var json = Parse(await D365BCAdminService.create_pte_upload(TenantId, "Production", CompanyId, _appFile));

        Assert.False(json.GetProperty("success").GetBoolean());
        var error = json.GetProperty("error").GetString();
        Assert.StartsWith("Failed to create extension upload task", error);
        Assert.Contains("Authorization_RequestDenied", error);
    }

    [Fact]
    public async Task GetExtensionUpload_ReturnsTheApiError()
    {
        Api.On(HttpMethod.Get, $"{RecordPath}$", HttpStatusCode.NotFound, new { error = new { code = "BadRequest_NotFound", message = "The extension upload does not exist." } });

        var json = Parse(await D365BCAdminService.get_extension_upload(TenantId, "Production", CompanyId, UploadId));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("BadRequest_NotFound", json.GetProperty("error").GetString());
        Assert.Equal($"Bearer {Token}", Assert.Single(Api.Requests).Authorization);
    }
}
