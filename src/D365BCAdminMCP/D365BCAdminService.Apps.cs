using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

public partial class D365BCAdminService
{
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets information about apps that are installed on a Business Central environment.")]
    public static async Task<string> get_installed_apps(
        [Description("The tenant ID (GUID) for which to get installed apps")] Guid tenantId,
        [Description("The name of the environment to get installed apps for")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getInstalledApps(environmentName));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets information about new app versions that are available for apps currently installed on a Business Central environment.")]
    public static async Task<string> get_available_app_updates(
        [Description("The tenant ID (GUID) for which to get available app updates")] Guid tenantId,
        [Description("The name of the environment to get available app updates for")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.getAvailableAppUpdates(environmentName);

        if (result.Success)
        {
            // Transform to the expected output format with "id" instead of "appId"
            var transformedUpdates = result.Value!.Select(update => new
            {
                id = update.Id,
                name = update.Name,
                publisher = update.Publisher,
                version = update.Version,
                requirements = update.Requirements?.Select(req => new
                {
                    id = req.Id,
                    name = req.Name,
                    publisher = req.Publisher,
                    version = req.Version,
                    type = req.Type
                }).ToArray() ?? Array.Empty<object>()
            }).ToList();

            // Return the proper format with "value" wrapper
            var response = new { value = transformedUpdates };
            return JsonSerializer.Serialize(response);
        }

        return ErrorJson(result.Error);
    }

    [McpServerTool(Destructive = false), Description("Updates an app in a Business Central environment to a specified target version.")]
    public static async Task<string> update_app(
        [Description("The tenant ID (GUID) for which to update the app")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app to update")] Guid appId,
        [Description("The target version to update to")] string targetVersion,
        [Description("Optional: Whether to use the environment update window")] bool? useEnvironmentUpdateWindow = null,
        [Description("Optional: Whether to allow preview versions")] bool? allowPreviewVersion = null,
        [Description("Optional: Whether to install or update needed dependencies")] bool? installOrUpdateNeededDependencies = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);

        var request = new UpdateAppRequest
        {
            TargetVersion = targetVersion,
            UseEnvironmentUpdateWindow = useEnvironmentUpdateWindow,
            AllowPreviewVersion = allowPreviewVersion,
            InstallOrUpdateNeededDependencies = installOrUpdateNeededDependencies
        };

        return ToJson(await d365bcAdminService.updateApp(environmentName, appId, request));
    }

    [McpServerTool(Destructive = false), Description("Installs an app in a Business Central environment. acceptIsvEula defaults to true, meaning you agree to the ISV terms.")]
    public static async Task<string> install_app(
        [Description("The tenant ID (GUID) for which to install the app")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app to install")] Guid appId,
        [Description("Whether to use the environment update window")] bool useEnvironmentUpdateWindow = false,
        [Description("Whether to install or update needed dependencies; if false, missing dependencies are returned as error details")] bool installOrUpdateNeededDependencies = true,
        [Description("Whether to accept the ISV EULA (must be true for installation to proceed; defaults to true)")] bool acceptIsvEula = true,
        [Description("Optional: Target version to install; required when allowPreviewVersion is true")] string? targetVersion = null,
        [Description("Optional: Whether to allow preview versions; requires targetVersion when true")] bool allowPreviewVersion = false,
        [Description("Optional: Locale ID language code, e.g. 'en-US'")] string? languageId = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);

        var request = new InstallAppRequest
        {
            TargetVersion = targetVersion,
            UseEnvironmentUpdateWindow = useEnvironmentUpdateWindow,
            AllowPreviewVersion = allowPreviewVersion,
            InstallOrUpdateNeededDependencies = installOrUpdateNeededDependencies,
            AcceptIsvEula = acceptIsvEula,
            LanguageId = languageId
        };

        var result = await d365bcAdminService.installApp("BusinessCentral", environmentName, appId, request);

        // A 400 carries the structured list of missing dependencies (data.requirements): return it as-is.
        if (result.StatusCode == 400 && result.ErrorBody != null)
        {
            try
            {
                var errorResponse = JsonSerializer.Deserialize<UpdateAppErrorResponse>(result.ErrorBody);
                if (errorResponse?.Code != null)
                {
                    return JsonSerializer.Serialize(errorResponse);
                }
            }
            catch (JsonException)
            {
            }
        }

        return ToJson(result);
    }

    [McpServerTool(Destructive = true), Description("Uninstalls an app from a Business Central environment. Deleting the app data (deleteData) in a production environment requires the user to confirm by typing the environment name.")]
    public static async Task<string> uninstall_app(
        [Description("The tenant ID (GUID) for which to uninstall the app")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app to uninstall")] Guid appId,
        [Description("Optional: Whether to use the environment update window")] bool? useEnvironmentUpdateWindow = null,
        [Description("Optional: Whether to uninstall dependent apps")] bool? uninstallDependents = null,
        [Description("Optional: Whether to delete data and sync clean the extension")] bool? deleteData = null,
        [Description(ConfirmEnvironmentNameDescription)] string? confirmEnvironmentName = null,
        McpServer? server = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        if (deleteData == true)
        {
            var refusal = await d365bcAdminService.ConfirmProductionActionAsync(environmentName, $"UNINSTALL app {appId} and DELETE ITS DATA from", confirmEnvironmentName, ConfirmationPrompt(server));
            if (refusal != null)
            {
                return refusal;
            }
        }

        var request = new UninstallAppRequest
        {
            UseEnvironmentUpdateWindow = useEnvironmentUpdateWindow,
            UninstallDependents = uninstallDependents,
            DeleteData = deleteData
        };

        return ToJson(await d365bcAdminService.uninstallApp(environmentName, appId, request));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets information about app install, uninstall, and update operations for a specific app.")]
    public static async Task<string> get_app_operations(
        [Description("The tenant ID (GUID) for which to get app operations")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app")] Guid appId,
        [Description("Optional: The specific operation ID to retrieve")] Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getAppOperations(environmentName, appId, operationId));
    }

    // Tool output always exposes the identifier as "id", whichever name the API used.
    public async Task<ApiResult<List<InstalledApp>>> getInstalledApps(string environmentName) =>
        (await getInstalledAppsResult(environmentName)).Map(response => (response.Value ?? new List<InstalledApp>()).Select(app =>
        {
            app.Id = app.EffectiveId;
            app.AppId = null;
            return app;
        }).ToList());

    public Task<ApiResult<InstalledAppsResponse>> getInstalledAppsResult(string environmentName) =>
        SendAsync<InstalledAppsResponse>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/apps");

    public async Task<ApiResult<List<AvailableAppUpdate>>> getAvailableAppUpdates(string environmentName) =>
        (await getAvailableAppUpdatesResult(environmentName)).Map(response => response.Value ?? new List<AvailableAppUpdate>());

    public Task<ApiResult<AvailableAppUpdatesResponse>> getAvailableAppUpdatesResult(string environmentName) =>
        SendAsync<AvailableAppUpdatesResponse>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/apps/availableUpdates");

    public Task<ApiResult<UpdateAppResponse>> updateApp(string environmentName, Guid appId, UpdateAppRequest request) =>
        SendAsync<UpdateAppResponse>(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/apps/{appId}/update", request);

    public Task<ApiResult<InstallAppResponse>> installApp(string applicationFamily, string environmentName, Guid appId, InstallAppRequest request) =>
        SendAsync<InstallAppResponse>(HttpMethod.Post, $"{EnvironmentUrl(environmentName, applicationFamily)}/apps/{appId}/install", request);

    public Task<ApiResult<UninstallAppResponse>> uninstallApp(string environmentName, Guid appId, UninstallAppRequest request) =>
        SendAsync<UninstallAppResponse>(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/apps/{appId}/uninstall", request);

    public async Task<ApiResult<List<AppOperation>>> getAppOperations(string environmentName, Guid appId, Guid? operationId = null)
    {
        if (operationId.HasValue)
        {
            return (await getAppOperation(environmentName, appId, operationId.Value)).Map(operation => new List<AppOperation> { operation });
        }

        return (await SendAsync<AppOperationsResponse>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/apps/{appId}/operations"))
            .Map(response => response.Value ?? new List<AppOperation>());
    }

    // The single-operation endpoint returns the operation object directly, while the list endpoint wraps it in "value".
    public async Task<ApiResult<AppOperation>> getAppOperation(string environmentName, Guid appId, Guid operationId)
    {
        var result = await SendAsync<JsonElement>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/apps/{appId}/operations/{operationId}");
        if (result.Error != null)
        {
            return new ApiResult<AppOperation> { StatusCode = result.StatusCode, Error = result.Error, ErrorBody = result.ErrorBody };
        }

        var element = result.Value;
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            element = list.EnumerateArray().FirstOrDefault(op =>
                op.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsed) && parsed == operationId);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ApiResult<AppOperation> { StatusCode = result.StatusCode, Error = $"Operation {operationId} not found" };
        }

        return new ApiResult<AppOperation> { StatusCode = result.StatusCode, Value = element.Deserialize<AppOperation>() };
    }
}
