using System.Text.Json;
using System.Text.RegularExpressions;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

public partial class D365BCAdminService
{
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets how many database exports are allowed per month for an environment and how many are left this month.")]
    public static async Task<string> get_database_export_metrics(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getDatabaseExportMetrics(environmentName));
    }

    [McpServerTool(Destructive = false), Description("Starts exporting the database of a Business Central environment as a .bacpac file to an Azure Storage account. Requires a paid subscription, the D365 BACKUP/RESTORE permission set, and an Azure standard (not premium) storage account; each export uses one of the monthly exports (see get_database_export_metrics). Asynchronous: track it with get_environment_operations. The SAS URI is never logged or returned, but it is part of this conversation, so use a short-lived one.")]
    public static async Task<string> start_database_export(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment to export")] string environmentName,
        [Description("Blob service SAS URI of the Azure Storage account, with Read, Write, Create and Delete permissions (https://<account>.blob.core.windows.net/?sv=...&sig=...)")] string storageAccountSasUri,
        [Description("The container to export to (3-63 lowercase letters, digits and hyphens); created if it does not exist")] string container,
        [Description("Optional: The blob (file) name. Defaults to '<environment>-<UTC timestamp>.bacpac'")] string? blob = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateDatabaseExport(storageAccountSasUri, container, blob);
        if (validationError != null)
        {
            return JsonSerializer.Serialize(new { success = false, error = validationError });
        }

        var blobName = string.IsNullOrWhiteSpace(blob) ? $"{environmentName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bacpac" : blob.Trim();
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.startDatabaseExport(environmentName, storageAccountSasUri.Trim(), container.Trim(), blobName);

        return JsonSerializer.Serialize(new
        {
            success = result.Success,
            environmentName,
            container = container.Trim(),
            blob = blobName,
            message = result.Success ? "Database export started; track it with get_environment_operations." : null,
            error = result.Error
        }, IgnoreNullsOptions);
    }

    private static readonly Regex ContainerNamePattern = new("^(?!.*--)[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$");

    internal static string? ValidateDatabaseExport(string storageAccountSasUri, string container, string? blob)
    {
        if (!Uri.TryCreate(storageAccountSasUri?.Trim(), UriKind.Absolute, out var sasUri) || sasUri.Scheme != Uri.UriSchemeHttps
            || !sasUri.Query.Contains("sig=", StringComparison.OrdinalIgnoreCase))
        {
            return "storageAccountSasUri must be an https SAS URI of the storage account (it must contain a 'sig' parameter).";
        }

        if (!ContainerNamePattern.IsMatch(container?.Trim() ?? string.Empty))
        {
            return $"'{container}' is not a valid container name: use 3-63 lowercase letters, digits and single hyphens, starting and ending with a letter or digit.";
        }

        if (blob != null && (blob.Trim().Length > 1024 || blob.Contains('\\')))
        {
            return $"'{blob}' is not a valid blob name.";
        }

        return null;
    }

    private static string ExportUrl(string environmentName, string applicationFamily = "BusinessCentral") =>
        $"{AdminApiBaseUrl}/exports/applications/{Uri.EscapeDataString(applicationFamily)}/environments/{Uri.EscapeDataString(environmentName)}";

    public Task<ApiResult<DatabaseExportMetrics>> getDatabaseExportMetrics(string environmentName) =>
        SendAsync<DatabaseExportMetrics>(HttpMethod.Get, $"{ExportUrl(environmentName)}/metrics");

    public Task<ApiResult<bool>> startDatabaseExport(string environmentName, string storageAccountSasUri, string container, string blob)
    {
        Log.Info($"Starting database export of {environmentName} to container '{container}', blob '{blob}'");
        return SendWithoutResultAsync(HttpMethod.Post, ExportUrl(environmentName), new { storageAccountSasUri, container, blob });
    }
}
