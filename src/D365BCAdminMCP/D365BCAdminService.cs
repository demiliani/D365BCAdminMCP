using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Azure.Core;
using Azure.Identity;

namespace D365BCAdminMCP;

[McpServerToolType]
public class D365BCAdminService
{
    HttpClient httpClient;
    private string? _accessToken;
    
    // Static token cache for sharing across method calls
    private static readonly Dictionary<Guid, CachedToken> _tokenCache = new();
    private static readonly object _tokenCacheLock = new object();

    // Token cache class
    private class CachedToken
    {
        public string Token { get; set; } = string.Empty;
        public DateTimeOffset ExpiresOn { get; set; }
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresOn.AddMinutes(-5); // Refresh 5 minutes before expiry
    }

    public D365BCAdminService()
    {
        this.httpClient = new HttpClient();
    }

    public D365BCAdminService(string? accessToken) : this()
    {
        _accessToken = accessToken;

        if (!string.IsNullOrEmpty(accessToken))
        {
            this.httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }
    }

    public void SetAccessToken(string accessToken)
    {
        _accessToken = accessToken;
        this.httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task EnsureAuthenticated(Guid tenantId)
    {
        Console.WriteLine($"🔍 [DEBUG] EnsureAuthenticated called with tenantId: {tenantId}");
        
        if (httpClient.DefaultRequestHeaders.Authorization != null)
        {
            Console.WriteLine($"🔍 [DEBUG] Already authenticated with token: {httpClient.DefaultRequestHeaders.Authorization.Parameter?.Substring(0, 50)}...");
            return; // Already authenticated
        }

        Console.WriteLine($"🔍 [DEBUG] Getting new token using DefaultAzureCredential");
        try
        {
            var credential = new DefaultAzureCredential();
            var tokenRequestContext = new TokenRequestContext(new[] { "https://api.businesscentral.dynamics.com/.default" });
            var tokenResult = await credential.GetTokenAsync(tokenRequestContext);
            
            Console.WriteLine($"🔍 [DEBUG] Token acquired successfully, length: {tokenResult.Token.Length}");
            Console.WriteLine($"🔍 [DEBUG] Token expires at: {tokenResult.ExpiresOn}");
            
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);
            Console.WriteLine($"🔍 [DEBUG] Authorization header set");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [DEBUG] Authentication failed: {ex.Message}");
            Console.WriteLine($"❌ [DEBUG] Full exception: {ex}");
            throw new InvalidOperationException($"Failed to authenticate: {ex.Message}", ex);
        }
    }

    private async Task<string> GetAutomationApiTokenAsync(Guid tenantId)
    {
        Console.WriteLine($"🔍 [DEBUG] Getting Automation API token for tenantId: {tenantId}");
        
        try
        {
            var credential = new DefaultAzureCredential();
            var tokenRequestContext = new TokenRequestContext(new[] { "Automation.ReadWrite.All" });
            var tokenResult = await credential.GetTokenAsync(tokenRequestContext);
            
            Console.WriteLine($"🔍 [DEBUG] Automation API token acquired successfully, length: {tokenResult.Token.Length}");
            Console.WriteLine($"🔍 [DEBUG] Automation API token expires at: {tokenResult.ExpiresOn}");
            
            return tokenResult.Token;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [DEBUG] Automation API authentication failed: {ex.Message}");
            Console.WriteLine($"❌ [DEBUG] Full exception: {ex}");
            throw new InvalidOperationException($"Failed to authenticate for Automation API: {ex.Message}", ex);
        }
    }

    #region MCP Tools

