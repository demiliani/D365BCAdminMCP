using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.ComponentModel;
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

    // [McpServerTool, Description("Creates an extension upload task for a Business Central environment and company.")]
    // public static async Task<string> create_extension_upload(
    //     [Description("The tenant ID (GUID) for which to create the extension upload")] Guid tenantId,
    //     [Description("The name of the environment to create extension upload for")] string environmentName,
    //     [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
    //     [Description("The schedule for the extension upload (e.g., 'Current version', 'Next version')")] string schedule = "Current version",
    //     [Description("The schema synchronization mode (e.g., 'Add', 'Force')")] string schemaSyncMode = "Add")
    // {
    //     var accessToken = await get_microsoft_entra_id_token(tenantId);
    //     var d365bcAdminService = new D365BCAdminService(accessToken);
    //     var uploadResponse = await d365bcAdminService.createExtensionUpload(tenantId, environmentName, companyId, schedule, schemaSyncMode);

    //     if (uploadResponse != null)
    //     {
    //         return JsonSerializer.Serialize(new {
    //             success = true,
    //             extensionUploadId = uploadResponse.SystemId,
    //             schedule = uploadResponse.Schedule,
    //             schemaSyncMode = uploadResponse.SchemaSyncMode,
    //             extensionContent = uploadResponse.ExtensionContent
    //         });
    //     }

    //     return JsonSerializer.Serialize(new { success = false, error = "Unable to create extension upload" });
    // }

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
            return JsonSerializer.Serialize(new {
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
        var uploadResponse = await d365bcAdminService.getExtensionUpload(tenantId, environmentName, companyId, extensionUploadId);

        if (uploadResponse != null)
        {
            return JsonSerializer.Serialize(new {
                success = true,
                systemId = uploadResponse.SystemId,
                schedule = uploadResponse.Schedule,
                schemaSyncMode = uploadResponse.SchemaSyncMode,
                extensionContent = uploadResponse.ExtensionContent
            });
        }

        return JsonSerializer.Serialize(new { success = false, error = "Unable to retrieve extension upload" });
    }

    // [McpServerTool, Description("Uploads the extension content (.app file) for a Business Central extension upload task.")]
    // public static async Task<string> upload_extension_content(
    //     [Description("The tenant ID (GUID) for which to upload the extension content")] Guid tenantId,
    //     [Description("The name of the environment")] string environmentName,
    //     [Description("The company ID (GUID) for the company within the environment")] Guid companyId,
    //     [Description("The extension upload ID (systemId) obtained from create_extension_upload")] string extensionUploadId,
    //     [Description("The local file path to the .app file (e.g., c:\\TMP\\MyExtension_1.0.0.0.app)")] string appFilePath)
    // {
    //     var accessToken = await get_microsoft_entra_id_token(tenantId);
    //     var d365bcAdminService = new D365BCAdminService(accessToken);
    //     var result = await d365bcAdminService.uploadExtensionContent(tenantId, environmentName, companyId, extensionUploadId, appFilePath);

    //     if (result)
    //     {
    //         return JsonSerializer.Serialize(new {
    //             success = true,
    //             message = $"Extension content uploaded successfully for upload ID {extensionUploadId}",
    //             extensionUploadId = extensionUploadId
    //         });
    //     }

    //     return JsonSerializer.Serialize(new { success = false, error = "Unable to upload extension content" });
    // }

    public async Task<ApiResult<List<ExtensionDeploymentStatus>>> getExtensionDeploymentStatus(Guid tenantId, string environmentName, Guid companyId)
    {
        await EnsureAuthenticated(tenantId);
        return (await SendAsync<ExtensionDeploymentStatusResponse>(HttpMethod.Get, $"{AutomationCompanyUrl(environmentName, companyId)}/extensionDeploymentStatus"))
            .Map(response => response.Value ?? new List<ExtensionDeploymentStatus>());
    }

    public async Task<ExtensionUploadResponse?> createExtensionUpload(Guid tenantId, string environmentName, Guid companyId, string schedule, string schemaSyncMode)
    {
        await EnsureAuthenticated(tenantId);
        
        // API endpoint for creating extension upload
        var url = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/microsoft/automation/v2.0/companies({companyId})/extensionUpload";
        
        Log.Debug($"Making Automation API request to: {url}");
        
        try
        {
            // First, try to get existing extension uploads to see the current state
            Log.Debug($"Checking for existing extension uploads...");
            var getResponse = await staticHttpClient.GetAsync(url);
            if (getResponse.IsSuccessStatusCode)
            {
                var existingContent = await getResponse.Content.ReadAsStringAsync();
            }
            
            // Create request body
            var requestBody = new ExtensionUploadRequest
            {
                Schedule = schedule,
                SchemaSyncMode = schemaSyncMode
            };
            
            var jsonContent = JsonSerializer.Serialize(requestBody, ExtensionUploadRequestContext.Default.ExtensionUploadRequest);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
            
            
            // Try POST first (standard REST convention)
            var httpRequest = CreateAuthenticatedRequest(HttpMethod.Post, url);
            httpRequest.Content = content;
            var response = await staticHttpClient.SendAsync(httpRequest);
            Log.Debug($"POST Response status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Log.Debug($"Extension upload created successfully");
                
                var uploadResponse = await response.Content.ReadFromJsonAsync(ExtensionUploadResponseContext.Default.ExtensionUploadResponse);
                return uploadResponse;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Debug($"POST returned BadRequest");
                Log.Error($"Full error response: {errorContent}");
                
                // If POST fails, it's likely because a record already exists
                // Always try to retrieve the existing record
                Log.Debug($"Attempting to retrieve existing extension upload record...");
                
                // Try listing all and getting the first one
                Log.Debug($"Attempting to list all extension uploads to find existing record...");
                var listResponse = await staticHttpClient.GetAsync(url);
                if (listResponse.IsSuccessStatusCode)
                {
                    var listContent = await listResponse.Content.ReadAsStringAsync();
                    
                    // Try to parse as a collection response
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(listContent);
                        if (jsonDoc.RootElement.TryGetProperty("value", out var valueElement))
                        {
                            if (valueElement.ValueKind == JsonValueKind.Array && valueElement.GetArrayLength() > 0)
                            {
                                // Get the first element
                                var firstElement = valueElement[0];
                                var firstRecord = JsonSerializer.Deserialize(firstElement.GetRawText(), ExtensionUploadResponseContext.Default.ExtensionUploadResponse);
                                if (firstRecord != null)
                                {
                                    Log.Debug($"Found existing extension upload in list with ID: {firstRecord.SystemId}");
                                    return firstRecord;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Failed to parse list response: {ex.Message}");
                    }
                }
                else
                {
                    Log.Debug($"Failed to list extension uploads: HTTP {listResponse.StatusCode}");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"HTTP {response.StatusCode} Error response: {errorContent}");
                Log.Error($"Response reason phrase: {response.ReasonPhrase}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in createExtensionUpload: {ex.Message}");
        }

        return null;
    }

    public async Task<bool> uploadExtensionContent(Guid tenantId, string environmentName, Guid companyId, string extensionUploadId, string appFilePath)
    {
        await EnsureAuthenticated(tenantId);
        
        // Validate file exists
        if (!File.Exists(appFilePath))
        {
            Log.Error($"File not found: {appFilePath}");
            return false;
        }
        
        Log.Debug($"Reading file: {appFilePath}");
        
        try
        {
            // Read the .app file as binary FIRST before getting the record
            var fileBytes = await File.ReadAllBytesAsync(appFilePath);
            Log.Debug($"File size: {fileBytes.Length} bytes");
            
            if (fileBytes.Length == 0)
            {
                Log.Error($"File is empty!");
                return false;
            }
            
            // Now GET the extension upload record to retrieve the etag (concurrency token)
            var getUrl = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/microsoft/automation/v2.0/companies({companyId})/extensionUpload({extensionUploadId})";
            Log.Debug($"Getting extension upload record to retrieve etag from: {getUrl}");
            
            var getResponse = await staticHttpClient.GetAsync(getUrl);
            if (!getResponse.IsSuccessStatusCode)
            {
                var errorContent = await getResponse.Content.ReadAsStringAsync();
                Log.Error($"Failed to GET extension upload record: HTTP {getResponse.StatusCode}: {errorContent}");
                return false;
            }
            
            // Extract etag from response headers
            string? etag = null;
            
            if (getResponse.Headers.TryGetValues("etag", out var etagValues))
            {
                etag = etagValues.FirstOrDefault();
            }
            else if (getResponse.Content.Headers.TryGetValues("etag", out var contentEtagValues))
            {
                etag = contentEtagValues.FirstOrDefault();
            }
            else
            {
                // Try case-insensitive search
                var allHeaders = getResponse.Headers.Concat(getResponse.Content.Headers);
                var etagHeader = allHeaders.FirstOrDefault(h => h.Key.Equals("etag", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(etagHeader.Key))
                {
                    etag = etagHeader.Value.FirstOrDefault();
                }
            }
            
            if (string.IsNullOrEmpty(etag))
            {
                // Fallback: use "*" as the wildcard for If-Match
                Log.Warn($"Could not retrieve etag from GET response, using wildcard '*' for If-Match");
                etag = "*";
            }
            
            // Create request with raw binary content (application/octet-stream)
            var patchContent = new ByteArrayContent(fileBytes);
            patchContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            
            Log.Debug($"Binary content size: {fileBytes.Length} bytes");
            Log.Debug($"Content-Type will be: application/octet-stream");
            Log.Debug($"Sending PATCH request with binary content and If-Match header...");
            
            // API endpoint for uploading extension content
            var patchUrl = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/microsoft/automation/v2.0/companies({companyId})/extensionUpload({extensionUploadId})/extensionContent";
            Log.Debug($"PATCH URL: {patchUrl}");
            
            // Create a custom request with If-Match header (must be added to request, not content)
            var request = new HttpRequestMessage(HttpMethod.Patch, patchUrl)
            {
                Content = patchContent
            };
            request.Headers.Add("If-Match", etag);
            // Add authentication
            if (!string.IsNullOrEmpty(_accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }

            var response = await staticHttpClient.SendAsync(request);
            Log.Debug($"PATCH Response status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Log.Debug($"Extension content uploaded successfully");
                Log.Debug($"Response content length: {responseContent.Length}");
                if (!string.IsNullOrEmpty(responseContent))
                {
                }
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"HTTP {response.StatusCode} Error response: {errorContent}");
                Log.Error($"Response reason phrase: {response.ReasonPhrase}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in uploadExtensionContent: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Retrieves the status of an extension upload task.
    /// </summary>
    public async Task<ExtensionUploadResponse?> getExtensionUpload(Guid tenantId, string environmentName, Guid companyId, string extensionUploadId)
    {
        await EnsureAuthenticated(tenantId);
        
        var url = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/microsoft/automation/v2.0/companies({companyId})/extensionUpload({extensionUploadId})";
        
        Log.Debug($"getExtensionUpload called - URL: {url}");
        
        try
        {
            var httpRequest = CreateAuthenticatedRequest(HttpMethod.Get, url);
            var response = await staticHttpClient.SendAsync(httpRequest);
            Log.Debug($"Response status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var uploadResponse = JsonSerializer.Deserialize<ExtensionUploadResponse>(jsonContent, options);
                if (uploadResponse != null)
                {
                    Log.Debug($"Extension upload retrieved successfully");
                    Log.Debug($"Extension Upload ID: {uploadResponse.SystemId}");
                    Log.Debug($"Schedule: {uploadResponse.Schedule}");
                    Log.Debug($"Schema Sync Mode: {uploadResponse.SchemaSyncMode}");
                        // Try to retrieve the media content from the media link
                        var mediaReadLink = jsonContent.Contains("extensionContent@odata.mediaReadLink") ? "Found" : "Not found";
                        Log.Debug($"OData media read link status: {mediaReadLink}");
                    
                        // Extract and try to read the actual content from the media link
                        try
                        {
                            var contentUrl = $"https://api.businesscentral.dynamics.com/v2.0/{url.Split('/')[5]}/api/microsoft/automation/v2.0/companies({url.Split("companies(")[1].Split(")")[0]})/extensionUpload({extensionUploadId})/extensionContent";
                            var contentResponse = await staticHttpClient.GetAsync(contentUrl);
                                Log.Debug($"Attempting to retrieve content from: {contentUrl}");
                            if (contentResponse.IsSuccessStatusCode)
                            {
                                var contentBytes = await contentResponse.Content.ReadAsByteArrayAsync();
                                Log.Debug($"Successfully retrieved extension content: {contentBytes.Length} bytes");
                            }
                            else
                            {
                                Log.Debug($"Could not retrieve content: HTTP {contentResponse.StatusCode}");
                            
                                    // Try to extract the actual media link from the JSON response
                                    try
                                    {
                                        var jsonDoc = JsonDocument.Parse(jsonContent);
                                        if (jsonDoc.RootElement.TryGetProperty("extensionContent@odata.mediaReadLink", out var mediaLinkElement))
                                        {
                                            var actualMediaLink = mediaLinkElement.GetString();
                                            Log.Debug($"Found media read link in JSON: {actualMediaLink}");
                                    
                                            var actualResponse = await staticHttpClient.GetAsync(actualMediaLink);
                                            Log.Debug($"Response from actual media link: HTTP {actualResponse.StatusCode}");
                                            if (actualResponse.IsSuccessStatusCode)
                                            {
                                                var contentBytes2 = await actualResponse.Content.ReadAsByteArrayAsync();
                                                Log.Debug($"Successfully retrieved extension content from media link: {contentBytes2.Length} bytes");
                                            }
                                        }
                                    }
                                    catch (Exception parseEx)
                                    {
                                        Log.Debug($"Exception parsing media link: {parseEx.Message}");
                                    }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"Exception retrieving content: {ex.Message}");
                        }
                    
                        return uploadResponse;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"HTTP {response.StatusCode} Error response: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in getExtensionUpload: {ex.Message}");
        }

        return null;
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

    /// <summary>
    /// Triggers deployment of the uploaded extension by updating its schedule property.
    /// This signals the system to deploy the extension to the environment.
    /// </summary>
    public async Task<bool> triggerExtensionDeployment(Guid tenantId, string environmentName, Guid companyId, string extensionUploadId, string schedule = "Current version")
    {
        await EnsureAuthenticated(tenantId);
        
        Log.Debug($"triggerExtensionDeployment called - Extension ID: {extensionUploadId}, Schedule: {schedule}");
        
        try
        {
            // First, GET the record to get its etag
            var getUrl = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/microsoft/automation/v2.0/companies({companyId})/extensionUpload({extensionUploadId})";
            Log.Debug($"Getting extension upload record for etag...");
            
            var getResponse = await staticHttpClient.GetAsync(getUrl);
            if (!getResponse.IsSuccessStatusCode)
            {
                Log.Error($"Failed to GET extension record: HTTP {getResponse.StatusCode}");
                return false;
            }
            
            // Get the etag from response
            string? etag = null;
            if (getResponse.Headers.TryGetValues("etag", out var etagValues))
            {
                etag = etagValues.FirstOrDefault();
            }
            
            if (string.IsNullOrEmpty(etag))
            {
                etag = "*"; // Fallback to wildcard
                Log.Warn($"No etag found, using wildcard");
            }
            else
            {
            }
            
            // PATCH the extension upload record to update schedule (which triggers deployment)
            var updateRequest = new { schedule = schedule };
            var updateJson = JsonSerializer.Serialize(updateRequest);
            var patchContent = new StringContent(updateJson, System.Text.Encoding.UTF8, "application/json");
            
            var patchRequest = new HttpRequestMessage(HttpMethod.Patch, getUrl)
            {
                Content = patchContent
            };
            patchRequest.Headers.Add("If-Match", etag);
            
            Log.Debug($"Sending PATCH to update schedule and trigger deployment...");
            var patchResponse = await staticHttpClient.SendAsync(patchRequest);
            Log.Debug($"PATCH response status: {patchResponse.StatusCode}");
            
            if (patchResponse.IsSuccessStatusCode)
            {
                var responseContent = await patchResponse.Content.ReadAsStringAsync();
                Log.Debug($"Extension deployment triggered successfully");
                if (!string.IsNullOrEmpty(responseContent))
                {
                }
                return true;
            }
            else
            {
                var errorContent = await patchResponse.Content.ReadAsStringAsync();
                Log.Error($"PATCH failed: HTTP {patchResponse.StatusCode}");
                Log.Debug($"Error: {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in triggerExtensionDeployment: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Calls the Microsoft.NAV.upload bound action to actually install the extension.
    /// This must be called after uploading the extension content.
    /// </summary>
    public async Task<bool> invokeExtensionUploadAction(Guid tenantId, string environmentName, Guid companyId, string extensionUploadId)
    {
        await EnsureAuthenticated(tenantId);

        Log.Debug($"invokeExtensionUploadAction called - Extension ID: {extensionUploadId}");

        try
        {
            // Call the bound action Microsoft.NAV.upload to trigger the actual installation
            var actionUrl = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/microsoft/automation/v2.0/companies({companyId})/extensionUpload({extensionUploadId})/Microsoft.NAV.upload";
            
            Log.Debug($"Calling Microsoft.NAV.upload bound action at: {actionUrl}");
            
            // POST with no body (or empty body) to the action
            var request = new HttpRequestMessage(HttpMethod.Post, actionUrl);
            // Don't set Content-Type for action invocation - let it be null or use default
            // Add authentication
            if (!string.IsNullOrEmpty(_accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }
            
            Log.Debug($"Sending POST request to invoke upload action...");
            var response = await staticHttpClient.SendAsync(request);
            
            Log.Debug($"Microsoft.NAV.upload response status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Log.Debug($"Extension upload action invoked successfully");
                if (!string.IsNullOrEmpty(responseContent))
                {
                }
                else
                {
                    Log.Debug($"Response has no content (expected for action invocation)");
                }
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"Microsoft.NAV.upload failed: HTTP {response.StatusCode} {response.ReasonPhrase}");
                Log.Error($"Error response: {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in invokeExtensionUploadAction: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Creates a Professional Tools Extension (PTE) upload task and uploads the extension content in a single operation.
    /// This method combines the functionality of createExtensionUpload and uploadExtensionContent.
    /// </summary>
    public async Task<PteUploadResult> createPteUpload(Guid tenantId, string environmentName, Guid companyId, string appFilePath, string schedule = "Current version", string schemaSyncMode = "Add")
    {
        Log.Debug($"createPteUpload called - Environment: {environmentName}, Company: {companyId}, File: {appFilePath}");
        
        try
        {
            // Extract app name from file (without extension)
            var appName = Path.GetFileNameWithoutExtension(appFilePath);
            Log.Debug($"App name extracted from file: {appName}");
            
            // Step 1: Create the extension upload task
            Log.Debug($"Step 1: Creating extension upload task");
            var uploadResponse = await createExtensionUpload(tenantId, environmentName, companyId, schedule, schemaSyncMode);
            
            if (uploadResponse == null)
            {
                Log.Error($"Failed to create extension upload task");
                return new PteUploadResult { Success = false, Error = "Failed to create extension upload task", AppName = appName };
            }
            
            var extensionUploadId = uploadResponse.SystemId;
            if (string.IsNullOrEmpty(extensionUploadId))
            {
                Log.Error($"Extension upload ID is null or empty");
                return new PteUploadResult { Success = false, Error = "Extension upload ID is null or empty", AppName = appName };
            }
            
            Log.Debug($"Extension upload task created successfully with ID: {extensionUploadId}");
            
            // Step 2: Upload the extension content
            Log.Debug($"Step 2: Uploading extension content from file: {appFilePath}");
            Log.Debug($"Using extensionUploadId: {extensionUploadId} for PATCH request");
            var uploadSuccess = await uploadExtensionContent(tenantId, environmentName, companyId, extensionUploadId, appFilePath);
            
            if (!uploadSuccess)
            {
                Log.Error($"Failed to upload extension content");
                return new PteUploadResult { 
                    Success = false, 
                    Error = "Failed to upload extension content",
                    ExtensionUploadId = extensionUploadId,
                    AppName = appName
                };
            }
            
            Log.Debug($"Extension content uploaded successfully");

            // Step 3: Invoke the Microsoft.NAV.upload bound action to actually install the extension
            Log.Debug($"Step 3: Invoking Microsoft.NAV.upload action to install the extension");
            var uploadActionSuccess = await invokeExtensionUploadAction(tenantId, environmentName, companyId, extensionUploadId);
            
            if (!uploadActionSuccess)
            {
                Log.Warn($"Upload action invocation failed");
                return new PteUploadResult {
                    Success = false,
                    Error = "Failed to invoke Microsoft.NAV.upload action",
                    ExtensionUploadId = extensionUploadId,
                    AppName = appName
                };
            }

            Log.Debug($"PTE upload completed successfully");
            return new PteUploadResult { 
                Success = true,
                ExtensionUploadId = extensionUploadId,
                Schedule = uploadResponse.Schedule,
                SchemaSyncMode = uploadResponse.SchemaSyncMode,
                AppName = appName
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in createPteUpload: {ex.Message}");
            return new PteUploadResult { Success = false, Error = $"Exception: {ex.Message}", AppName = Path.GetFileNameWithoutExtension(appFilePath) };
        }
    }
}
