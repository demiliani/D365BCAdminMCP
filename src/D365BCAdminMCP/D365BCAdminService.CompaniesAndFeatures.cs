using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

public partial class D365BCAdminService
{
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets a list of available features and their status in the feature management of a specific Business Central environment.")]
    public static async Task<string> get_available_features(
        [Description("The tenant ID (GUID) for which to get available features")] Guid tenantId,
        [Description("The name of the environment to get features for")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getAvailableFeatures(tenantId, environmentName, companyId));
    }

    [McpServerTool(Destructive = false, Idempotent = true), Description("Activates a feature in a Business Central environment.")]
    public static async Task<string> activate_feature(
        [Description("The tenant ID (GUID) for which to activate the feature")] Guid tenantId,
        [Description("The name of the environment to activate the feature in")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
        [Description("The feature ID (string) to activate")] string featureId,
        [Description("Optional: Whether the feature activation should occur in the background (default: true)")] bool updateInBackground = true,
        [Description("Optional: The date and time when the feature activation should commence in ISO 8601 format (default: current UTC time)")] DateTime? startDateTime = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.activateFeature(tenantId, environmentName, companyId, featureId, updateInBackground, startDateTime);

        if (result.Success)
        {
            return JsonSerializer.Serialize(new { 
                success = true, 
                message = $"Feature {featureId} activation initiated successfully",
                featureId = featureId,
                updateInBackground = updateInBackground,
                startDateTime = startDateTime ?? DateTime.UtcNow
            });
        }

        return JsonSerializer.Serialize(new { 
            success = false, 
            error = result.Error
        });
    }

    [McpServerTool(Destructive = false, Idempotent = true), Description("Deactivates a feature in a Business Central environment.")]
    public static async Task<string> deactivate_feature(
        [Description("The tenant ID (GUID) for which to deactivate the feature")] Guid tenantId,
        [Description("The name of the environment to deactivate the feature in")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
        [Description("The feature ID (string) to deactivate")] string featureId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.deactivateFeature(tenantId, environmentName, companyId, featureId);

        if (result.Success)
        {
            return JsonSerializer.Serialize(new { 
                success = true, 
                message = $"Feature {featureId} deactivation initiated successfully",
                featureId = featureId
            });
        }

        return JsonSerializer.Serialize(new { 
            success = false, 
            error = result.Error
        });
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets a list of companies available in a specific Business Central environment.")]
    public static async Task<string> get_environment_companies(
        [Description("The tenant ID (GUID) for which to get environment companies")] Guid tenantId,
        [Description("The name of the environment to get companies for")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getEnvironmentCompanies(environmentName));
    }

    public async Task<ApiResult<List<Feature>>> getAvailableFeatures(Guid tenantId, string environmentName, Guid companyId)
    {
        await EnsureAuthenticated(tenantId);
        return (await SendAsync<FeaturesResponse>(HttpMethod.Get, $"{AutomationCompanyUrl(environmentName, companyId)}/features"))
            .Map(response => response.Value ?? new List<Feature>());
    }

    public async Task<ApiResult<bool>> activateFeature(Guid tenantId, string environmentName, Guid companyId, string featureId, bool updateInBackground = true, DateTime? startDateTime = null)
    {
        await EnsureAuthenticated(tenantId);

        var requestBody = new
        {
            updateInBackground,
            startDateTime = (startDateTime ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        return await SendWithoutResultAsync(HttpMethod.Post, $"{AutomationCompanyUrl(environmentName, companyId)}/features('{EscapeODataKey(featureId)}')/Microsoft.NAV.activate", requestBody);
    }

    public async Task<ApiResult<bool>> deactivateFeature(Guid tenantId, string environmentName, Guid companyId, string featureId)
    {
        await EnsureAuthenticated(tenantId);
        return await SendWithoutResultAsync(HttpMethod.Post, $"{AutomationCompanyUrl(environmentName, companyId)}/features('{EscapeODataKey(featureId)}')/Microsoft.NAV.deactivate", new { });
    }

    private static string EscapeODataKey(string key) => Uri.EscapeDataString(key.Replace("'", "''"));

    public async Task<ApiResult<List<Company>>> getEnvironmentCompanies(string environmentName) =>
        (await SendAsync<CompaniesResponse>(HttpMethod.Get, $"https://api.businesscentral.dynamics.com/v2.0/{Uri.EscapeDataString(environmentName)}/api/v2.0/companies"))
            .Map(response => response.Value ?? new List<Company>());
}
