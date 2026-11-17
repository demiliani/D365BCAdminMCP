using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

// Extension upload through the Business Central Automation API, superseded by install_pte (Admin Center API).
// Microsoft removes PTE upload from the Automation API in 2027 release wave 1; these tools are kept only for
// environments that cannot use the Admin Center API yet and should be deleted once that deadline passes.
public partial class D365BCAdminService
{
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Gets the extension deployment status for a specific Business Central environment and company.")]
    public static async Task<string> get_extension_deployment_status(
        [Description("The tenant ID (GUID) for which to get extension deployment status")] Guid tenantId,
        [Description("The name of the environment to get extension deployment status for")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        return ToJson(await d365bcAdminService.getExtensionDeploymentStatus(tenantId, environmentName, companyId));
    }

    [McpServerTool(Destructive = false), Description("DEPRECATED: uses the Automation API, whose PTE upload is being removed in 2027 release wave 1. Prefer install_pte. Creates a Per-Tenant Extension (PTE) upload task and uploads the extension content (.app file) in a single operation.")]
    public static async Task<string> create_pte_upload(
        [Description("The tenant ID (GUID) for which to create the PTE upload")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
        [Description("The local file path to the .app file (e.g., c:\\TMP\\MyExtension_1.0.0.0.app)")] string appFilePath,
        [Description("The schedule for the extension upload (e.g., 'Current version', 'Next version')")] string schedule = "Current version",
        [Description("The schema synchronization mode (e.g., 'Add', 'Force')")] string schemaSyncMode = "Add",
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.createPteUpload(tenantId, environmentName, companyId, appFilePath, schedule, schemaSyncMode);

        if (result.Success)
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                message = "PTE upload created and extension content uploaded successfully",
                extensionUploadId = result.ExtensionUploadId,
                schedule = result.Schedule,
                schemaSyncMode = result.SchemaSyncMode
            });
        }

