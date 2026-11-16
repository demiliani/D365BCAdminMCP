using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

public partial class D365BCAdminService
{
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets active sessions for a specific Business Central environment.")]
    public static async Task<string> get_active_sessions(
        [Description("The tenant ID (GUID) for which to get active sessions")] Guid tenantId,
        [Description("The application family (e.g., 'BusinessCentral')")] string applicationFamily,
        [Description("The name of the environment to get active sessions for")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getActiveSessions(applicationFamily, environmentName));
    }

    [McpServerTool(Destructive = true), Description("Terminates and deletes an active session for a Business Central environment.")]
    public static async Task<string> kill_active_sessions(
        [Description("The tenant ID (GUID) for which to kill the session")] Guid tenantId,
        [Description("The application family (e.g., 'BusinessCentral')")] string applicationFamily,
        [Description("The name of the environment")] string environmentName,
        [Description("The session ID to terminate")] int sessionId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.killActiveSessions(applicationFamily, environmentName, sessionId);

        return JsonSerializer.Serialize(new { success = result.Success, sessionId = sessionId, error = result.Error }, IgnoreNullsOptions);
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets a list of notification recipients configured for Business Central environments.")]
    public static async Task<string> get_environment_notification_recipient(
        [Description("The tenant ID (GUID) for which to get notification recipients")] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getEnvironmentNotificationRecipient());
    }

    [McpServerTool(Destructive = false), Description("Creates a new notification recipient for Business Central environments.")]
    public static async Task<string> create_environment_notification_recipient(
        [Description("The tenant ID (GUID) for which to create the notification recipient")] Guid tenantId,
        [Description("The email address of the notification recipient")] string email,
        [Description("The full name of the notification recipient")] string name,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.createEnvironmentNotificationRecipient(email, name));
    }

    [McpServerTool(Destructive = true), Description("Deletes a notification recipient for Business Central environments.")]
    public static async Task<string> delete_environment_notification_recipient(
        [Description("The tenant ID (GUID) for which to delete the notification recipient")] Guid tenantId,
        [Description("The unique identifier of the notification recipient to delete")] Guid recipientId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.deleteEnvironmentNotificationRecipient(recipientId);

        return JsonSerializer.Serialize(new { success = result.Success, recipientId = recipientId, error = result.Error }, IgnoreNullsOptions);
    }

    public async Task<ApiResult<List<ActiveSession>>> getActiveSessions(string applicationFamily, string environmentName) =>
        (await SendAsync<ActiveSessionsResponse>(HttpMethod.Get, $"{EnvironmentUrl(environmentName, applicationFamily)}/sessions"))
            .Map(response => response.Value ?? new List<ActiveSession>());

    public Task<ApiResult<bool>> killActiveSessions(string applicationFamily, string environmentName, int sessionId) =>
        SendWithoutResultAsync(HttpMethod.Delete, $"{EnvironmentUrl(environmentName, applicationFamily)}/sessions/{sessionId}");

    public async Task<ApiResult<List<NotificationRecipient>>> getEnvironmentNotificationRecipient() =>
        (await SendAsync<NotificationRecipientsResponse>(HttpMethod.Get, $"{AdminApiBaseUrl}/settings/notification/recipients"))
            .Map(response => response.Value ?? new List<NotificationRecipient>());

    public Task<ApiResult<NotificationRecipient>> createEnvironmentNotificationRecipient(string email, string name) =>
        SendAsync<NotificationRecipient>(HttpMethod.Put, $"{AdminApiBaseUrl}/settings/notification/recipients", new CreateNotificationRecipientRequest
        {
            Email = email,
            Name = name
        });

    public Task<ApiResult<bool>> deleteEnvironmentNotificationRecipient(Guid recipientId) =>
        SendWithoutResultAsync(HttpMethod.Delete, $"{AdminApiBaseUrl}/settings/notification/recipients/{recipientId}");
}
