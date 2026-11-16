using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

public partial class D365BCAdminService
{
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets environment information for a Dynamics 365 Business Central tenant.")]
    public static async Task<string> get_environment_informations([Description("The tenant ID (GUID) for which to get environment information")] Guid tenantId, CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getEnvironmentInformations(tenantId));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets the update window settings for a specific Business Central environment.")]
    public static async Task<string> get_environment_update_window(
        [Description("The tenant ID (GUID) for which to get environment update window information")] Guid tenantId,
        [Description("The name of the environment to get update window for")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getEnvironmentUpdateWindow(environmentName, tenantId));
    }

    [McpServerTool(Destructive = false, Idempotent = true), Description("Sets the update window settings for a specific Business Central environment.")]
    public static async Task<string> set_environment_update_window(
        [Description("The tenant ID (GUID) for which to set environment update window")] Guid tenantId,
        [Description("The name of the environment to set update window for")] string environmentName,
        [Description("The preferred start time (HH:mm format, e.g., '22:00')")] string preferredStartTime,
        [Description("The preferred end time (HH:mm format, e.g., '06:00')")] string preferredEndTime,
        [Description("The Windows time zone identifier (e.g., 'UTC', 'Eastern Standard Time')")] string timeZoneId = "UTC",
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.setEnvironmentUpdateWindow(environmentName, preferredStartTime, preferredEndTime, timeZoneId));
    }

    [McpServerTool(Destructive = false, Idempotent = true), Description("Sets the Application Insights connection string for a specific Business Central environment.")]
    public static async Task<string> set_environment_application_insights_connection_string(
        [Description("The tenant ID (GUID) for which to set Application Insights connection string")] Guid tenantId,
        [Description("The name of the environment to set Application Insights connection string for")] string environmentName,
        [Description("The Application Insights connection string/key to set")] string appInsightsKey,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var response = await d365bcAdminService.setEnvironmentApplicationInsightsConnectionString(environmentName, appInsightsKey);

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Destructive = false), Description("Creates a new Business Central environment. The creation is asynchronous: the returned operation can be tracked with get_environment_operations.")]
    public static async Task<string> create_new_environment(
        [Description("The tenant ID (GUID) for which to create the environment")] Guid tenantId,
        [Description("The name of the new environment to create")] string environmentName,
        [Description("The type of environment to create (Sandbox, Production, etc.)")] string environmentType,
        [Description("The country code for the environment (e.g., US, IT, GB)")] string countryCode,
        [Description("Optional: The ring to create the environment in (e.g. 'Preview', only for Sandbox environments). Defaults to the production ring.")] string? ringName = null,
        [Description("Optional: The application version to create the environment on (e.g. '26.1.12345.0'). Defaults to the latest available version.")] string? applicationVersion = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.createNewEnvironment(environmentName, environmentType, countryCode, ringName, applicationVersion));
    }

    [McpServerTool(Destructive = false), Description("Copies an existing Business Central environment to create a new one. The copy is asynchronous: the returned operation can be tracked with get_environment_operations.")]
    public static async Task<string> copy_environment(
        [Description("The tenant ID (GUID) for which to copy the environment")] Guid tenantId,
        [Description("The name of the source environment to copy from")] string sourceEnvironmentName,
        [Description("The name of the new environment to create")] string newEnvironmentName,
        [Description("The type of the new environment (Sandbox, Production, etc.)")] string newEnvironmentType,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.copyEnvironment(sourceEnvironmentName, newEnvironmentName, newEnvironmentType));
    }

    [McpServerTool(Destructive = true), Description("Deletes a Business Central environment. Returns HTTP status code 202 (Accepted) with operation details. Expected errors: invalidStatusCannotDeleteTenant (can't delete in current state), tenantDeletionInProgress (already being deleted), ambiguousRequest (multiple environments with same name), environmentNotFound (environment not found).")]
    public static async Task<string> delete_environment(
        [Description("The tenant ID (GUID) for which to delete the environment")] Guid tenantId,
        [Description("The name of the environment to delete")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.deleteEnvironment(environmentName));
    }

    [McpServerTool(Destructive = false), Description("Recovers a soft-deleted Business Central environment (API v2.18+). Asynchronous: track the returned operation with get_environment_operations.")]
    public static async Task<string> recover_environment(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the deleted environment to recover")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.recoverEnvironment(environmentName));
    }

    [McpServerTool(Destructive = true), Description("Renames a Business Central environment. Integrations and URLs that use the old name stop working. Asynchronous: track the returned operation with get_environment_operations.")]
    public static async Task<string> rename_environment(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The current name of the environment")] string environmentName,
        [Description("The new name of the environment (max 30 characters, starts with a letter; letters, digits, '-' and '_')")] string newEnvironmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.renameEnvironment(environmentName, newEnvironmentName));
    }

    [McpServerTool(Destructive = false), Description("Restores a Business Central environment to a point in time into a NEW environment (the source environment is not changed). Use get_available_restore_periods to find valid points in time. Asynchronous: track the returned operation with get_environment_operations.")]
    public static async Task<string> restore_environment(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment to restore")] string environmentName,
        [Description("The name of the new environment created by the restore")] string newEnvironmentName,
        [Description("The type of the new environment: 'Production' or 'Sandbox'")] string environmentType,
        [Description("The point in time to restore to, in ISO 8601 format (UTC), e.g. '2026-11-15T20:00:00Z'")] DateTime pointInTime,
        [Description("Optional: Do not install per-tenant extensions in the restored environment (default: false)")] bool skipInstallingPtes = false,
        [Description("Optional: Do not install third-party AppSource apps in the restored environment (default: false)")] bool skipInstallingThirdPartyGlobalApps = false,
        [Description("Optional: Skip the cleanup that disables integrations and selected setups in the restored environment (default: false)")] bool skipEnvironmentCleanup = false,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.restoreEnvironment(environmentName, newEnvironmentName, environmentType, pointInTime,
            skipInstallingPtes, skipInstallingThirdPartyGlobalApps, skipEnvironmentCleanup));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Lists the periods to which a Business Central environment can be restored, with the application version each period restores to.")]
    public static async Task<string> get_available_restore_periods(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getAvailableRestorePeriods(environmentName));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets environment operations (create, copy, delete, rename, restore, recover, updates, app installs, ...) and their status. Without environmentName it returns the operations of all environments; with operationId it returns that single operation.")]
    public static async Task<string> get_environment_operations(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("Optional: The name of the environment")] string? environmentName = null,
        [Description("Optional: The ID of a specific operation, e.g. the id returned by create_new_environment or copy_environment")] Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getEnvironmentOperations(environmentName, operationId));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets the tenant quotas: maximum number of production and sandbox environments and storage limits.")]
    public static async Task<string> get_environment_quotas(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getEnvironmentQuotas());
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets storage usage information for a specific Business Central environment.")]
    public static async Task<string> get_usage_storage_for_environment(
        [Description("The tenant ID (GUID) for which to get storage usage")] Guid tenantId,
        [Description("The name of the environment to get storage usage for")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getUsageStorageForEnvironment(environmentName));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets storage usage information for all Business Central environments in the tenant.")]
    public static async Task<string> get_usage_storage_for_all_environments(
        [Description("The tenant ID (GUID) for which to get storage usage for all environments")] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getUsageStorageForAllEnvironments());
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets information about available updates for a specific Business Central environment.")]
    public static async Task<string> get_environment_updates(
        [Description("The tenant ID (GUID) for which to get environment updates")] Guid tenantId,
        [Description("The name of the environment to get updates for")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getEnvironmentUpdates(environmentName));
    }

    [McpServerTool(Destructive = false), Description("Sets or schedules an update for a specific Business Central environment and target version.")]
    public static async Task<string> set_environment_update(
        [Description("The tenant ID (GUID) for which to set the environment update")] Guid tenantId,
        [Description("The name of the environment to update")] string environmentName,
        [Description("The target version to schedule (e.g., '26.1')")] string targetVersion,
        [Description("Optional: Set to true to select this target version for update")] bool? selected = null,
        [Description("Optional: The type of target version ('GA' or 'Preview')")] string? targetVersionType = null,
        [Description("Optional: The scheduled datetime for the update")] DateTime? selectedDateTime = null,
        [Description("Optional: Whether to ignore the environment update window")] bool? ignoreUpdateWindow = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);

        var request = new SetEnvironmentUpdateRequest
        {
            Selected = selected,
            TargetVersionType = targetVersionType
        };

        if (selectedDateTime.HasValue || ignoreUpdateWindow.HasValue)
        {
            request.ScheduleDetails = new SetEnvironmentUpdateScheduleDetails
            {
                SelectedDateTime = selectedDateTime,
                IgnoreUpdateWindow = ignoreUpdateWindow
            };
        }

        return ToJson(await d365bcAdminService.setEnvironmentUpdate(environmentName, targetVersion, request));
    }

    public async Task<ApiResult<List<Environment>>> getEnvironmentInformations(Guid? tenantId = null)
    {
        await EnsureAuthenticated(tenantId ?? Guid.Empty);

        var result = await SendAsync<EnvironmentResponse>(HttpMethod.Get, $"{AdminApiBaseUrl}/applications/BusinessCentral/environments");
        return result.Map(response => (response.Value ?? new List<EnvironmentData>()).Select(env => new Environment
        {
            Name = env.Name,
            Type = env.Type,
            CountryCode = env.CountryCode,
            TenantId = env.AadTenantId,
            Status = env.Status,
            LocationName = env.LocationName,
            GeoName = env.GeoName,
            AppInsightsKey = env.AppInsightsKey
        }).ToList());
    }

    public async Task<ApiResult<EnvironmentUpdateWindow>> getEnvironmentUpdateWindow(string environmentName, Guid tenantId)
    {
        var result = await SendAsync<EnvironmentUpdateWindow>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/settings/upgrade");
        return result.Map(window =>
        {
            window.TenantId = tenantId;
            window.EnvironmentName = environmentName;
            return window;
        });
    }

    public Task<ApiResult<EnvironmentUpdateWindowResponse>> setEnvironmentUpdateWindow(string environmentName, string preferredStartTime, string preferredEndTime, string timeZoneId)
    {
        // Wall-time + time zone format, API v2.13+
        var requestBody = new
        {
            preferredStartTime,
            preferredEndTime,
            timeZoneId
        };

        return SendAsync<EnvironmentUpdateWindowResponse>(HttpMethod.Put, $"{EnvironmentUrl(environmentName)}/settings/upgrade", requestBody);
    }

    public async Task<ApplicationInsightsKeyResponse> setEnvironmentApplicationInsightsConnectionString(string environmentName, string appInsightsKey)
    {
        var result = await SendWithoutResultAsync(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/settings/appinsightskey", new { key = appInsightsKey });

        return new ApplicationInsightsKeyResponse
        {
            Success = result.Success,
            Message = result.Success ? $"HTTP {result.StatusCode}" : result.Error
        };
    }

    public Task<ApiResult<EnvironmentOperation>> createNewEnvironment(string environmentName, string environmentType, string countryCode, string? ringName = null, string? applicationVersion = null)
    {
        var requestBody = new
        {
            environmentType,
            countryCode,
            ringName = string.IsNullOrWhiteSpace(ringName) ? null : ringName.Trim(),
            applicationVersion = string.IsNullOrWhiteSpace(applicationVersion) ? null : applicationVersion.Trim()
        };

        return SendAsync<EnvironmentOperation>(HttpMethod.Put, EnvironmentUrl(environmentName), requestBody);
    }

    public Task<ApiResult<EnvironmentOperation>> copyEnvironment(string sourceEnvironmentName, string newEnvironmentName, string newEnvironmentType)
    {
        var requestBody = new
        {
            environmentName = newEnvironmentName,
            type = newEnvironmentType
        };

        return SendAsync<EnvironmentOperation>(HttpMethod.Post, $"{EnvironmentUrl(sourceEnvironmentName)}/copy", requestBody);
    }

    public Task<ApiResult<EnvironmentDeletionResponse>> deleteEnvironment(string environmentName) =>
        SendAsync<EnvironmentDeletionResponse>(HttpMethod.Delete, EnvironmentUrl(environmentName));

    public Task<ApiResult<EnvironmentOperation>> recoverEnvironment(string environmentName) =>
        SendAsync<EnvironmentOperation>(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/recover");

    public Task<ApiResult<EnvironmentOperation>> renameEnvironment(string environmentName, string newEnvironmentName) =>
        SendAsync<EnvironmentOperation>(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/rename", new { NewEnvironmentName = newEnvironmentName });

    public Task<ApiResult<EnvironmentOperation>> restoreEnvironment(string environmentName, string newEnvironmentName, string environmentType, DateTime pointInTime,
        bool skipInstallingPtes, bool skipInstallingThirdPartyGlobalApps, bool skipEnvironmentCleanup)
    {
        var requestBody = new
        {
            EnvironmentName = newEnvironmentName,
            EnvironmentType = environmentType,
            PointInTime = pointInTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            SkipInstallingPTEs = skipInstallingPtes,
            SkipInstallingThirdPartyGlobalApps = skipInstallingThirdPartyGlobalApps,
            SkipEnvironmentCleanup = skipEnvironmentCleanup
        };

        return SendAsync<EnvironmentOperation>(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/restore", requestBody);
    }

    public async Task<ApiResult<List<RestorePeriod>>> getAvailableRestorePeriods(string environmentName) =>
        (await SendAsync<RestorePeriodsResponse>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/availableRestorePeriods"))
            .Map(response => response.Value ?? new List<RestorePeriod>());

    public async Task<ApiResult<List<EnvironmentOperation>>> getEnvironmentOperations(string? environmentName = null, Guid? operationId = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(environmentName) ? $"{AdminApiBaseUrl}/environments" : EnvironmentUrl(environmentName);
        if (operationId.HasValue)
        {
            return (await SendAsync<EnvironmentOperation>(HttpMethod.Get, $"{baseUrl}/operations/{operationId}"))
                .Map(operation => new List<EnvironmentOperation> { operation });
        }

        return (await SendAsync<EnvironmentOperationsResponse>(HttpMethod.Get, $"{baseUrl}/operations"))
            .Map(response => response.Value ?? new List<EnvironmentOperation>());
    }

    public Task<ApiResult<EnvironmentQuotas>> getEnvironmentQuotas() =>
        SendAsync<EnvironmentQuotas>(HttpMethod.Get, $"{AdminApiBaseUrl}/environments/quotas");

    public Task<ApiResult<EnvironmentStorageUsage>> getUsageStorageForEnvironment(string environmentName) =>
        SendAsync<EnvironmentStorageUsage>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/usedstorage");

    public async Task<ApiResult<List<EnvironmentStorageUsage>>> getUsageStorageForAllEnvironments() =>
        (await SendAsync<EnvironmentStorageUsageListResponse>(HttpMethod.Get, $"{AdminApiBaseUrl}/environments/usedstorage"))
            .Map(response => response.Value ?? new List<EnvironmentStorageUsage>());

    public async Task<ApiResult<List<EnvironmentUpdateInfo>>> getEnvironmentUpdates(string environmentName) =>
        (await SendAsync<EnvironmentUpdatesListResponse>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/updates"))
            .Map(response => response.Value ?? new List<EnvironmentUpdateInfo>());

    public Task<ApiResult<SetEnvironmentUpdateResponse>> setEnvironmentUpdate(string environmentName, string targetVersion, SetEnvironmentUpdateRequest? request = null) =>
        SendAsync<SetEnvironmentUpdateResponse>(HttpMethod.Patch, $"{EnvironmentUrl(environmentName)}/updates/{Uri.EscapeDataString(targetVersion)}", request);
}
