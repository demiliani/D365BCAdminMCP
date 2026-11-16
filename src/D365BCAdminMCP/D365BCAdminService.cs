using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Azure.Core;
using Azure.Identity;

namespace D365BCAdminMCP;

[McpServerToolType]
public class D365BCAdminService : IDisposable
{
    private const string AdminApiVersion = "v2.29";
    private const string AdminApiBaseUrl = "https://api.businesscentral.dynamics.com/admin/" + AdminApiVersion;

    // Static shared HttpClient for all instances (best practice to avoid socket exhaustion)
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromMinutes(5);
    private static HttpClient staticHttpClient = new HttpClient { Timeout = HttpTimeout };
    private string? _accessToken;

    // Set by the tool for the duration of one MCP request; honours cancellation sent by the client.
    internal CancellationToken CancellationToken { get; set; }
    private bool _disposed = false;
    
    // Static token cache for sharing across method calls
    private static readonly Dictionary<Guid, CachedToken> _tokenCache = new();
    private static readonly Dictionary<Guid, TokenCredential> _credentials = new();
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
        // Don't create HttpClient per instance - use static shared client
    }

    public D365BCAdminService(string? accessToken) : this()
    {
        _accessToken = accessToken;
        // Don't set authorization on shared client - will use per-request headers
    }

    public void SetAccessToken(string accessToken)
    {
        _accessToken = accessToken;
        // Don't set authorization on shared client - will use per-request headers
    }

    // Helper method to create authenticated requests
    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(_accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
        return request;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Don't dispose static HttpClient - it's shared across all instances
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private async Task EnsureAuthenticated(Guid tenantId)
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            return;
        }

        _accessToken = await AcquireTokenAsync(tenantId == Guid.Empty ? null : tenantId);
    }

    internal static void SetHttpMessageHandlerForTesting(HttpMessageHandler handler) => staticHttpClient = new HttpClient(handler) { Timeout = HttpTimeout };

    internal static void SetCachedTokenForTesting(Guid tenantId, string token)
    {
        lock (_tokenCacheLock)
        {
            _tokenCache[tenantId] = new CachedToken { Token = token, ExpiresOn = DateTimeOffset.UtcNow.AddHours(1) };
        }
    }

    // Tokens are cached per tenant; Guid.Empty is used for the credential's default tenant.
    private static async Task<string> AcquireTokenAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = tenantId ?? Guid.Empty;
        TokenCredential? knownCredential;
        lock (_tokenCacheLock)
        {
            if (_tokenCache.TryGetValue(cacheKey, out var cachedToken) && !cachedToken.IsExpired)
            {
                Log.Debug($"Using cached token for tenant {cacheKey}, expires: {cachedToken.ExpiresOn}");
                return cachedToken.Token;
            }
            _credentials.TryGetValue(cacheKey, out knownCredential);
        }

        var tokenRequestContext = new TokenRequestContext(new[] { "https://api.businesscentral.dynamics.com/.default" });
        AccessToken tokenResult;
        try
        {
            if (knownCredential == null)
            {
                throw new CredentialUnavailableException("No credential has been used for this tenant yet");
            }
            tokenResult = await knownCredential.GetTokenAsync(tokenRequestContext, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (knownCredential != null)
            {
                Log.Warn($"Token refresh failed for tenant {cacheKey} ({ex.Message}); signing in again");
            }
            Log.Info($"Acquiring new token for tenant {cacheKey}");
            TokenCredential credential;
            (credential, tokenResult) = await SignInAsync(tenantId, tokenRequestContext, cancellationToken);
            lock (_tokenCacheLock)
            {
                _credentials[cacheKey] = credential;
            }
        }

        lock (_tokenCacheLock)
        {
            _tokenCache[cacheKey] = new CachedToken { Token = tokenResult.Token, ExpiresOn = tokenResult.ExpiresOn };
        }

        Log.Info($"Token acquired for tenant {cacheKey}, expires: {tokenResult.ExpiresOn}");
        return tokenResult.Token;
    }

    private static async Task<(TokenCredential Credential, AccessToken Token)> SignInAsync(Guid? tenantId, TokenRequestContext context, CancellationToken cancellationToken)
    {
        try
        {
            var options = new DefaultAzureCredentialOptions();
            if (tenantId.HasValue)
            {
                options.TenantId = tenantId.Value.ToString();
            }
            var credential = new DefaultAzureCredential(options);
            return (credential, await credential.GetTokenAsync(context, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"DefaultAzureCredential failed ({ex.Message}); falling back to interactive browser authentication");
        }

        if (PersistentTokenCacheEnabled)
        {
            try
            {
                return await PersistentInteractiveSignInAsync(tenantId, context, cancellationToken);
            }
            catch (Exception ex) when (IsPersistenceFailure(ex))
            {
                Log.Warn($"Persistent token cache unavailable ({ex.Message}); the login will be kept in memory only");
            }
        }

        var inMemoryCredential = new InteractiveBrowserCredential(InteractiveOptions(tenantId));
        return (inMemoryCredential, await inMemoryCredential.GetTokenAsync(context, cancellationToken));
    }

    // The encrypted MSAL cache keeps refresh tokens across restarts; the authentication record (account and tenant, no secrets)
    // tells the credential which cached account to use, so no browser window opens while the refresh token is valid.
    private static async Task<(TokenCredential Credential, AccessToken Token)> PersistentInteractiveSignInAsync(Guid? tenantId, TokenRequestContext context, CancellationToken cancellationToken)
    {
        var options = InteractiveOptions(tenantId);
        options.TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = TokenCacheName };

        var recordPath = AuthenticationRecordPath(tenantId ?? Guid.Empty);
        AuthenticationRecord? record = null;
        if (File.Exists(recordPath))
        {
            try
            {
                await using var stream = File.OpenRead(recordPath);
                record = await AuthenticationRecord.DeserializeAsync(stream, cancellationToken);
                options.AuthenticationRecord = record;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn($"Ignoring unreadable authentication record {recordPath}: {ex.Message}");
            }
        }

        var credential = new InteractiveBrowserCredential(options);
        if (record == null)
        {
            record = await credential.AuthenticateAsync(context, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
            await using var stream = File.Create(recordPath);
            await record.SerializeAsync(stream, cancellationToken);
        }

        return (credential, await credential.GetTokenAsync(context, cancellationToken));
    }

    private static InteractiveBrowserCredentialOptions InteractiveOptions(Guid? tenantId)
    {
        var options = new InteractiveBrowserCredentialOptions();
        if (tenantId.HasValue)
        {
            options.TenantId = tenantId.Value.ToString();
        }
        return options;
    }

    private static bool IsPersistenceFailure(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current.GetType().Name.Contains("Persistence", StringComparison.Ordinal)
                || current.Message.Contains("persistence", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    internal const string PersistentTokenCacheEnvironmentVariable = "D365BC_MCP_PERSIST_TOKEN_CACHE";
    private const string TokenCacheName = "D365BCAdminMCP";

    private static bool PersistentTokenCacheEnabled =>
        !string.Equals(System.Environment.GetEnvironmentVariable(PersistentTokenCacheEnvironmentVariable)?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    internal static string AuthenticationRecordDirectory { get; set; } =
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), TokenCacheName);

    internal static string AuthenticationRecordPath(Guid tenantId) => Path.Combine(AuthenticationRecordDirectory, $"auth-record-{tenantId}.json");

    private static async Task<D365BCAdminService> CreateServiceAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        new D365BCAdminService(await AcquireTokenAsync(tenantId, cancellationToken)) { CancellationToken = cancellationToken };

    private static string ToJson<T>(ApiResult<T> result) =>
        result.Success ? JsonSerializer.Serialize(result.Value) : ErrorJson(result.Error);

    private static string ErrorJson(string? error) =>
        JsonSerializer.Serialize(new { error = error ?? "Empty response from the Business Central API" });

    #region MCP Tools

    // The token itself is never returned: it would end up in the model context and chat history.
    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Signs in to Microsoft Entra ID for the specified tenant (Azure CLI / managed identity, falling back to interactive browser login) and caches the token used by all other tools. Returns the authentication status and expiry, not the token.")]
    public static async Task<string> get_microsoft_entra_id_token([Description("The tenant ID (GUID) to sign in to")] Guid tenantId, CancellationToken cancellationToken = default)
    {
        bool fromCache;
        lock (_tokenCacheLock)
        {
            fromCache = _tokenCache.TryGetValue(tenantId, out var cached) && !cached.IsExpired;
        }

        try
        {
            await AcquireTokenAsync(tenantId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Authentication failed for tenant {tenantId}: {ex.Message}");
            return JsonSerializer.Serialize(new { success = false, tenantId, error = $"Authentication failed: {ex.Message}" });
        }

        DateTimeOffset expiresOn;
        lock (_tokenCacheLock)
        {
            expiresOn = _tokenCache[tenantId].ExpiresOn;
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            tenantId,
            authenticated = true,
            fromCache,
            expiresOn,
            message = "Authenticated. The token is cached by the server and used automatically by the other tools."
        });
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Retrieves the tenant ID (GUID) for a given tenant name by querying the Azure AD well-known configuration endpoint.")]
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
            Log.Error($"get_tenant_id_from_tenant_name failed: {ex.Message}");
            return JsonSerializer.Serialize(new { 
                success = false, 
                error = ex.Message,
                tenantName = tenantName
            });
        }
    }

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

    [McpServerTool(Destructive = true), Description("Uninstalls an app from a Business Central environment.")]
    public static async Task<string> uninstall_app(
        [Description("The tenant ID (GUID) for which to uninstall the app")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app to uninstall")] Guid appId,
        [Description("Optional: Whether to use the environment update window")] bool? useEnvironmentUpdateWindow = null,
        [Description("Optional: Whether to uninstall dependent apps")] bool? uninstallDependents = null,
        [Description("Optional: Whether to delete data and sync clean the extension")] bool? deleteData = null,
        CancellationToken cancellationToken = default)
    {
        var d365bcAdminService = await CreateServiceAsync(tenantId, cancellationToken);

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

    private const string InstallMissingDependenciesDescription =
        "Whether to install or update, before uploading, the apps this extension depends on (e.g. AppSource apps) that are missing or older than the declared minimum version. " +
        "They are installed immediately (even for scheduled deployments) at the latest version available for the environment, not the minimum declared. " +
        "If false, the tool stops before uploading and lists the missing dependencies. Defaults to true.";

    [McpServerTool(Destructive = false), Description("Uploads a Per-Tenant Extension (.app file, max 50 MB) through the Admin Center API (v2.29+) and installs or updates it immediately or on a schedule. Dependencies that are missing or outdated in the environment (e.g. AppSource apps) are installed or updated first. Requires an Admin Center role (e.g. Dynamics 365 Business Central Administrator). With deploymentSchedule 'Immediate' it waits for the install operation to finish.")]
    public static async Task<string> install_pte(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The local file path to the .app file (e.g., c:\\TMP\\MyExtension_1.0.0.0.app)")] string appFilePath,
        [Description("When to install: 'Immediate', 'UpdateWindow', 'NextMinorUpdate' or 'NextMajorUpdate'. New PTEs (not yet installed) only accept 'Immediate' or 'UpdateWindow'.")] string deploymentSchedule = "Immediate",
        [Description("Schema sync mode: 'Add' or 'ForceSync'")] string syncMode = "Add",
        [Description(InstallMissingDependenciesDescription)] bool installMissingDependencies = true,
        [Description("Whether to accept the ISV EULA, also for dependencies installed automatically (must be true for installation to proceed; defaults to true)")] bool acceptIsvEula = true,
        [Description("Optional: Locale ID language code, e.g. 'en-US'")] string? languageId = null,
        [Description("Optional: For 'Immediate' deployments, wait for the install operation to finish (default: true)")] bool waitForCompletion = true,
        [Description("Optional: Maximum time to wait for each install operation in seconds (default: 600)")] int maxWaitSeconds = 600,
        [Description("Optional: Polling interval in milliseconds (default: 5000)")] int pollIntervalMs = 5000,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = BuildPteInstallOptions(deploymentSchedule, syncMode, installMissingDependencies, acceptIsvEula, languageId, out var optionsError);
        if (options == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = optionsError });
        }

        var fileError = ValidatePteFile(appFilePath);
        if (fileError != null)
        {
            return JsonSerializer.Serialize(new { success = false, error = fileError });
        }

        PteAppInfo? appInfo = null;
        try
        {
            appInfo = PtePackageReader.ReadAppInfo(appFilePath);
        }
        catch (Exception ex)
        {
            Log.Warn($"Unable to read metadata from {appFilePath}: {ex.Message}");
        }

        var service = await CreateServiceAsync(tenantId, cancellationToken);

        ExternalDependencyResult? dependencies = null;
        if (appInfo != null)
        {
            progress?.Report(new ProgressNotificationValue { Progress = 0, Total = 2, Message = "Checking dependencies" });
            var installed = await service.getInstalledAppsResult(environmentName);
            if (!installed.Success)
            {
                return JsonSerializer.Serialize(new { success = false, error = $"Unable to retrieve installed apps to check dependencies, nothing was uploaded: {installed.Error}" });
            }

            dependencies = await service.ensureExternalDependencies(environmentName, new List<PteAppInfo> { appInfo }, installed.Value!.Value ?? new List<InstalledApp>(), options, maxWaitSeconds, pollIntervalMs);
            if (dependencies.FatalError != null)
            {
                return JsonSerializer.Serialize(new { success = false, error = dependencies.FatalError, dependencies = dependencies.Dependencies });
            }

            var failedDependency = dependencies.Dependencies.FirstOrDefault(d => dependencies.FailedDependencyIds.Contains(d.AppId));
            if (failedDependency != null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    result = PteInstallOutcome.Skipped(appInfo, $"Skipped because dependency '{failedDependency.Name}' ({failedDependency.AppId}) could not be installed or updated"),
                    dependencies = dependencies.Dependencies
                });
            }
        }

        progress?.Report(new ProgressNotificationValue { Progress = 1, Total = 2, Message = $"Uploading {Path.GetFileName(appFilePath)}" });
        var outcome = await service.installPteAndWait(environmentName, appFilePath, appInfo, options, waitForCompletion, maxWaitSeconds, pollIntervalMs);
        progress?.Report(new ProgressNotificationValue { Progress = 2, Total = 2, Message = $"{Path.GetFileName(appFilePath)}: {outcome.Status}" });
        return JsonSerializer.Serialize(new
        {
            success = outcome.Status is "succeeded" or "scheduled" or "running",
            result = outcome,
            dependencies = dependencies?.Dependencies,
            warning = appInfo == null ? "Unable to read the package manifest: dependencies were not checked" : null
        });
    }

    [McpServerTool(Destructive = false), Description("Uploads and installs all Per-Tenant Extension .app files in a folder through the Admin Center API (v2.29+). Apps are ordered by the dependencies declared in their manifest (app.json / NavxManifest.xml), so an app is always uploaded after the apps it depends on. Dependencies outside the folder that are missing or outdated in the environment (e.g. AppSource apps) are installed or updated first. With 'Immediate' each install is awaited before the next one; if an app or dependency fails, apps depending on it are skipped. All files are validated before anything is uploaded. Requires an Admin Center role.")]
    public static async Task<string> install_ptes_from_folder(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The local folder path containing .app files (e.g., c:\\MYFOLDER)")] string folderPath,
        [Description("When to install: 'Immediate', 'UpdateWindow', 'NextMinorUpdate' or 'NextMajorUpdate'. New PTEs (not yet installed) only accept 'Immediate' or 'UpdateWindow'.")] string deploymentSchedule = "Immediate",
        [Description("Schema sync mode: 'Add' or 'ForceSync'")] string syncMode = "Add",
        [Description(InstallMissingDependenciesDescription)] bool installMissingDependencies = true,
        [Description("Whether to accept the ISV EULA, also for dependencies installed automatically (must be true for installation to proceed; defaults to true)")] bool acceptIsvEula = true,
        [Description("Optional: Locale ID language code, e.g. 'en-US'")] string? languageId = null,
        [Description("Optional: Maximum time to wait for each install operation in seconds (default: 600)")] int maxWaitSeconds = 600,
        [Description("Optional: Polling interval in milliseconds (default: 5000)")] int pollIntervalMs = 5000,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = BuildPteInstallOptions(deploymentSchedule, syncMode, installMissingDependencies, acceptIsvEula, languageId, out var optionsError);
        if (options == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = optionsError });
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Folder not found: {folderPath}" });
        }

        var appFiles = Directory.GetFiles(folderPath, "*.app", SearchOption.TopDirectoryOnly);
        if (appFiles.Length == 0)
        {
            return JsonSerializer.Serialize(new { success = false, error = "No .app files found in the specified folder", folder = folderPath });
        }

        var plan = PlanPteFolderInstall(appFiles);
        if (plan.Errors.Count > 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Validation failed, nothing was uploaded",
                validationErrors = plan.Errors
            });
        }

        var service = await CreateServiceAsync(tenantId, cancellationToken);

        var installedResult = await service.getInstalledAppsResult(environmentName);
        if (!installedResult.Success)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Unable to retrieve installed apps, nothing was uploaded: {installedResult.Error}" });
        }
        var installedApps = installedResult.Value!.Value ?? new List<InstalledApp>();

        if (options.DeploymentSchedule is "NextMinorUpdate" or "NextMajorUpdate")
        {
            var installedIds = installedApps.Where(a => a.EffectiveId.HasValue).Select(a => a.EffectiveId!.Value).ToHashSet();
            var notInstalled = plan.Ordered.Where(a => !installedIds.Contains(a.Id)).ToList();
            if (notInstalled.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"'{options.DeploymentSchedule}' can only be used to update PTEs that are already installed. New PTEs require 'Immediate' or 'UpdateWindow'. Nothing was uploaded.",
                    notInstalledApps = notInstalled.Select(a => new { file = Path.GetFileName(a.FilePath), appId = a.Id, name = a.Name, version = a.Version })
                });
            }
        }

        // One step for the dependency check plus one per app.
        var totalSteps = plan.Ordered.Count + 1;
        progress?.Report(new ProgressNotificationValue { Progress = 0, Total = totalSteps, Message = "Checking dependencies outside the folder" });
        var dependencies = await service.ensureExternalDependencies(environmentName, plan.Ordered, installedApps, options, maxWaitSeconds, pollIntervalMs);
        progress?.Report(new ProgressNotificationValue
        {
            Progress = 1,
            Total = totalSteps,
            Message = $"Dependencies: {dependencies.Dependencies.Count(d => d.Status is "installed" or "updated")} installed or updated, {dependencies.FailedDependencyIds.Count} failed"
        });
        if (dependencies.FatalError != null)
        {
            return JsonSerializer.Serialize(new { success = false, error = dependencies.FatalError, dependencies = dependencies.Dependencies });
        }

        var folderAppIds = plan.Ordered.Select(a => a.Id).ToHashSet();
        var blockedAppIds = new HashSet<Guid>(dependencies.FailedDependencyIds);
        var results = new List<PteInstallOutcome>();

        foreach (var app in plan.Ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blockingDependency = app.Dependencies.FirstOrDefault(d => blockedAppIds.Contains(d.Id));
            if (blockingDependency != null)
            {
                blockedAppIds.Add(app.Id);
                results.Add(PteInstallOutcome.Skipped(app, $"Skipped because dependency '{blockingDependency.Name}' ({blockingDependency.Id}) did not install successfully"));
                ReportAppProgress(progress, results, totalSteps);
                continue;
            }

            Log.Debug($"Installing PTE {app.Name} {app.Version} from {app.FilePath}");

            // Long folder runs can outlive a token; the cache refreshes it when close to expiry.
            service.SetAccessToken(await AcquireTokenAsync(tenantId, cancellationToken));
            var outcome = await service.installPteAndWait(environmentName, app.FilePath, app, options, waitForCompletion: true, maxWaitSeconds, pollIntervalMs);
            results.Add(outcome);
            ReportAppProgress(progress, results, totalSteps);

            if (outcome.Status is not ("succeeded" or "scheduled"))
            {
                blockedAppIds.Add(app.Id);
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = results.All(r => r.Status is "succeeded" or "scheduled") && dependencies.FailedDependencyIds.Count == 0,
            deploymentSchedule = options.DeploymentSchedule,
            dependencies = dependencies.Dependencies,
            totalFiles = results.Count,
            succeeded = results.Count(r => r.Status == "succeeded"),
            scheduled = results.Count(r => r.Status == "scheduled"),
            failed = results.Count(r => r.Status is not ("succeeded" or "scheduled" or "skipped")),
            skipped = results.Count(r => r.Status == "skipped"),
            dependenciesInstalled = dependencies.Dependencies.Count(d => d.Status is "installed" or "updated"),
            dependenciesFailed = dependencies.FailedDependencyIds.Count,
            installOrder = plan.Ordered.Select(a => new
            {
                file = Path.GetFileName(a.FilePath),
                name = a.Name,
                version = a.Version,
                dependsOn = a.Dependencies.Where(d => folderAppIds.Contains(d.Id)).Select(d => d.Name)
            }),
            results
        });
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Lists the scheduled Per-Tenant Extension (PTE) installs and updates for a Business Central environment (Admin Center API v2.29+).")]
    public static async Task<string> get_scheduled_pte_operations(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        var service = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await service.getScheduledPteOperations(environmentName);

        if (result.Success)
        {
            return JsonSerializer.Serialize(result.Value!.Value ?? new List<PteOperation>());
        }

        return JsonSerializer.Serialize(new { success = false, error = result.Error });
    }

    [McpServerTool(Destructive = true), Description("Cancels a scheduled Per-Tenant Extension (PTE) install or update (Admin Center API v2.29+). The scheduled .app version is permanently removed from storage.")]
    public static async Task<string> cancel_scheduled_pte(
        [Description("The tenant ID (GUID)")] Guid tenantId,
        [Description("The name of the environment")] string environmentName,
        [Description("The ID of the app whose scheduled version should be removed")] Guid appId,
        [Description("The scheduled version to remove (e.g., '2.0.0.0')")] string targetVersion,
        [Description("The schedule of the operation to cancel: 'UpdateWindow', 'NextMinorUpdate' or 'NextMajorUpdate'")] string scheduleKind,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = NormalizePteValue(scheduleKind, PteScheduledKinds);
        if (normalizedKind == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Invalid scheduleKind '{scheduleKind}'. Allowed values: {string.Join(", ", PteScheduledKinds)}" });
        }

        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            return JsonSerializer.Serialize(new { success = false, error = "targetVersion is required" });
        }

        var service = await CreateServiceAsync(tenantId, cancellationToken);
        var result = await service.removeScheduledPteVersion(environmentName, appId, new RemoveScheduledPteVersionRequest
        {
            TargetVersion = targetVersion.Trim(),
            ScheduleKind = normalizedKind
        });

        if (result.Success)
        {
            return JsonSerializer.Serialize(new { success = true, operation = result.Value });
        }

        return JsonSerializer.Serialize(new { success = false, error = result.Error });
    }

    private static void ReportAppProgress(IProgress<ProgressNotificationValue>? progress, List<PteInstallOutcome> results, int totalSteps)
    {
        var last = results[^1];
        progress?.Report(new ProgressNotificationValue
        {
            Progress = results.Count + 1,
            Total = totalSteps,
            Message = $"{last.Name ?? last.File}: {last.Status}"
        });
    }

    internal static PteInstallOptions? BuildPteInstallOptions(string deploymentSchedule, string syncMode, bool installOrUpdateNeededDependencies, bool acceptIsvEula, string? languageId, out string? error)
    {
        error = null;
        var normalizedSchedule = NormalizePteValue(deploymentSchedule, PteDeploymentSchedules);
        if (normalizedSchedule == null)
        {
            error = $"Invalid deploymentSchedule '{deploymentSchedule}'. Allowed values: {string.Join(", ", PteDeploymentSchedules)}";
            return null;
        }

        var normalizedSyncMode = NormalizePteValue(syncMode, PteSyncModes);
        if (normalizedSyncMode == null)
        {
            error = $"Invalid syncMode '{syncMode}'. Allowed values: {string.Join(", ", PteSyncModes)}";
            return null;
        }

        if (!acceptIsvEula)
        {
            error = "acceptIsvEula must be true for the installation to proceed";
            return null;
        }

        return new PteInstallOptions
        {
            DeploymentSchedule = normalizedSchedule,
            SyncMode = normalizedSyncMode,
            InstallOrUpdateNeededDependencies = installOrUpdateNeededDependencies,
            AcceptIsvEula = acceptIsvEula,
            LanguageId = string.IsNullOrWhiteSpace(languageId) ? null : languageId.Trim()
        };
    }

    internal static string? ValidatePteFile(string appFilePath)
    {
        if (string.IsNullOrWhiteSpace(appFilePath) || !File.Exists(appFilePath))
        {
            return $"File not found: {appFilePath}";
        }

        if (!string.Equals(Path.GetExtension(appFilePath), ".app", StringComparison.OrdinalIgnoreCase))
        {
            return $"Not an .app file: {appFilePath}";
        }

        var size = new FileInfo(appFilePath).Length;
        if (size > MaxPteFileSizeBytes)
        {
            return $"File exceeds the 50 MB limit of the Admin Center API ({size / (1024.0 * 1024.0):F1} MB): {appFilePath}";
        }

        return null;
    }

    internal static (List<PteAppInfo> Ordered, List<string> Errors) PlanPteFolderInstall(IEnumerable<string> appFiles)
    {
        var errors = new List<string>();
        var apps = new List<PteAppInfo>();

        foreach (var file in appFiles)
        {
            var fileError = ValidatePteFile(file);
            if (fileError != null)
            {
                errors.Add(fileError);
                continue;
            }

            try
            {
                apps.Add(PtePackageReader.ReadAppInfo(file));
            }
            catch (Exception ex)
            {
                errors.Add($"Unable to read app metadata from {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        foreach (var duplicate in apps.GroupBy(a => a.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"App {duplicate.First().Name} ({duplicate.Key}) appears in more than one file: {string.Join(", ", duplicate.Select(a => Path.GetFileName(a.FilePath)))}");
        }

        if (errors.Count > 0)
        {
            return (new List<PteAppInfo>(), errors);
        }

        var order = PtePackageReader.SortByDependencies(apps);
        if (order.Cyclic.Count > 0)
        {
            errors.Add($"Circular dependency between: {string.Join(", ", order.Cyclic.Select(a => $"{a.Name} ({Path.GetFileName(a.FilePath)})"))}");
            return (new List<PteAppInfo>(), errors);
        }

        return (order.Ordered, errors);
    }

    // Dependencies that are not part of the given apps must already be in the environment at the declared minimum version.
    // Missing ones are installed and outdated ones updated (to the latest available version) before any PTE is uploaded.
    internal async Task<ExternalDependencyResult> ensureExternalDependencies(string environmentName, List<PteAppInfo> apps, List<InstalledApp> installedApps, PteInstallOptions options, int maxWaitSeconds, int pollIntervalMs)
    {
        var result = new ExternalDependencyResult();
        var ownIds = apps.Select(a => a.Id).ToHashSet();
        var required = new Dictionary<Guid, ExternalDependencyOutcome>();

        foreach (var app in apps)
        {
            foreach (var dep in app.Dependencies.Where(d => !ownIds.Contains(d.Id)))
            {
                if (!required.TryGetValue(dep.Id, out var entry))
                {
                    entry = new ExternalDependencyOutcome { AppId = dep.Id, Name = dep.Name, Publisher = dep.Publisher, RequiredVersion = dep.Version };
                    required[dep.Id] = entry;
                }
                else if (IsLowerVersion(entry.RequiredVersion, dep.Version))
                {
                    entry.RequiredVersion = dep.Version;
                }
                entry.RequiredBy.Add(app.Name ?? Path.GetFileName(app.FilePath));
            }
        }

        result.Dependencies = required.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var dep in result.Dependencies)
        {
            var installed = installedApps.FirstOrDefault(a => a.EffectiveId == dep.AppId);
            dep.InstalledVersion = installed?.Version;
            dep.Status = installed == null ? "missing" : IsLowerVersion(installed.Version, dep.RequiredVersion) ? "outdated" : "satisfied";

            if (PlatformAppIds.Contains(dep.AppId))
            {
                if (dep.Status == "missing")
                {
                    dep.Status = "satisfied";
                    dep.Note = "Microsoft platform app, not listed by the API: assumed present";
                }
                else if (dep.Status == "outdated")
                {
                    dep.Action = "update";
                    FailDependency(result, dep, $"The environment has {dep.Name} {dep.InstalledVersion}, but version {dep.RequiredVersion} is required. Platform apps are updated with the environment version: schedule an environment update first.");
                }
            }
        }

        var unsatisfied = result.Dependencies.Where(d => d.Status is "missing" or "outdated").ToList();
        if (unsatisfied.Count == 0)
        {
            return result;
        }

        if (!options.InstallOrUpdateNeededDependencies)
        {
            result.FatalError = "Some dependencies are missing or outdated in the environment and installMissingDependencies is false. Nothing was uploaded.";
            return result;
        }

        List<AvailableAppUpdate>? availableUpdates = null;
        foreach (var dep in unsatisfied)
        {
            ApiResult<(Guid? Id, string? Status, string? ErrorMessage)> request;
            if (dep.Status == "missing")
            {
                dep.Action = "install";
                Log.Info($"Installing missing dependency {dep.Name} ({dep.AppId})");
                request = (await installApp("BusinessCentral", environmentName, dep.AppId, new InstallAppRequest
                {
                    AcceptIsvEula = options.AcceptIsvEula,
                    InstallOrUpdateNeededDependencies = true,
                    UseEnvironmentUpdateWindow = false,
                    LanguageId = options.LanguageId
                })).Map(op => (op.Id, op.Status, op.ErrorMessage));
            }
            else
            {
                dep.Action = "update";
                if (availableUpdates == null)
                {
                    var updates = await getAvailableAppUpdatesResult(environmentName);
                    if (!updates.Success)
                    {
                        FailDependency(result, dep, $"Unable to retrieve available app updates: {updates.Error}");
                        continue;
                    }
                    availableUpdates = updates.Value!.Value ?? new List<AvailableAppUpdate>();
                }

                var update = availableUpdates.FirstOrDefault(u => u.Id == dep.AppId);
                if (update?.Version == null || IsLowerVersion(update.Version, dep.RequiredVersion))
                {
                    FailDependency(result, dep, $"No available update reaches version {dep.RequiredVersion}"
                        + (update?.Version != null ? $" (latest available: {update.Version})" : string.Empty)
                        + DependencyHint(dep));
                    continue;
                }

                Log.Info($"Updating dependency {dep.Name} ({dep.AppId}) from {dep.InstalledVersion} to {update.Version}");
                request = (await updateApp(environmentName, dep.AppId, new UpdateAppRequest
                {
                    TargetVersion = update.Version,
                    UseEnvironmentUpdateWindow = false,
                    InstallOrUpdateNeededDependencies = true
                })).Map(op => (op.Id, op.Status, op.ErrorMessage));
            }

            if (!request.Success)
            {
                FailDependency(result, dep, request.Error?.TrimEnd('.') + DependencyHint(dep));
                continue;
            }

            var (operationId, status, operationError) = request.Value;
            dep.OperationId = operationId;

            if (dep.OperationId != null && !PteFinalStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                var (operation, timedOut, pollError) = await waitForAppOperation(environmentName, dep.AppId, dep.OperationId.Value, maxWaitSeconds, pollIntervalMs);
                if (timedOut)
                {
                    FailDependency(result, dep, $"The {dep.Action} operation did not complete within {maxWaitSeconds} seconds" + (pollError != null ? $" (last polling error: {pollError})" : string.Empty));
                    continue;
                }
                status = operation!.Status;
                operationError = operation.ErrorMessage;
            }

            if (!string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                FailDependency(result, dep, $"The {dep.Action} operation ended with status '{status}'" + (string.IsNullOrWhiteSpace(operationError) ? string.Empty : $": {operationError}"));
                continue;
            }

            dep.Status = dep.Action == "install" ? "installed" : "updated";
        }

        var refreshed = await getInstalledAppsResult(environmentName);
        if (refreshed.Success)
        {
            var refreshedApps = refreshed.Value!.Value ?? new List<InstalledApp>();
            foreach (var dep in unsatisfied.Where(d => d.Status is "installed" or "updated"))
            {
                var installed = refreshedApps.FirstOrDefault(a => a.EffectiveId == dep.AppId);
                dep.InstalledVersion = installed?.Version;
                if (installed == null || IsLowerVersion(installed.Version, dep.RequiredVersion))
                {
                    FailDependency(result, dep, $"After the {dep.Action} the environment has version {installed?.Version ?? "none"}, lower than the required {dep.RequiredVersion}");
                }
            }
        }

        return result;
    }

    // System, System Application, Business Foundation, Base Application and Application: shipped with the environment version.
    private static readonly HashSet<Guid> PlatformAppIds = new()
    {
        Guid.Parse("8874ed3a-0643-4247-9ced-7a7002f7135d"),
        Guid.Parse("63ca2fa4-4f03-4f2b-a480-172fef340d3f"),
        Guid.Parse("f3552374-a1f2-4356-848e-196002525837"),
        Guid.Parse("437dbf0e-84ff-417a-965d-ed2bb9650972"),
        Guid.Parse("c1335042-3002-4257-bf8a-75c898ccb1b8")
    };

    private static void FailDependency(ExternalDependencyResult result, ExternalDependencyOutcome dep, string error)
    {
        dep.Status = "failed";
        dep.Error = error;
        result.FailedDependencyIds.Add(dep.AppId);
    }

    private static string DependencyHint(ExternalDependencyOutcome dep)
    {
        if (string.Equals(dep.Publisher, "Microsoft", StringComparison.OrdinalIgnoreCase) && dep.Action == "update")
        {
            return ". Microsoft apps are usually updated together with the environment version: consider scheduling an environment update.";
        }

        return dep.Action == "install"
            ? ". If this dependency is a per-tenant extension, add its .app file to the folder."
            : string.Empty;
    }

    private static bool IsLowerVersion(string? version, string? than) =>
        Version.TryParse(than, out var target) && (!Version.TryParse(version, out var current) || current < target);

    internal async Task<PteInstallOutcome> installPteAndWait(string environmentName, string appFilePath, PteAppInfo? appInfo, PteInstallOptions options, bool waitForCompletion, int maxWaitSeconds, int pollIntervalMs)
    {
        ApiResult<PteOperation> installResult;
        try
        {
            installResult = await installPte(environmentName, appFilePath, options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PteInstallOutcome.Failed(appFilePath, appInfo, null, ex.Message);
        }

        if (!installResult.Success)
        {
            return PteInstallOutcome.Failed(appFilePath, appInfo, null, installResult.Error);
        }

        var operation = installResult.Value!;
        var outcome = PteInstallOutcome.FromOperation(appFilePath, appInfo, operation);
        var appId = operation.AppId ?? appInfo?.Id;

        if (options.DeploymentSchedule != "Immediate" || !waitForCompletion || operation.Id == null || appId == null
            || string.Equals(operation.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            return outcome;
        }

        var (finalOperation, timedOut, pollError) = await waitForAppOperation(environmentName, appId.Value, operation.Id.Value, maxWaitSeconds, pollIntervalMs);
        if (timedOut)
        {
            outcome.Status = "timeout";
            outcome.Error = $"Install operation did not complete within {maxWaitSeconds} seconds" + (pollError != null ? $" (last polling error: {pollError})" : string.Empty);
            return outcome;
        }

        outcome.Status = finalOperation!.Status?.ToLowerInvariant();
        if (DateTime.TryParse(finalOperation.CompletedOn, null, System.Globalization.DateTimeStyles.RoundtripKind, out var completedOn))
        {
            outcome.CompletedOn = completedOn;
        }
        if (!string.IsNullOrWhiteSpace(finalOperation.ErrorMessage))
        {
            outcome.Error = finalOperation.ErrorMessage;
        }

        return outcome;
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

    [McpServerTool(Destructive = false, Idempotent = true, OpenWorld = false), Description("Clears the cached authentication token for the specified tenant, forcing a new login on the next API call.")]
    public static Task<string> clear_cached_token([Description("The tenant ID (GUID) for which to clear the cached token")] Guid tenantId)
    {
        bool removed;
        lock (_tokenCacheLock)
        {
            removed = _tokenCache.Remove(tenantId) | _credentials.Remove(tenantId);
        }

        var recordPath = AuthenticationRecordPath(tenantId);
        if (File.Exists(recordPath))
        {
            File.Delete(recordPath);
            removed = true;
        }

        if (removed)
        {
            Log.Debug($"Cleared cached token and saved sign-in for tenant {tenantId}");
            return Task.FromResult(JsonSerializer.Serialize(new { success = true, message = $"Token and saved sign-in cleared for tenant {tenantId}: the next call signs in again" }));
        }

        Log.Warn($"No cached token found for tenant {tenantId}");
        return Task.FromResult(JsonSerializer.Serialize(new { success = false, message = $"No cached token found for tenant {tenantId}" }));
    }

    [McpServerTool(ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Shows the status of cached authentication tokens for all tenants.")]
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
            Log.Debug($"GetTenantIDFromTenantName called with tenantName: {tenantName}");
            
            // Construct the well-known OpenID configuration endpoint
            var url = $"https://login.microsoftonline.com/{tenantName}/.well-known/openid-configuration";
            Log.Debug($"Making request to: {url}");
            
            var httpRequest = CreateAuthenticatedRequest(HttpMethod.Get, url);
            
            var response = await staticHttpClient.SendAsync(httpRequest);
            Log.Debug($"Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"Failed to retrieve tenant configuration: {response.StatusCode} - {errorContent}");
                throw new InvalidOperationException($"Failed to retrieve tenant configuration for '{tenantName}': HTTP {response.StatusCode}");
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            Log.Debug($"Response content length: {jsonContent.Length}");
            
            // Parse the JSON response to extract the tenant ID from the token_endpoint
            var jsonDocument = JsonDocument.Parse(jsonContent);
            var root = jsonDocument.RootElement;

            if (!root.TryGetProperty("token_endpoint", out var tokenEndpointElement))
            {
                Log.Error($"'token_endpoint' property not found in response");
                throw new InvalidOperationException("'token_endpoint' property not found in OpenID configuration response");
            }

            var tokenEndpoint = tokenEndpointElement.GetString();
            if (string.IsNullOrEmpty(tokenEndpoint))
            {
                Log.Error($"'token_endpoint' value is null or empty");
                throw new InvalidOperationException("'token_endpoint' value is empty");
            }

            Log.Debug($"Token endpoint: {tokenEndpoint}");

            // Extract the tenant ID (GUID) from the token_endpoint URL
            // Example: https://login.microsoftonline.com/d0de2a8d-d356-4558-99bc-61e6d62a55e8/oauth2/token
            // We need to extract: d0de2a8d-d356-4558-99bc-61e6d62a55e8
            
            var uri = new Uri(tokenEndpoint);
            var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (pathSegments.Length == 0)
            {
                Log.Error($"Could not extract path segments from token_endpoint");
                throw new InvalidOperationException("Could not extract tenant ID from token_endpoint URL");
            }

            var tenantIdString = pathSegments[0];
            Log.Debug($"Extracted tenant ID string: {tenantIdString}");

            if (!Guid.TryParse(tenantIdString, out var tenantId))
            {
                Log.Error($"Failed to parse '{tenantIdString}' as a GUID");
                throw new InvalidOperationException($"Failed to parse tenant ID '{tenantIdString}' as a valid GUID");
            }

            Log.Debug($"Successfully retrieved tenant ID: {tenantId}");
            return tenantId;
        }
        catch (HttpRequestException ex)
        {
            Log.Error($"HTTP request failed: {ex.Message}");
            throw new InvalidOperationException($"HTTP request to retrieve tenant configuration failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            Log.Error($"JSON parsing failed: {ex.Message}");
            throw new InvalidOperationException($"Failed to parse OpenID configuration response: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Log.Error($"Unexpected error: {ex.Message}");
            throw new InvalidOperationException($"An unexpected error occurred while retrieving tenant ID: {ex.Message}", ex);
        }
    }

    private static readonly JsonSerializerOptions IgnoreNullsOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static string EnvironmentUrl(string environmentName, string applicationFamily = "BusinessCentral") =>
        $"{AdminApiBaseUrl}/applications/{Uri.EscapeDataString(applicationFamily)}/environments/{Uri.EscapeDataString(environmentName)}";

    private static string AutomationCompanyUrl(string environmentName, Guid companyId) =>
        $"https://api.businesscentral.dynamics.com/v2.0/{Uri.EscapeDataString(environmentName)}/api/microsoft/automation/v2.0/companies({companyId})";

    private Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        var json = body == null ? null : JsonSerializer.Serialize(body, IgnoreNullsOptions);
        return SendAsync<T>(() =>
        {
            var request = CreateAuthenticatedRequest(method, url);
            if (json != null)
            {
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        });
    }

    // For endpoints whose success response has no meaningful body (204, empty 200/202).
    private async Task<ApiResult<bool>> SendWithoutResultAsync(HttpMethod method, string url, object? body = null)
    {
        var result = await SendAsync<JsonElement?>(method, url, body);
        return result.Error == null
            ? new ApiResult<bool> { StatusCode = result.StatusCode, Value = true }
            : new ApiResult<bool> { StatusCode = result.StatusCode, Error = result.Error, ErrorBody = result.ErrorBody };
    }

    internal const int MaxAttempts = 3;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);
    internal static Func<TimeSpan, CancellationToken, Task> RetryDelay { get; set; } = Task.Delay;

    // The request is rebuilt for every attempt because an HttpRequestMessage cannot be sent twice.
    private async Task<ApiResult<T>> SendAsync<T>(Func<HttpRequestMessage> createRequest)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = createRequest();
            // A failed network call or gateway timeout may have reached the service: only reads are safe to repeat.
            var canRepeat = attempt < MaxAttempts && request.Method == HttpMethod.Get;
            Log.Debug($"{request.Method} {request.RequestUri}");

            HttpResponseMessage response;
            try
            {
                response = await staticHttpClient.SendAsync(request, CancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (!canRepeat)
                {
                    return new ApiResult<T> { Error = $"Request to {request.RequestUri} failed: {ex.Message}" };
                }
                await WaitBeforeRetry(request, $"failed ({ex.Message})", BackoffDelay(attempt), attempt);
                continue;
            }
            catch (TaskCanceledException) when (!CancellationToken.IsCancellationRequested)
            {
                if (!canRepeat)
                {
                    return new ApiResult<T> { Error = $"Request to {request.RequestUri} timed out after {staticHttpClient.Timeout.TotalSeconds:0} seconds" };
                }
                await WaitBeforeRetry(request, "timed out", BackoffDelay(attempt), attempt);
                continue;
            }

            using (response)
            {
                Log.Debug($"{request.Method} {request.RequestUri} -> {(int)response.StatusCode}");
                var throttled = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
                var gatewayError = response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;
                if (attempt < MaxAttempts && (throttled || (gatewayError && canRepeat)))
                {
                    await WaitBeforeRetry(request, $"returned {(int)response.StatusCode}", RetryAfterDelay(response) ?? BackoffDelay(attempt), attempt);
                    continue;
                }

                return await ReadApiResult<T>(response);
            }
        }
    }

    private async Task WaitBeforeRetry(HttpRequestMessage request, string reason, TimeSpan delay, int attempt)
    {
        Log.Warn($"{request.Method} {request.RequestUri} {reason}; retrying in {delay.TotalSeconds:0.#}s (attempt {attempt + 1}/{MaxAttempts})");
        await RetryDelay(delay, CancellationToken);
    }

    private static TimeSpan BackoffDelay(int attempt) => TimeSpan.FromSeconds(2 * Math.Pow(2, attempt - 1));

    internal static TimeSpan? RetryAfterDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (delay == null)
        {
            return null;
        }
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static async Task<ApiResult<T>> ReadApiResult<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            var error = DescribeApiError(response, body);
            Log.Warn(error);
            return new ApiResult<T> { StatusCode = statusCode, Error = error, ErrorBody = body };
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ApiResult<T> { StatusCode = statusCode };
        }

        try
        {
            return new ApiResult<T> { StatusCode = statusCode, Value = JsonSerializer.Deserialize<T>(body) };
        }
        catch (JsonException ex)
        {
            return new ApiResult<T> { StatusCode = statusCode, Error = $"Unexpected response format: {ex.Message}" };
        }
    }

    internal static string DescribeApiError(HttpResponseMessage response, string body)
    {
        var prefix = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        if (string.IsNullOrWhiteSpace(body))
        {
            return prefix;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // Business Central standard and automation APIs wrap the details in an "error" object.
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var inner) && inner.ValueKind == JsonValueKind.Object)
            {
                root = inner;
            }

            var code = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (code != null || message != null)
            {
                var description = $"{prefix}: {code}: {message}";
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("requirements", out var requirements) && requirements.ValueKind == JsonValueKind.Array
                    && requirements.GetArrayLength() > 0)
                {
                    var items = requirements.EnumerateArray().Select(r =>
                        $"{(r.TryGetProperty("type", out var t) ? t.GetString() : "?")} {(r.TryGetProperty("name", out var n) ? n.GetString() : "?")} {(r.TryGetProperty("version", out var v) ? v.GetString() : string.Empty)}".Trim());
                    description += $" (requirements: {string.Join("; ", items)})";
                }
                return description;
            }
        }
        catch (JsonException)
        {
        }

        const int maxBodyLength = 500;
        var trimmed = body.Trim();
        return $"{prefix}: {(trimmed.Length > maxBodyLength ? trimmed[..maxBodyLength] + "..." : trimmed)}";
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

    internal static readonly string[] PteDeploymentSchedules = { "Immediate", "UpdateWindow", "NextMinorUpdate", "NextMajorUpdate" };
    internal static readonly string[] PteScheduledKinds = { "UpdateWindow", "NextMinorUpdate", "NextMajorUpdate" };
    internal static readonly string[] PteSyncModes = { "Add", "ForceSync" };
    internal const long MaxPteFileSizeBytes = 50L * 1024 * 1024;
    private static readonly string[] PteFinalStatuses = { "succeeded", "failed", "canceled", "skipped" };

    internal static string? NormalizePteValue(string? value, string[] allowedValues) =>
        allowedValues.FirstOrDefault(v => string.Equals(v, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<ApiResult<PteOperation>> installPte(string environmentName, string appFilePath, PteInstallOptions options)
    {
        var fileBytes = await File.ReadAllBytesAsync(appFilePath, CancellationToken);
        var url = $"{EnvironmentUrl(environmentName)}/apps/pteInstall";

        return await SendAsync<PteOperation>(() =>
        {
            var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "extensionFile", Path.GetFileName(appFilePath));
            form.Add(new StringContent(options.DeploymentSchedule), "deploymentSchedule");
            form.Add(new StringContent(options.SyncMode), "syncMode");
            form.Add(new StringContent(options.AcceptIsvEula ? "true" : "false"), "acceptIsvEula");
            form.Add(new StringContent(options.InstallOrUpdateNeededDependencies ? "true" : "false"), "installOrUpdateNeededDependencies");
            if (!string.IsNullOrWhiteSpace(options.LanguageId))
            {
                form.Add(new StringContent(options.LanguageId), "languageId");
            }

            var request = CreateAuthenticatedRequest(HttpMethod.Post, url);
            request.Content = form;
            return request;
        });
    }

    public Task<ApiResult<ScheduledPteOperationsResponse>> getScheduledPteOperations(string environmentName) =>
        SendAsync<ScheduledPteOperationsResponse>(HttpMethod.Get, $"{EnvironmentUrl(environmentName)}/apps/scheduledPteOperations");

    public Task<ApiResult<PteOperation>> removeScheduledPteVersion(string environmentName, Guid appId, RemoveScheduledPteVersionRequest request) =>
        SendAsync<PteOperation>(HttpMethod.Post, $"{EnvironmentUrl(environmentName)}/apps/{appId}/removeScheduledPteVersion", request);

    public async Task<(AppOperation? Operation, bool TimedOut, string? Error)> waitForAppOperation(string environmentName, Guid appId, Guid operationId, int maxWaitSeconds, int pollIntervalMs)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        AppOperation? lastOperation = null;
        string? lastError = null;

        while (stopwatch.Elapsed.TotalSeconds < maxWaitSeconds)
        {
            var result = await getAppOperation(environmentName, appId, operationId);
            if (result.Success)
            {
                lastOperation = result.Value;
                lastError = null;
                if (PteFinalStatuses.Contains(lastOperation!.Status, StringComparer.OrdinalIgnoreCase))
                {
                    return (lastOperation, false, null);
                }
            }
            else
            {
                lastError = result.Error;
                Log.Warn($"Polling operation {operationId} failed: {result.Error}");
            }

            await Task.Delay(pollIntervalMs, CancellationToken);
        }

        return (lastOperation, true, lastError);
    }

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

    #endregion
}



