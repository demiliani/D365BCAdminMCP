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
}
