using System.Net.Http.Headers;
using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

public partial class D365BCAdminService
{
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
}
