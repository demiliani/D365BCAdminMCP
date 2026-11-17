using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Azure.Core;
using Azure.Identity;

namespace D365BCAdminMCP;

[McpServerToolType]
public partial class D365BCAdminService : IDisposable
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

    private static void CacheToken(Guid tenantId, string token, DateTimeOffset expiresOn)
    {
        lock (_tokenCacheLock)
        {
            _tokenCache[tenantId] = new CachedToken
            {
                Token = token,
                ExpiresOn = expiresOn
            };
        }
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

        CacheToken(cacheKey, tokenResult.Token, tokenResult.ExpiresOn);

        Log.Info($"Token acquired for tenant {cacheKey}, expires: {tokenResult.ExpiresOn}");
        return tokenResult.Token;
    }

    // Skips Azure CLI / managed identity and the saved sign-in, so the user can pick a different account for the tenant.
    [McpServerTool(Destructive = false, Idempotent = false), Description("Forces an interactive browser login for the specified tenant, ignoring Azure CLI / managed identity credentials and any saved sign-in. Use it when the automatic login fails, never shows a browser window, or signs in with the wrong account. Returns the authentication status and expiry, not the token.")]
    public static async Task<string> force_interactive_auth(
        [Description("The tenant ID (GUID) that requires a fresh interactive login")] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        Log.Info($"Forcing interactive sign-in for tenant {tenantId}");
        var tokenRequestContext = new TokenRequestContext(new[] { "https://api.businesscentral.dynamics.com/.default" });

        try
        {
            var (credential, tokenResult) = await InteractiveSignInAsync(tenantId, tokenRequestContext, forcePrompt: true, cancellationToken);
            lock (_tokenCacheLock)
            {
                _credentials[tenantId] = credential;
            }
            CacheToken(tenantId, tokenResult.Token, tokenResult.ExpiresOn);

            Log.Info($"Interactive token acquired for tenant {tenantId}, expires: {tokenResult.ExpiresOn}");
            return JsonSerializer.Serialize(new { success = true, tenantId, authenticated = true, expiresOn = tokenResult.ExpiresOn });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"force_interactive_auth failed for tenant {tenantId}: {ex.Message}");
            return JsonSerializer.Serialize(new { success = false, tenantId, error = $"Interactive sign-in failed: {ex.Message}" });
        }
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

        return await InteractiveSignInAsync(tenantId, context, forcePrompt: false, cancellationToken);
    }

    // forcePrompt ignores the saved authentication record so the browser always opens and a new account can be chosen.
    private static async Task<(TokenCredential Credential, AccessToken Token)> InteractiveSignInAsync(Guid? tenantId, TokenRequestContext context, bool forcePrompt, CancellationToken cancellationToken)
    {
        if (PersistentTokenCacheEnabled)
        {
            try
            {
                return await PersistentInteractiveSignInAsync(tenantId, context, forcePrompt, cancellationToken);
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
    private static async Task<(TokenCredential Credential, AccessToken Token)> PersistentInteractiveSignInAsync(Guid? tenantId, TokenRequestContext context, bool forcePrompt, CancellationToken cancellationToken)
    {
        var options = InteractiveOptions(tenantId);
        options.TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = TokenCacheName };

        var recordPath = AuthenticationRecordPath(tenantId ?? Guid.Empty);
        AuthenticationRecord? record = null;
        if (!forcePrompt && File.Exists(recordPath))
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
        !string.Equals(Environment.GetEnvironmentVariable(PersistentTokenCacheEnvironmentVariable)?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    internal static string AuthenticationRecordDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), TokenCacheName);

    internal static string AuthenticationRecordPath(Guid tenantId) => Path.Combine(AuthenticationRecordDirectory, $"auth-record-{tenantId}.json");

    private static async Task<D365BCAdminService> CreateServiceAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        new D365BCAdminService(await AcquireTokenAsync(tenantId, cancellationToken)) { CancellationToken = cancellationToken };

    private static string ToJson<T>(ApiResult<T> result) =>
        result.Success ? JsonSerializer.Serialize(result.Value) : ErrorJson(result.Error);

    private static string ErrorJson(string? error) =>
        JsonSerializer.Serialize(new { error = error ?? "Empty response from the Business Central API" });

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
        [Description("The tenant name (e.g. 'contoso.onmicrosoft.com')")] string tenantName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = await new D365BCAdminService { CancellationToken = cancellationToken }.getTenantIDFromTenantName(tenantName);
            return JsonSerializer.Serialize(new { success = true, tenantId, tenantName });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Log.Error($"get_tenant_id_from_tenant_name failed: {ex.Message}");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message, tenantName });
        }
    }

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

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                totalCachedTokens = _tokenCache.Count,
                tokens = status
            }));
        }
    }

    private static readonly Regex TenantNamePattern = new(@"^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*$");

    // The OpenID discovery document is public; the request is built without the access token on purpose.
    public async Task<Guid> getTenantIDFromTenantName(string tenantName)
    {
        tenantName = tenantName?.Trim() ?? string.Empty;
        if (!TenantNamePattern.IsMatch(tenantName))
        {
            throw new ArgumentException($"'{tenantName}' is not a valid tenant name; use a domain such as 'contoso.onmicrosoft.com'.", nameof(tenantName));
        }

        var url = $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantName)}/.well-known/openid-configuration";
        var result = await SendAsync<OpenIdConfiguration>(() => new HttpRequestMessage(HttpMethod.Get, url));
        if (!result.Success)
        {
            throw new InvalidOperationException($"Tenant '{tenantName}' could not be resolved: {result.Error}");
        }

        // token_endpoint has the form https://login.microsoftonline.com/{tenantId}/oauth2/token
        var tokenEndpoint = result.Value!.TokenEndpoint;
        var segments = Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        if (segments.Length == 0 || !Guid.TryParse(segments[0], out var tenantId))
        {
            throw new InvalidOperationException($"Unexpected token endpoint '{tokenEndpoint}' in the OpenID configuration of '{tenantName}'");
        }

        return tenantId;
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
}