    [McpServerTool, Description("Gets a Microsoft Entra ID token using interactive authentication for the specified tenant.")]
    public static async Task<string> get_microsoft_entra_id_token([Description("The tenant ID (GUID) for which to acquire the token")] Guid tenantId)
    {
        // Check if we have a valid cached token
        lock (_tokenCacheLock)
        {
            if (_tokenCache.TryGetValue(tenantId, out var cachedToken) && !cachedToken.IsExpired)
            {
                Console.WriteLine($"🔄 [DEBUG] Using cached token for tenant {tenantId}, expires: {cachedToken.ExpiresOn}");
                return cachedToken.Token;
            }
        }

        Console.WriteLine($"🔑 [DEBUG] Acquiring new token for tenant {tenantId}");
        
        // Try DefaultAzureCredential first (includes Azure CLI, managed identity, etc.)
        var tokenRequestContext = new TokenRequestContext(new[] { "https://api.businesscentral.dynamics.com/.default" });
        
        try
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId.ToString() });
            var tokenResult = await credential.GetTokenAsync(tokenRequestContext);
            
            // Cache the new token
            lock (_tokenCacheLock)
            {
                _tokenCache[tenantId] = new CachedToken
                {
                    Token = tokenResult.Token,
                    ExpiresOn = tokenResult.ExpiresOn
                };
            }
            
            Console.WriteLine($"✅ [DEBUG] New token acquired and cached for tenant {tenantId}, expires: {tokenResult.ExpiresOn}");
            return tokenResult.Token;
        }
        catch
        {
            // Fall back to interactive browser credential if DefaultAzureCredential fails
            var interactiveCredential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenantId.ToString() });
            var tokenResult = await interactiveCredential.GetTokenAsync(tokenRequestContext);
            
            // Cache the new token
            lock (_tokenCacheLock)
            {
                _tokenCache[tenantId] = new CachedToken
                {
                    Token = tokenResult.Token,
                    ExpiresOn = tokenResult.ExpiresOn
                };
            }
            
            Console.WriteLine($"✅ [DEBUG] New token acquired via interactive auth and cached for tenant {tenantId}, expires: {tokenResult.ExpiresOn}");
            return tokenResult.Token;
        }
    }

    [McpServerTool, Description("Retrieves the tenant ID (GUID) for a given tenant name by querying the Azure AD well-known configuration endpoint.")]
    public static async Task<string> get_tenant_id_from_tenant_name(
        [Description("The tenant name (e.g. 'contoso.onmicrosoft.com')")] string tenantName)
    {
        try
        {
            var d365bcAdminService = new D365BCAdminService();
            var tenantId = await d365bcAdminService.getTenantIDFromTenantName(tenantName);
            return JsonSerializer.Serialize(new { 
                success = true, 
                tenantId = tenantId,
                tenantName = tenantName
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [DEBUG] get_tenant_id_from_tenant_name failed: {ex.Message}");
            return JsonSerializer.Serialize(new { 
                success = false, 
                error = ex.Message,
                tenantName = tenantName
            });
        }
    }

    [McpServerTool, Description("Gets environment information for a Dynamics 365 Business Central tenant.")]
    public static async Task<string> get_environment_informations([Description("The tenant ID (GUID) for which to get environment information")] Guid tenantId)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var environments = await d365bcAdminService.getEnvironmentInformations(tenantId);
        return JsonSerializer.Serialize(environments);
    }

    [McpServerTool, Description("Gets the update window settings for a specific Business Central environment.")]
    public static async Task<string> get_environment_update_window(
        [Description("The tenant ID (GUID) for which to get environment update window information")] Guid tenantId,
        [Description("The name of the environment to get update window for")] string environmentName)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var updateWindow = await d365bcAdminService.getEnvironmentUpdateWindow(environmentName, tenantId);

        if (updateWindow != null)
        {
            return JsonSerializer.Serialize(updateWindow);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve update window information" });
    }

    [McpServerTool, Description("Sets the update window settings for a specific Business Central environment.")]
    public static async Task<string> set_environment_update_window(
        [Description("The tenant ID (GUID) for which to set environment update window")] Guid tenantId,
        [Description("The name of the environment to set update window for")] string environmentName,
        [Description("The preferred start time (HH:mm format, e.g., '22:00')")] string preferredStartTime,
        [Description("The preferred end time (HH:mm format, e.g., '06:00')")] string preferredEndTime,
        [Description("The Windows time zone identifier (e.g., 'UTC', 'Eastern Standard Time')")] string timeZoneId = "UTC")
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var updateWindowResponse = await d365bcAdminService.setEnvironmentUpdateWindow(environmentName, preferredStartTime, preferredEndTime, timeZoneId);

        if (updateWindowResponse != null)
        {
            return JsonSerializer.Serialize(updateWindowResponse);
        }

        return JsonSerializer.Serialize(new { error = "Unable to set update window information" });
    }

    [McpServerTool, Description("Sets the Application Insights connection string for a specific Business Central environment.")]
    public static async Task<string> set_app_insights_key(
        [Description("The tenant ID (GUID) for which to set Application Insights connection string")] Guid tenantId,
        [Description("The name of the environment to set Application Insights connection string for")] string environmentName,
        [Description("The Application Insights connection string/key to set")] string appInsightsKey)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var response = await d365bcAdminService.setEnvironmentApplicationInsightsConnectionString(environmentName, appInsightsKey);

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool, Description("Creates a new Business Central environment.")]
    public static async Task<string> create_new_environment(
        [Description("The tenant ID (GUID) for which to create the environment")] Guid tenantId,
        [Description("The name of the new environment to create")] string environmentName,
        [Description("The type of environment to create (Sandbox, Production, etc.)")] string environmentType,
        [Description("The country code for the environment (e.g., US, IT, GB)")] string countryCode)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var creationResponse = await d365bcAdminService.createNewEnvironment(environmentName, environmentType, countryCode);

        if (creationResponse != null)
        {
            return JsonSerializer.Serialize(creationResponse);
        }

        return JsonSerializer.Serialize(new { error = "Unable to create environment" });
    }

    [McpServerTool, Description("Copies an existing Business Central environment to create a new one.")]
    public static async Task<string> copy_environment(
        [Description("The tenant ID (GUID) for which to copy the environment")] Guid tenantId,
        [Description("The name of the source environment to copy from")] string sourceEnvironmentName,
        [Description("The name of the new environment to create")] string newEnvironmentName,
        [Description("The type of the new environment (Sandbox, Production, etc.)")] string newEnvironmentType)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var copyResponse = await d365bcAdminService.copyEnvironment(sourceEnvironmentName, newEnvironmentName, newEnvironmentType);

        if (copyResponse != null)
        {
            return JsonSerializer.Serialize(copyResponse);
        }

        return JsonSerializer.Serialize(new { error = "Unable to copy environment" });
    }

    [McpServerTool, Description("Gets storage usage information for a specific Business Central environment.")]
    public static async Task<string> get_environment_storage_usage(
        [Description("The tenant ID (GUID) for which to get storage usage")] Guid tenantId,
        [Description("The name of the environment to get storage usage for")] string environmentName)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var storageUsage = await d365bcAdminService.getUsageStorageForEnvironment(environmentName);

        if (storageUsage != null)
        {
            return JsonSerializer.Serialize(storageUsage);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve storage usage information" });
    }

    [McpServerTool, Description("Gets storage usage information for all Business Central environments in the tenant.")]
    public static async Task<string> get_all_environments_storage_usage(
        [Description("The tenant ID (GUID) for which to get storage usage for all environments")] Guid tenantId)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var storageUsageList = await d365bcAdminService.getUsageStorageForAllEnvironments();

        if (storageUsageList != null)
        {
            return JsonSerializer.Serialize(storageUsageList);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve storage usage information for all environments" });
    }

    [McpServerTool, Description("Gets information about available updates for a specific Business Central environment.")]
    public static async Task<string> get_available_environment_updates(
        [Description("The tenant ID (GUID) for which to get environment updates")] Guid tenantId,
        [Description("The name of the environment to get updates for")] string environmentName)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var updates = await d365bcAdminService.getEnvironmentUpdates(environmentName);

        if (updates != null)
        {
            return JsonSerializer.Serialize(updates);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve environment updates" });
    }

    [McpServerTool, Description("Sets or schedules an update for a specific Business Central environment and target version.")]
    public static async Task<string> schedule_environment_update(
        [Description("The tenant ID (GUID) for which to set the environment update")] Guid tenantId,
        [Description("The name of the environment to update")] string environmentName,
        [Description("The target version to schedule (e.g., '26.1')")] string targetVersion,
        [Description("Optional: Set to true to select this target version for update")] bool? selected = null,
        [Description("Optional: The type of target version ('GA' or 'Preview')")] string? targetVersionType = null,
        [Description("Optional: The scheduled datetime for the update")] DateTime? selectedDateTime = null,
        [Description("Optional: Whether to ignore the environment update window")] bool? ignoreUpdateWindow = null)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);

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

        var updateResponse = await d365bcAdminService.setEnvironmentUpdate(environmentName, targetVersion, request);

        if (updateResponse != null)
        {
            return JsonSerializer.Serialize(updateResponse);
        }

        return JsonSerializer.Serialize(new { error = "Unable to set environment update" });
    }

    [McpServerTool, Description("Gets active sessions for a specific Business Central environment.")]
    public static async Task<string> get_active_sessions(
        [Description("The tenant ID (GUID) for which to get active sessions")] Guid tenantId,
        [Description("The application family (e.g., 'BusinessCentral')")] string applicationFamily,
        [Description("The name of the environment to get active sessions for")] string environmentName)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var sessions = await d365bcAdminService.getActiveSessions(applicationFamily, environmentName);

        if (sessions != null)
        {
            return JsonSerializer.Serialize(sessions);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve active sessions" });
    }

    [McpServerTool, Description("Terminates and deletes an active session for a Business Central environment.")]
    public static async Task<string> kill_active_sessions(
        [Description("The tenant ID (GUID) for which to kill the session")] Guid tenantId,
        [Description("The application family (e.g., 'BusinessCentral')")] string applicationFamily,
        [Description("The name of the environment")] string environmentName,
        [Description("The session ID to terminate")] int sessionId)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var success = await d365bcAdminService.killActiveSessions(applicationFamily, environmentName, sessionId);

        return JsonSerializer.Serialize(new { success = success, sessionId = sessionId });
    }

    [McpServerTool, Description("Gets a list of notification recipients configured for Business Central environments.")]
    public static async Task<string> get_notification_recipients(
        [Description("The tenant ID (GUID) for which to get notification recipients")] Guid tenantId)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var recipients = await d365bcAdminService.getEnvironmentNotificationRecipient();

        if (recipients != null)
        {
            return JsonSerializer.Serialize(recipients);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve notification recipients" });
    }

    [McpServerTool, Description("Creates a new notification recipient for Business Central environments.")]
    public static async Task<string> create_notification_recipient(
        [Description("The tenant ID (GUID) for which to create the notification recipient")] Guid tenantId,
        [Description("The email address of the notification recipient")] string email,
        [Description("The full name of the notification recipient")] string name)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var recipient = await d365bcAdminService.createEnvironmentNotificationRecipient(email, name);

        if (recipient != null)
        {
            return JsonSerializer.Serialize(recipient);
        }

        return JsonSerializer.Serialize(new { error = "Unable to create notification recipient" });
    }

    [McpServerTool, Description("Deletes a notification recipient for Business Central environments.")]
    public static async Task<string> delete_notification_recipient(
        [Description("The tenant ID (GUID) for which to delete the notification recipient")] Guid tenantId,
        [Description("The unique identifier of the notification recipient to delete")] Guid recipientId)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var success = await d365bcAdminService.deleteEnvironmentNotificationRecipient(recipientId);

        return JsonSerializer.Serialize(new { success = success, recipientId = recipientId });
    }

    [McpServerTool, Description("Gets information about apps that are installed on a Business Central environment.")]
    public static async Task<string> get_installed_apps(
        [Description("The tenant ID (GUID) for which to get installed apps")] Guid tenantId,
        [Description("The name of the environment to get installed apps for")] string environmentName)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var apps = await d365bcAdminService.getInstalledApps(environmentName);

        if (apps != null)
        {
            return JsonSerializer.Serialize(apps);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve installed apps" });
    }

    [McpServerTool, Description("Gets information about new app versions that are available for apps currently installed on a Business Central environment.")]
    public static async Task<string> get_available_app_updates(
        [Description("The tenant ID (GUID) for which to get available app updates")] Guid tenantId,
        [Description("The name of the environment to get available app updates for")] string environmentName)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var updates = await d365bcAdminService.getAvailableAppUpdates(environmentName);

        if (updates != null)
        {
            // Transform to the expected output format with "id" instead of "appId"
            var transformedUpdates = updates.Select(update => new
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

        return JsonSerializer.Serialize(new { error = "Unable to retrieve available app updates" });
    }

    [McpServerTool, Description("Updates an app in a Business Central environment to a specified target version.")]
    public static async Task<string> update_app(
        [Description("The tenant ID (GUID) for which to update the app")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app to update")] Guid appId,
        [Description("The target version to update to")] string targetVersion,
        [Description("Optional: Whether to use the environment update window")] bool? useEnvironmentUpdateWindow = null,
        [Description("Optional: Whether to allow preview versions")] bool? allowPreviewVersion = null,
        [Description("Optional: Whether to install or update needed dependencies")] bool? installOrUpdateNeededDependencies = null)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);

        var request = new UpdateAppRequest
        {
            TargetVersion = targetVersion,
            UseEnvironmentUpdateWindow = useEnvironmentUpdateWindow,
            AllowPreviewVersion = allowPreviewVersion,
            InstallOrUpdateNeededDependencies = installOrUpdateNeededDependencies
        };

        var updateResponse = await d365bcAdminService.updateApp(environmentName, appId, request);

        if (updateResponse != null)
        {
            return JsonSerializer.Serialize(updateResponse);
        }

        return JsonSerializer.Serialize(new { error = "Unable to update app" });
    }

    [McpServerTool, Description("Uninstalls an app from a Business Central environment.")]
    public static async Task<string> uninstall_app(
        [Description("The tenant ID (GUID) for which to uninstall the app")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app to uninstall")] Guid appId,
        [Description("Optional: Whether to use the environment update window")] bool? useEnvironmentUpdateWindow = null,
        [Description("Optional: Whether to uninstall dependent apps")] bool? uninstallDependents = null,
        [Description("Optional: Whether to delete data and sync clean the extension")] bool? deleteData = null)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);

        var request = new UninstallAppRequest
        {
            UseEnvironmentUpdateWindow = useEnvironmentUpdateWindow,
            UninstallDependents = uninstallDependents,
            DeleteData = deleteData
        };

        var uninstallResponse = await d365bcAdminService.uninstallApp(environmentName, appId, request);

        if (uninstallResponse != null)
        {
            return JsonSerializer.Serialize(uninstallResponse);
        }

        return JsonSerializer.Serialize(new { error = "Unable to uninstall app" });
    }

    [McpServerTool, Description("Gets information about app install, uninstall, and update operations for a specific app.")]
    public static async Task<string> get_app_operations(
        [Description("The tenant ID (GUID) for which to get app operations")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app")] Guid appId,
        [Description("Optional: The specific operation ID to retrieve")] Guid? operationId = null)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var operations = await d365bcAdminService.getAppOperations(environmentName, appId, operationId);

        if (operations != null)
        {
            return JsonSerializer.Serialize(operations);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve app operations" });
    }

    [McpServerTool, Description("Gets a list of available features and their status in the feature management of a specific Business Central environment.")]
    public static async Task<string> get_available_features(
        [Description("The tenant ID (GUID) for which to get available features")] Guid tenantId,
        [Description("The name of the environment to get features for")] string environmentName,
        [Description("The company ID (GUID) for the company within the environment")] Guid companyId)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var features = await d365bcAdminService.getAvailableFeatures(tenantId, environmentName, companyId);

        if (features != null)
        {
            return JsonSerializer.Serialize(features);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve available features" });
    }

    [McpServerTool, Description("Gets a list of companies available in a specific Business Central environment.")]
    public static async Task<string> get_companies(
        [Description("The tenant ID (GUID) for which to get environment companies")] Guid tenantId,
        [Description("The name of the environment to get companies for")] string environmentName)
    {
        var accessToken = await get_microsoft_entra_id_token(tenantId);
        var d365bcAdminService = new D365BCAdminService(accessToken);
        var companies = await d365bcAdminService.getEnvironmentCompanies(environmentName);

        if (companies != null)
        {
            return JsonSerializer.Serialize(companies);
        }

        return JsonSerializer.Serialize(new { error = "Unable to retrieve environment companies" });
    }

    [McpServerTool, Description("Clears the cached authentication token for the specified tenant, forcing a new login on the next API call.")]
    public static Task<string> clear_cached_token([Description("The tenant ID (GUID) for which to clear the cached token")] Guid tenantId)
    {
        lock (_tokenCacheLock)
        {
            if (_tokenCache.Remove(tenantId))
            {
                Console.WriteLine($"🗑️ [DEBUG] Cleared cached token for tenant {tenantId}");
                return Task.FromResult(JsonSerializer.Serialize(new { success = true, message = $"Token cleared for tenant {tenantId}" }));
            }
            else
            {
                Console.WriteLine($"⚠️ [DEBUG] No cached token found for tenant {tenantId}");
                return Task.FromResult(JsonSerializer.Serialize(new { success = false, message = $"No cached token found for tenant {tenantId}" }));
            }
        }
    }

    [McpServerTool, Description("Shows the status of cached authentication tokens for all tenants.")]
    public static Task<string> get_token_cache_status()
    {
        lock (_tokenCacheLock)
        {
            var status = _tokenCache.Select(kvp => new
            {
                tenantId = kvp.Key,
                expiresOn = kvp.Value.ExpiresOn,
                isExpired = kvp.Value.IsExpired,
                timeUntilExpiry = kvp.Value.IsExpired ? "Expired" : (kvp.Value.ExpiresOn - DateTimeOffset.UtcNow).ToString(@"hh\:mm\:ss")
            }).ToList();

            return Task.FromResult(JsonSerializer.Serialize(new { 
                totalCachedTokens = _tokenCache.Count,
                tokens = status 
            }));
        }
    }

    #endregion

    #region MCP Tool Implementations
    /// <summary>
    /// Retrieves the tenant ID (GUID) for a given tenant name by querying the Azure AD well-known configuration endpoint.
    /// </summary>
    /// <param name="tenantName">The tenant name (e.g., "3t.bike" or "contoso.onmicrosoft.com")</param>
    /// <returns>The tenant ID as a GUID</returns>
    public async Task<Guid> getTenantIDFromTenantName(string tenantName)
    {
        if (string.IsNullOrWhiteSpace(tenantName))
        {
            throw new ArgumentException("Tenant name cannot be null or empty.", nameof(tenantName));
        }

        try
        {
            Console.WriteLine($"🔍 [DEBUG] GetTenantIDFromTenantName called with tenantName: {tenantName}");
            
            // Construct the well-known OpenID configuration endpoint
            var url = $"https://login.microsoftonline.com/{tenantName}/.well-known/openid-configuration";
            Console.WriteLine($"🔍 [DEBUG] Making request to: {url}");
            
            var response = await httpClient.GetAsync(url);
            Console.WriteLine($"🔍 [DEBUG] Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ [DEBUG] Failed to retrieve tenant configuration: {response.StatusCode} - {errorContent}");
                throw new InvalidOperationException($"Failed to retrieve tenant configuration for '{tenantName}': HTTP {response.StatusCode}");
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"🔍 [DEBUG] Response content length: {jsonContent.Length}");
            
            // Parse the JSON response to extract the tenant ID from the token_endpoint
            var jsonDocument = JsonDocument.Parse(jsonContent);
            var root = jsonDocument.RootElement;

            if (!root.TryGetProperty("token_endpoint", out var tokenEndpointElement))
            {
                Console.WriteLine($"❌ [DEBUG] 'token_endpoint' property not found in response");
                throw new InvalidOperationException("'token_endpoint' property not found in OpenID configuration response");
            }

            var tokenEndpoint = tokenEndpointElement.GetString();
            if (string.IsNullOrEmpty(tokenEndpoint))
            {
                Console.WriteLine($"❌ [DEBUG] 'token_endpoint' value is null or empty");
                throw new InvalidOperationException("'token_endpoint' value is empty");
            }

            Console.WriteLine($"🔍 [DEBUG] Token endpoint: {tokenEndpoint}");

            // Extract the tenant ID (GUID) from the token_endpoint URL
            // Example: https://login.microsoftonline.com/d0de2a8d-d356-4558-99bc-61e6d62a55e8/oauth2/token
            // We need to extract: d0de2a8d-d356-4558-99bc-61e6d62a55e8
            
            var uri = new Uri(tokenEndpoint);
            var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (pathSegments.Length == 0)
            {
                Console.WriteLine($"❌ [DEBUG] Could not extract path segments from token_endpoint");
                throw new InvalidOperationException("Could not extract tenant ID from token_endpoint URL");
            }

            var tenantIdString = pathSegments[0];
            Console.WriteLine($"🔍 [DEBUG] Extracted tenant ID string: {tenantIdString}");

            if (!Guid.TryParse(tenantIdString, out var tenantId))
            {
                Console.WriteLine($"❌ [DEBUG] Failed to parse '{tenantIdString}' as a GUID");
                throw new InvalidOperationException($"Failed to parse tenant ID '{tenantIdString}' as a valid GUID");
            }

            Console.WriteLine($"✅ [DEBUG] Successfully retrieved tenant ID: {tenantId}");
            return tenantId;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"❌ [DEBUG] HTTP request failed: {ex.Message}");
            throw new InvalidOperationException($"HTTP request to retrieve tenant configuration failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"❌ [DEBUG] JSON parsing failed: {ex.Message}");
            throw new InvalidOperationException($"Failed to parse OpenID configuration response: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [DEBUG] Unexpected error: {ex.Message}");
            throw new InvalidOperationException($"An unexpected error occurred while retrieving tenant ID: {ex.Message}", ex);
        }
    }

    public async Task<List<Environment>> getEnvironmentInformations(Guid? tenantId = null)
    {
        Console.WriteLine($"🔍 [DEBUG] getEnvironmentInformations called with tenantId: {tenantId}");
        
        // Ensure we have authentication
        await EnsureAuthenticated(tenantId ?? Guid.Empty);
        
        // Use the same API endpoint that works in PowerShell - don't filter by tenant ID in query
        var url = "https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments";
        Console.WriteLine($"� [DEBUG] Making API call to: {url}");
        
        var response = await httpClient.GetAsync(url);
        Console.WriteLine($"� [DEBUG] Response status: {response.StatusCode}");
        Console.WriteLine($"🔍 [DEBUG] Response headers: {response.Headers}");

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"� [DEBUG] Raw response content length: {responseContent.Length}");
            Console.WriteLine($"🔍 [DEBUG] Raw response content: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");
            
            var environmentsResponse = await response.Content.ReadFromJsonAsync(EnvironmentResponseContext.Default.EnvironmentResponse);
            Console.WriteLine($"🔍 [DEBUG] Deserialized response: {environmentsResponse}");
            Console.WriteLine($"🔍 [DEBUG] Response.Value: {environmentsResponse?.Value}");
            Console.WriteLine($"🔍 [DEBUG] Response.Value.Count: {environmentsResponse?.Value?.Count}");

            if (environmentsResponse != null && environmentsResponse.Value != null)
            {
                Console.WriteLine($"🔍 [DEBUG] Processing {environmentsResponse.Value.Count} environment objects");
                
                foreach (var env in environmentsResponse.Value.Take(1)) // Just debug the first one
                {
                    Console.WriteLine($"🔍 [DEBUG] First env - Name: '{env.Name}', Type: '{env.Type}', Status: '{env.Status}'");
                    Console.WriteLine($"🔍 [DEBUG] First env - FriendlyName: '{env.FriendlyName}', CountryCode: '{env.CountryCode}'");
                }
                
                var environments = environmentsResponse.Value.Select(env => new Environment
                {
                    Name = env.Name,
                    Type = env.Type,
                    CountryCode = env.CountryCode,
                    TenantId = env.AadTenantId,
                    Status = env.Status,
                    LocationName = env.LocationName,
                    GeoName = env.GeoName,
                    AppInsightsKey = env.AppInsightsKey
                }).ToList();
                
                Console.WriteLine($"🔍 [DEBUG] Returning {environments.Count} environments");
                Console.WriteLine($"🔍 [DEBUG] First mapped env - Name: '{environments[0].Name}', Type: '{environments[0].Type}', Status: '{environments[0].Status}'");
                return environments;
            }
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"❌ [DEBUG] API call failed: {response.StatusCode} - {errorContent}");
        }

        Console.WriteLine($"🔍 [DEBUG] Returning empty list");
        return new List<Environment>();
    }

    public async Task<EnvironmentUpdateWindow?> getEnvironmentUpdateWindow(string environmentName, Guid tenantId)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/settings/upgrade";
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var updateWindow = await response.Content.ReadFromJsonAsync(EnvironmentUpdateWindowContext.Default.EnvironmentUpdateWindow);
            if (updateWindow != null)
            {
                updateWindow.TenantId = tenantId;
                updateWindow.EnvironmentName = environmentName;
            }
            return updateWindow;
        }

        return null;
    }

    public async Task<EnvironmentUpdateWindowResponse?> setEnvironmentUpdateWindow(string environmentName, string preferredStartTime, string preferredEndTime, string timeZoneId)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/BusinessCentral/environments/{environmentName}/settings/upgrade";

        // Use the new wall-time + timezone format (API v2.13+)
        var requestBody = new
        {
            preferredStartTime = preferredStartTime,  // Format as "22:00" or "07:30"
            preferredEndTime = preferredEndTime,      // Format as "06:00" or "12:30"
            timeZoneId = timeZoneId                   // Windows time zone identifier
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PutAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            
            try 
            {
                // Reset the stream position for JSON deserialization
                var freshContent = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json");
                var updateWindowResponse = await freshContent.ReadFromJsonAsync(EnvironmentUpdateWindowResponseContext.Default.EnvironmentUpdateWindowResponse);
                return updateWindowResponse;
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    public async Task<ApplicationInsightsKeyResponse?> setEnvironmentApplicationInsightsConnectionString(string environmentName, string appInsightsKey)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/settings/appinsightskey";

        var requestBody = new
        {
            key = appInsightsKey
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content);

        var responseData = new ApplicationInsightsKeyResponse
        {
            Success = response.IsSuccessStatusCode,
            Message = $"{response.StatusCode} {response.ReasonPhrase}"
        };

        return responseData;
    }

    public async Task<EnvironmentCreationResponse?> createNewEnvironment(string environmentName, string environmentType, string countryCode)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}";

        var requestBody = new
        {
            EnvironmentType = environmentType,
            CountryCode = countryCode
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PutAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            var creationResponse = await response.Content.ReadFromJsonAsync(EnvironmentCreationResponseContext.Default.EnvironmentCreationResponse);
            return creationResponse;
        }

        return null;
    }

    public async Task<EnvironmentCreationResponse?> copyEnvironment(string sourceEnvironmentName, string newEnvironmentName, string newEnvironmentType)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{newEnvironmentName}/copy";

        var requestBody = new
        {
            type = newEnvironmentType,
            environmentName = sourceEnvironmentName
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PutAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            var copyResponse = await response.Content.ReadFromJsonAsync(EnvironmentCreationResponseContext.Default.EnvironmentCreationResponse);
            return copyResponse;
        }

        return null;
    }

    public async Task<EnvironmentStorageUsage?> getUsageStorageForEnvironment(string environmentName)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/usedstorage";
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var storageUsage = await response.Content.ReadFromJsonAsync(EnvironmentStorageUsageContext.Default.EnvironmentStorageUsage);
            return storageUsage;
        }

        return null;
    }

    public async Task<List<EnvironmentStorageUsage>?> getUsageStorageForAllEnvironments()
    {
        var url = "https://api.businesscentral.dynamics.com/admin/v2.27/environments/usedstorage";
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var storageUsageListResponse = await response.Content.ReadFromJsonAsync(EnvironmentStorageUsageListResponseContext.Default.EnvironmentStorageUsageListResponse);

            if (storageUsageListResponse?.Value != null)
            {
                return storageUsageListResponse.Value;
            }
        }

        return null;
    }

    public async Task<List<EnvironmentUpdateInfo>?> getEnvironmentUpdates(string environmentName)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/updates";
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var updatesListResponse = await response.Content.ReadFromJsonAsync(EnvironmentUpdatesListResponseContext.Default.EnvironmentUpdatesListResponse);

            if (updatesListResponse?.Value != null)
            {
                return updatesListResponse.Value;
            }
        }

        return null;
    }

    public async Task<SetEnvironmentUpdateResponse?> setEnvironmentUpdate(string environmentName, string targetVersion, SetEnvironmentUpdateRequest? request = null)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/updates/{targetVersion}";

        string? jsonContent = null;
        if (request != null)
        {
            jsonContent = JsonSerializer.Serialize(request);
        }

        var content = jsonContent != null ? new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json") : null;
        var response = await httpClient.PatchAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            var updateResponse = await response.Content.ReadFromJsonAsync(SetEnvironmentUpdateResponseContext.Default.SetEnvironmentUpdateResponse);
            return updateResponse;
        }

        return null;
    }

    public async Task<List<ActiveSession>?> getActiveSessions(string applicationFamily, string environmentName)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/{applicationFamily}/environments/{environmentName}/sessions";

        // Use GET method as per Microsoft documentation
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var sessionsResponse = await response.Content.ReadFromJsonAsync(ActiveSessionsResponseContext.Default.ActiveSessionsResponse);

            if (sessionsResponse?.Value != null)
            {
                return sessionsResponse.Value;
            }
        }

        return null;
    }

    public async Task<bool> killActiveSessions(string applicationFamily, string environmentName, int sessionId)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/{applicationFamily}/environments/{environmentName}/sessions/{sessionId}";

        var response = await httpClient.DeleteAsync(url);

        return response.IsSuccessStatusCode;
    }

    public async Task<List<NotificationRecipient>?> getEnvironmentNotificationRecipient()
    {
        var url = "https://api.businesscentral.dynamics.com/admin/v2.27/settings/notification/recipients";
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var recipientsResponse = await response.Content.ReadFromJsonAsync(NotificationRecipientsResponseContext.Default.NotificationRecipientsResponse);

            if (recipientsResponse?.Value != null)
            {
                return recipientsResponse.Value;
            }
        }

        return null;
    }

    public async Task<NotificationRecipient?> createEnvironmentNotificationRecipient(string email, string name)
    {
        var url = "https://api.businesscentral.dynamics.com/admin/v2.27/settings/notification/recipients";

        var requestBody = new CreateNotificationRecipientRequest
        {
            Email = email,
            Name = name
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PutAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            var recipient = await response.Content.ReadFromJsonAsync(CreateNotificationRecipientResponseContext.Default.NotificationRecipient);
            return recipient;
        }

        return null;
    }

    public async Task<bool> deleteEnvironmentNotificationRecipient(Guid recipientId)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/settings/notification/recipients/{recipientId}";

        var response = await httpClient.DeleteAsync(url);

        return response.IsSuccessStatusCode;
    }

    public async Task<List<InstalledApp>?> getInstalledApps(string environmentName)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/apps";
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var appsResponse = await response.Content.ReadFromJsonAsync(InstalledAppsResponseContext.Default.InstalledAppsResponse);

            if (appsResponse?.Value != null)
            {
                return appsResponse.Value;
            }
        }

        return null;
    }

    public async Task<List<AvailableAppUpdate>?> getAvailableAppUpdates(string environmentName)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/apps/availableUpdates";
        
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var updatesResponse = await response.Content.ReadFromJsonAsync(AvailableAppUpdatesResponseContext.Default.AvailableAppUpdatesResponse);

            if (updatesResponse?.Value != null)
            {
                return updatesResponse.Value;
            }
        }

        return null;
    }

    public async Task<List<Feature>?> getAvailableFeatures(Guid tenantId, string environmentName, Guid companyId)
    {
        await EnsureAuthenticated(tenantId);
        
        // Using the confirmed correct API endpoint
        var url = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/microsoft/automation/v2.0/companies({companyId})/features";
        
        Console.WriteLine($"🔍 [DEBUG] Making Automation API request to: {url}");
        Console.WriteLine($"🔍 [DEBUG] Using token: {httpClient.DefaultRequestHeaders.Authorization?.Parameter?.Substring(0, 50)}...");
        
        try
        {
            var response = await httpClient.GetAsync(url);
            Console.WriteLine($"🔍 [DEBUG] Response status: {response.StatusCode}");
            Console.WriteLine($"🔍 [DEBUG] Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"🔍 [DEBUG] Success! Response content: {responseContent}");
                
                var featuresResponse = await response.Content.ReadFromJsonAsync(FeaturesResponseContext.Default.FeaturesResponse);

                if (featuresResponse?.Value != null)
                {
                    return featuresResponse.Value;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ [DEBUG] HTTP {response.StatusCode} Error response: {errorContent}");
                Console.WriteLine($"❌ [DEBUG] Response reason phrase: {response.ReasonPhrase}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [DEBUG] Exception in getAvailableFeatures: {ex.Message}");
            Console.WriteLine($"❌ [DEBUG] Full exception: {ex}");
        }

        return null;
    }

    public async Task<List<Company>?> getEnvironmentCompanies(string environmentName)
    {
        var url = $"https://api.businesscentral.dynamics.com/v2.0/{environmentName}/api/v2.0/companies";
        
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var companiesResponse = await response.Content.ReadFromJsonAsync(CompaniesResponseContext.Default.CompaniesResponse);

            if (companiesResponse?.Value != null)
            {
                return companiesResponse.Value;
            }
        }

        return null;
    }

    public async Task<UpdateAppResponse?> updateApp(string environmentName, Guid appId, UpdateAppRequest request)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/apps/{appId}/update";

        var jsonContent = JsonSerializer.Serialize(request);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            var updateResponse = await response.Content.ReadFromJsonAsync(UpdateAppResponseContext.Default.UpdateAppResponse);
            return updateResponse;
        }

        return null;
    }

    public async Task<UninstallAppResponse?> uninstallApp(string environmentName, Guid appId, UninstallAppRequest request)
    {
        var url = $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/apps/{appId}/uninstall";

        var jsonContent = JsonSerializer.Serialize(request);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            var uninstallResponse = await response.Content.ReadFromJsonAsync(UninstallAppResponseContext.Default.UninstallAppResponse);
            return uninstallResponse;
        }

        return null;
    }

    public async Task<List<AppOperation>?> getAppOperations(string environmentName, Guid appId, Guid? operationId = null)
    {
        var url = operationId.HasValue
            ? $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/apps/{appId}/operations/{operationId}"
            : $"https://api.businesscentral.dynamics.com/admin/v2.27/applications/businesscentral/environments/{environmentName}/apps/{appId}/operations";

        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var operationsResponse = await response.Content.ReadFromJsonAsync(AppOperationsResponseContext.Default.AppOperationsResponse);

            if (operationsResponse?.Value != null)
            {
                return operationsResponse.Value;
            }
        }

        return null;
    }

    #endregion
}