        return JsonSerializer.Serialize(new { success = false, error = result.Error });
    }

    [McpServerTool(Destructive = false), Description("DEPRECATED: uses the Automation API, whose PTE upload is being removed in 2027 release wave 1. Prefer install_ptes_from_folder. Creates Per-Tenant Extension uploads for all .app files in a folder sequentially, waiting for each deployment to complete before processing the next file.")]
    public static async Task<string> create_pte_uploads_from_folder(
        [Description("The tenant ID (GUID) for which to create the PTE uploads")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
        [Description("The local folder path containing .app files (e.g., c:\\MYFOLDER)")] string folderPath,
        [Description("The schedule for the extension upload (e.g., 'Current version', 'Next version')")] string schedule = "Current version",
        [Description("The schema synchronization mode (e.g., 'Add', 'Force')")] string schemaSyncMode = "Add",
        [Description("Optional: Maximum time to wait for deployment to complete in seconds (default: 600)")] int maxWaitSeconds = 600,
        [Description("Optional: Polling interval in milliseconds (default: 5000)")] int pollIntervalMs = 5000,
        CancellationToken cancellationToken = default)
    {
        // Validate folder
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Folder not found: {folderPath}"
            });
        }

        // Find .app files
        var appFiles = Directory.GetFiles(folderPath, "*.app", SearchOption.TopDirectoryOnly);
        if (appFiles.Length == 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "No .app files found in the specified folder",
                folder = folderPath
            });
        }

        var service = await CreateServiceAsync(tenantId, cancellationToken);

        var successes = new List<object>();
        var failures = new List<object>();

        foreach (var appFile in appFiles)
        {
            try
            {
                Log.Debug($"Processing file: {appFile}");
                var result = await service.createPteUpload(tenantId, environmentName, companyId, appFile, schedule, schemaSyncMode);
                Log.Debug($"Result.Success: {result.Success}, Error: {result.Error ?? "none"}");

                if (result.Success)
                {
                    Log.Debug($"Upload created for {appFile}. Now waiting for deployment to complete...");

                    // Wait for deployment to complete for this specific app
                    var deploymentCompleted = await service.waitForDeploymentCompletion(tenantId, environmentName, companyId, result.AppName ?? Path.GetFileNameWithoutExtension(appFile), maxWaitSeconds, pollIntervalMs);

                    if (deploymentCompleted)
                    {
                        successes.Add(new
                        {
                            file = appFile,
                            extensionUploadId = result.ExtensionUploadId,
                            schedule = result.Schedule,
                            schemaSyncMode = result.SchemaSyncMode,
                            deploymentStatus = "Completed",
                            appName = result.AppName
                        });
                        Log.Debug($"Deployment completed successfully for {appFile}");
                    }
                    else
                    {
                        failures.Add(new
                        {
                            file = appFile,
                            error = $"Deployment did not complete within {maxWaitSeconds} seconds"
                        });
                        Log.Error($"Deployment timeout for {appFile}");
                    }
                }
                else
                {
                    failures.Add(new
                    {
                        file = appFile,
                        error = result.Error
                    });
                    Log.Error($"Failed to process {appFile}: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Exception processing {appFile}: {ex.Message}");
                failures.Add(new
                {
                    file = appFile,
                    error = ex.Message
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = failures.Count == 0,
            totalFiles = appFiles.Length,
            succeeded = successes.Count,
            failed = failures.Count,
            successes,
            failures
        });
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Retrieves the status of an extension upload task for a Business Central environment and company.")]
    public static async Task<string> get_extension_upload(
        [Description("The tenant ID (GUID) for which to get the extension upload")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
        [Description("The extension upload ID (systemId) to retrieve")] string extensionUploadId,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await d365bcAdminService.getExtensionUpload(tenantId, environmentName, companyId, extensionUploadId);

        if (result.Success)
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                systemId = result.Value!.SystemId,
                schedule = result.Value.Schedule,
                schemaSyncMode = result.Value.SchemaSyncMode,
                extensionContent = result.Value.ExtensionContent
            });
        }

        return JsonSerializer.Serialize(new { success = false, error = result.Error ?? "Empty response from the Business Central API" });
    }

    public async Task<ApiResult<List<ExtensionDeploymentStatus>>> getExtensionDeploymentStatus(Guid tenantId, string environmentName, Guid companyId)
    {
        await EnsureAuthenticated(tenantId);
        return (await SendAsync<ExtensionDeploymentStatusResponse>(HttpMethod.Get, $"{AutomationCompanyUrl(environmentName, companyId)}/extensionDeploymentStatus"))
            .Map(response => response.Value ?? new List<ExtensionDeploymentStatus>());
    }

    private static string ExtensionUploadUrl(string environmentName, Guid companyId) =>
        $"{AutomationCompanyUrl(environmentName, companyId)}/extensionUpload";

    private static string ExtensionUploadUrl(string environmentName, Guid companyId, string extensionUploadId) =>
        $"{ExtensionUploadUrl(environmentName, companyId)}({Uri.EscapeDataString(extensionUploadId)})";

    // A company has a single extension upload record: the POST answers 400 when it already exists, and the existing record is reused.
    public async Task<ApiResult<ExtensionUploadResponse>> createExtensionUpload(Guid tenantId, string environmentName, Guid companyId, string schedule, string schemaSyncMode)
    {
        await EnsureAuthenticated(tenantId);
        var url = ExtensionUploadUrl(environmentName, companyId);

        var created = await SendAsync<ExtensionUploadResponse>(HttpMethod.Post, url, new ExtensionUploadRequest { Schedule = schedule, SchemaSyncMode = schemaSyncMode });
        if (created.Success || created.StatusCode != (int)HttpStatusCode.BadRequest)
        {
            return created;
        }

        Log.Debug($"Creating the extension upload returned {created.Error}; looking for an existing record");
        var existing = await SendAsync<ExtensionUploadListResponse>(HttpMethod.Get, url);
        var record = existing.Value?.Value?.FirstOrDefault();
        return record != null
            ? new ApiResult<ExtensionUploadResponse> { StatusCode = existing.StatusCode, Value = record }
            : created;
    }

    // Returns null on success, otherwise the reason of the failure.
    public async Task<string?> uploadExtensionContent(Guid tenantId, string environmentName, Guid companyId, string extensionUploadId, string appFilePath)
    {
        await EnsureAuthenticated(tenantId);

        if (!File.Exists(appFilePath))
        {
            return $"File not found: {appFilePath}";
        }

        var fileBytes = await File.ReadAllBytesAsync(appFilePath, CancellationToken);
        if (fileBytes.Length == 0)
        {
            return $"File is empty: {appFilePath}";
        }

        // The content PATCH needs the record's concurrency token, which Business Central returns as @odata.etag.
        var recordUrl = ExtensionUploadUrl(environmentName, companyId, extensionUploadId);
        var record = await SendAsync<JsonElement>(HttpMethod.Get, recordUrl);
        if (record.Error != null)
        {
            return $"Failed to read the extension upload record: {record.Error}";
        }

        var etag = record.Value.ValueKind == JsonValueKind.Object && record.Value.TryGetProperty("@odata.etag", out var etagElement)
            ? etagElement.GetString()
            : null;
        if (string.IsNullOrEmpty(etag))
        {
            Log.Warn("The extension upload record has no @odata.etag; using If-Match: *");
            etag = "*";
        }

        Log.Debug($"Uploading {fileBytes.Length} bytes from {appFilePath}");
        var result = await SendAsync<JsonElement?>(() =>
        {
            var request = CreateAuthenticatedRequest(HttpMethod.Patch, $"{recordUrl}/extensionContent");
            request.Content = new ByteArrayContent(fileBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return request;
        });

        return result.Error == null ? null : $"Failed to upload extension content: {result.Error}";
    }

    public async Task<ApiResult<ExtensionUploadResponse>> getExtensionUpload(Guid tenantId, string environmentName, Guid companyId, string extensionUploadId)
    {
        await EnsureAuthenticated(tenantId);
        return await SendAsync<ExtensionUploadResponse>(HttpMethod.Get, ExtensionUploadUrl(environmentName, companyId, extensionUploadId));
    }

    public async Task<bool> waitForDeploymentCompletion(Guid tenantId, string environmentName, Guid companyId, string appName, int maxWaitSeconds = 300, int pollIntervalMs = 5000)
    {
        Log.Debug($"Waiting for all deployments to complete (max {maxWaitSeconds}s, polling every {pollIntervalMs}ms)...");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var maxWaitMs = maxWaitSeconds * 1000;

        while (stopwatch.ElapsedMilliseconds < maxWaitMs)
        {
            try
            {
                var deploymentStatuses = (await getExtensionDeploymentStatus(tenantId, environmentName, companyId)).Value;

                if (deploymentStatuses != null && deploymentStatuses.Count > 0)
                {
                    // Check if there are any deployments still in progress
                    var inProgressDeployments = deploymentStatuses.Where(ds =>
                        ds.Status != null && ds.Status.Equals("InProgress", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (inProgressDeployments.Count == 0)
                    {
                        // No InProgress deployments - we can proceed with next app
                        Log.Debug($"No 'InProgress' deployments found. Ready for next app!");
                        foreach (var status in deploymentStatuses)
                        {
                            Log.Debug($"   - {status.Name} ({status.Publisher}): {status.Status}");
                        }
                        return true;
                    }

                    // Log current in-progress deployments
                    Log.Debug($"Still have InProgress deployments (elapsed: {stopwatch.ElapsedMilliseconds}ms):");
                    foreach (var status in inProgressDeployments)
                    {
                        Log.Debug($"   - {status.Name} ({status.Publisher}): {status.Status}");
                    }

                    // Also show completed ones for context
                    var completedDeployments = deploymentStatuses.Where(ds =>
                        ds.Status != null && !ds.Status.Equals("InProgress", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (completedDeployments.Count > 0)
                    {
                        Log.Debug($"   Completed:");
                        foreach (var status in completedDeployments)
                        {
                            Log.Debug($"   - {status.Name} ({status.Publisher}): {status.Status}");
                        }
                    }
                }
                else
                {
                    Log.Debug($"No deployment statuses found yet, waiting...");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Error checking deployment status: {ex.Message}");
            }

            // Wait before next poll
            await Task.Delay(pollIntervalMs, CancellationToken);
        }

        Log.Debug($"Deployment polling timeout after {maxWaitSeconds} seconds");
        return false;
    }

    // Microsoft.NAV.upload is the bound action that installs the uploaded extension. Returns null on success, otherwise the reason of the failure.
    public async Task<string?> invokeExtensionUploadAction(Guid tenantId, string environmentName, Guid companyId, string extensionUploadId)
    {
        await EnsureAuthenticated(tenantId);
        var result = await SendWithoutResultAsync(HttpMethod.Post, $"{ExtensionUploadUrl(environmentName, companyId, extensionUploadId)}/Microsoft.NAV.upload");
        return result.Error == null ? null : $"Failed to invoke Microsoft.NAV.upload: {result.Error}";
    }

    /// <summary>
    /// Creates the extension upload task, uploads the .app content and invokes Microsoft.NAV.upload to install it.
    /// </summary>
    public async Task<PteUploadResult> createPteUpload(Guid tenantId, string environmentName, Guid companyId, string appFilePath, string schedule = "Current version", string schemaSyncMode = "Add")
    {
        var appName = Path.GetFileNameWithoutExtension(appFilePath);
        Log.Debug($"createPteUpload called - Environment: {environmentName}, Company: {companyId}, File: {appFilePath}");

        PteUploadResult Failed(string error, string? extensionUploadId = null)
        {
            Log.Error($"PTE upload of {appName} failed: {error}");
            return new PteUploadResult { Success = false, Error = error, ExtensionUploadId = extensionUploadId, AppName = appName };
        }

        try
        {
            var upload = await createExtensionUpload(tenantId, environmentName, companyId, schedule, schemaSyncMode);
            if (!upload.Success)
            {
                return Failed($"Failed to create extension upload task: {upload.Error ?? "empty response from the Business Central API"}");
            }

            var extensionUploadId = upload.Value!.SystemId;
            if (string.IsNullOrEmpty(extensionUploadId))
            {
                return Failed("The extension upload task returned by Business Central has no systemId");
            }

            var contentError = await uploadExtensionContent(tenantId, environmentName, companyId, extensionUploadId, appFilePath);
            if (contentError != null)
            {
                return Failed(contentError, extensionUploadId);
            }

            var actionError = await invokeExtensionUploadAction(tenantId, environmentName, companyId, extensionUploadId);
            if (actionError != null)
            {
                return Failed(actionError, extensionUploadId);
            }

            Log.Debug($"PTE upload of {appName} completed");
            return new PteUploadResult
            {
                Success = true,
                ExtensionUploadId = extensionUploadId,
                Schedule = upload.Value.Schedule,
                SchemaSyncMode = upload.Value.SchemaSyncMode,
                AppName = appName
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed($"Cannot read {appFilePath}: {ex.Message}");
        }
    }
}
