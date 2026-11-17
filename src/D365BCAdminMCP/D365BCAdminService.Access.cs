using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

public partial class D365BCAdminService
{
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets the Microsoft Entra security group that restricts access to an environment. An empty displayName means the group no longer exists in Microsoft Entra ID.")]
    public static async Task<string> get_environment_security_group(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.getEnvironmentSecurityGroup(environmentName);
        if (result.Error != null)
        {
            return ErrorJson(result.Error);
        }

        // 204 No Content: no group is assigned, so every licensed user can access the environment.
        return result.Value == null
            ? JsonSerializer.Serialize(new { configured = false, message = "No security group is assigned to the environment." })
            : JsonSerializer.Serialize(new { configured = true, id = result.Value.Id, displayName = result.Value.DisplayName });
    }

    [McpServerTool(Destructive = true, Idempotent = true), Description("Restricts access to an environment to the members of a Microsoft Entra security group. Users outside the group lose access, so check the group membership first.")]
    public static async Task<string> set_environment_security_group(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The object ID (GUID) of the Microsoft Entra security group")] Guid securityGroupId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.setEnvironmentSecurityGroup(environmentName, securityGroupId);
        return JsonSerializer.Serialize(new { success = result.Success, environmentName, securityGroupId, error = result.Error }, IgnoreNullsOptions);
    }

    [McpServerTool(Destructive = true, Idempotent = true), Description("Removes the security group restriction from an environment, so every licensed user of the tenant can access it again.")]
    public static async Task<string> clear_environment_security_group(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.clearEnvironmentSecurityGroup(environmentName);
        return JsonSerializer.Serialize(new { success = result.Success, environmentName, error = result.Error }, IgnoreNullsOptions);
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets whether users can access an environment with Microsoft 365 licenses (environment version 21.1 or later).")]
    public static async Task<string> get_access_with_m365_licenses(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getAccessWithM365Licenses(environmentName));
    }

    [McpServerTool(Destructive = false, Idempotent = true), Description("Enables or disables access to an environment with Microsoft 365 licenses (environment version 21.1 or later).")]
    public static async Task<string> set_access_with_m365_licenses(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("True to allow access with Microsoft 365 licenses, false to disallow it")] bool enabled,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.setAccessWithM365Licenses(environmentName, enabled);
        return JsonSerializer.Serialize(new { success = result.Success, environmentName, enabled, error = result.Error }, IgnoreNullsOptions);
    }

    public Task<ApiResult<SecurityGroupAccess>> getEnvironmentSecurityGroup(string environmentName) =>
        SendAsync<SecurityGroupAccess>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/settings/securitygroupaccess");

    public Task<ApiResult<bool>> setEnvironmentSecurityGroup(string environmentName, Guid securityGroupId) =>
        SendWithoutResultAsync(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/settings/securitygroupaccess", new { Value = securityGroupId });

    public Task<ApiResult<bool>> clearEnvironmentSecurityGroup(string environmentName) =>
        SendWithoutResultAsync(HttpMethod.Delete, $"{EnvironmentUrl(environmentName)}/settings/securitygroupaccess");

    public Task<ApiResult<AccessWithM365Licenses>> getAccessWithM365Licenses(string environmentName) =>
        SendAsync<AccessWithM365Licenses>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/settings/accesswithm365licenses");

    public Task<ApiResult<bool>> setAccessWithM365Licenses(string environmentName, bool enabled) =>
        SendWithoutResultAsync(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/settings/accesswithm365licenses", new { enabled });
}
