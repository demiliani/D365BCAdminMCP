using System.Text.Json.Serialization;

namespace D365BCAdminMCP;

public partial class BCExtension
{
    public string? ID { get; set; }
    public string? Name { get; set; }
    public string? Publisher { get; set; }
    public string? Version { get; set; }    
}

[JsonSerializable(typeof(List<BCExtension>))]
internal sealed partial class BCExtensionContext : JsonSerializerContext {

}

public class EnvironmentResponse
{
    [JsonPropertyName("value")]
    public List<EnvironmentData>? Value { get; set; }
}

public class EnvironmentData
{
    [JsonPropertyName("friendlyName")]
    public string? FriendlyName { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
    
    [JsonPropertyName("applicationFamily")]
    public string? ApplicationFamily { get; set; }
    
    [JsonPropertyName("aadTenantId")]
    public Guid? AadTenantId { get; set; }
    
    [JsonPropertyName("applicationVersion")]
    public string? ApplicationVersion { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("webClientLoginUrl")]
    public string? WebClientLoginUrl { get; set; }
    
    [JsonPropertyName("webServiceUrl")]
    public string? WebServiceUrl { get; set; }
    
    [JsonPropertyName("locationName")]
    public string? LocationName { get; set; }
    
    [JsonPropertyName("geoName")]
    public string? GeoName { get; set; }
    
    [JsonPropertyName("platformVersion")]
    public string? PlatformVersion { get; set; }
    
    [JsonPropertyName("ringName")]
    public string? RingName { get; set; }
    
    [JsonPropertyName("ringNameIdentifier")]
    public string? RingNameIdentifier { get; set; }
    
    [JsonPropertyName("appInsightsKey")]
    public string? AppInsightsKey { get; set; }
    
    [JsonPropertyName("linkedPowerPlatformEnvironmentId")]
    public string? LinkedPowerPlatformEnvironmentId { get; set; }
    
    [JsonPropertyName("softDeletedOn")]
    public DateTime? SoftDeletedOn { get; set; }
    
    [JsonPropertyName("hardDeletePendingOn")]
    public DateTime? HardDeletePendingOn { get; set; }
    
    [JsonPropertyName("deleteReason")]
    public string? DeleteReason { get; set; }
    
    [JsonPropertyName("appSourceAppsUpdateCadence")]
    public string? AppSourceAppsUpdateCadence { get; set; }
    
    [JsonPropertyName("versionDetails")]
    public EnvironmentVersionDetails? VersionDetails { get; set; }
}

public class EnvironmentVersionDetails
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    
    [JsonPropertyName("gracePeriodStartDate")]
    public DateTime? GracePeriodStartDate { get; set; }
    
    [JsonPropertyName("enforcedUpdatePeriodStartDate")]
    public DateTime? EnforcedUpdatePeriodStartDate { get; set; }
}

[JsonSerializable(typeof(EnvironmentResponse))]
internal sealed partial class EnvironmentResponseContext : JsonSerializerContext {

}

public class EnvironmentUpdateWindow
{
    [JsonPropertyName("preferredStartTime")]
    public string? PreferredStartTime { get; set; }
    
    [JsonPropertyName("preferredEndTime")]
    public string? PreferredEndTime { get; set; }
    
    [JsonPropertyName("timeZoneId")]
    public string? TimeZoneId { get; set; }
    
    [JsonPropertyName("preferredStartTimeUtc")]
    public DateTime? PreferredStartTimeUtc { get; set; }
    
    [JsonPropertyName("preferredEndTimeUtc")]
    public DateTime? PreferredEndTimeUtc { get; set; }
    
    public Guid? TenantId { get; set; }
    public string? EnvironmentName { get; set; }
}

[JsonSerializable(typeof(EnvironmentUpdateWindow))]
internal sealed partial class EnvironmentUpdateWindowContext : JsonSerializerContext {

}

public class EnvironmentUpdateWindowResponse
{
    // New wall-time + timezone format (API v2.13+)
    [JsonPropertyName("preferredStartTime")]
    public string? PreferredStartTime { get; set; }
    
    [JsonPropertyName("preferredEndTime")]
    public string? PreferredEndTime { get; set; }
    
    [JsonPropertyName("timeZoneId")]
    public string? TimeZoneId { get; set; }
    
    // Legacy UTC format (for backward compatibility)
    [JsonPropertyName("preferredStartTimeUtc")]
    public DateTime? PreferredStartTimeUtc { get; set; }
    
    [JsonPropertyName("preferredEndTimeUtc")]
    public DateTime? PreferredEndTimeUtc { get; set; }
}

[JsonSerializable(typeof(EnvironmentUpdateWindowResponse))]
internal sealed partial class EnvironmentUpdateWindowResponseContext : JsonSerializerContext {

}

public class ApplicationInsightsKeyResponse
{
    public bool? Success { get; set; }
    public string? Message { get; set; }
}

[JsonSerializable(typeof(ApplicationInsightsKeyResponse))]
internal sealed partial class ApplicationInsightsKeyResponseContext : JsonSerializerContext {

}

public class EnvironmentCreationParameters
{
    public string? SourceEnvironmentName { get; set; }
    public string? SourceEnvironmentType { get; set; }
    public string? DestinationEnvironmentName { get; set; }
    public string? DestinationEnvironmentType { get; set; }
    public string? ApplicationVersion { get; set; }
    public string? CountryCode { get; set; }
}

public class EnvironmentCreationResponse
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? AadTenantId { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? StartedOn { get; set; }
    public DateTime? CompletedOn { get; set; }
    public string? CreatedBy { get; set; }
    public string? ErrorMessage { get; set; }
    public EnvironmentCreationParameters? Parameters { get; set; }
    public string? EnvironmentName { get; set; }
    public string? EnvironmentType { get; set; }
    public string? ProductFamily { get; set; }
}

[JsonSerializable(typeof(EnvironmentCreationResponse))]
internal sealed partial class EnvironmentCreationResponseContext : JsonSerializerContext {

}

public class EnvironmentDeletionParameters
{
    [JsonPropertyName("softDeletedOn")]
    public string? SoftDeletedOn { get; set; }
    
    [JsonPropertyName("hardDeletePendingOn")]
    public string? HardDeletePendingOn { get; set; }
    
    [JsonPropertyName("deleteReason")]
    public string? DeleteReason { get; set; }
}

public class EnvironmentDeletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("aadTenantId")]
    public string? AadTenantId { get; set; }
    
    [JsonPropertyName("createdOn")]
    public DateTime? CreatedOn { get; set; }
    
    [JsonPropertyName("startedOn")]
    public DateTime? StartedOn { get; set; }
    
    [JsonPropertyName("completedOn")]
    public DateTime? CompletedOn { get; set; }
    
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }
    
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
    
    [JsonPropertyName("parameters")]
    public EnvironmentDeletionParameters? Parameters { get; set; }
    
    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; set; }
    
    [JsonPropertyName("environmentType")]
    public string? EnvironmentType { get; set; }
    
    [JsonPropertyName("productFamily")]
    public string? ProductFamily { get; set; }
}

[JsonSerializable(typeof(EnvironmentDeletionResponse))]
internal sealed partial class EnvironmentDeletionResponseContext : JsonSerializerContext {

}

public class EnvironmentStorageUsage
{
    [JsonPropertyName("environmentType")]
    public string? EnvironmentType { get; set; }
    
    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; set; }
    
    [JsonPropertyName("applicationFamily")]
    public string? ApplicationFamily { get; set; }
    
    [JsonPropertyName("databaseStorageInKilobytes")]
    public long? DatabaseStorageInKilobytes { get; set; }
}

[JsonSerializable(typeof(EnvironmentStorageUsage))]
internal sealed partial class EnvironmentStorageUsageContext : JsonSerializerContext {

}

public class EnvironmentStorageUsageListResponse
{
    [JsonPropertyName("value")]
    public List<EnvironmentStorageUsage>? Value { get; set; }
}

[JsonSerializable(typeof(EnvironmentStorageUsageListResponse))]
internal sealed partial class EnvironmentStorageUsageListResponseContext : JsonSerializerContext {

}

public class EnvironmentUpdateScheduleDetails
{
    [JsonPropertyName("latestSelectableDate")]
    public DateTime? LatestSelectableDate { get; set; }
    
    [JsonPropertyName("selectedDateTime")]
    public DateTime? SelectedDateTime { get; set; }
    
    [JsonPropertyName("ignoreUpdateWindow")]
    public bool? IgnoreUpdateWindow { get; set; }
    
    [JsonPropertyName("rolloutStatus")]
    public string? RolloutStatus { get; set; }
}

public class EnvironmentUpdateExpectedAvailability
{
    [JsonPropertyName("month")]
    public int? Month { get; set; }
    
    [JsonPropertyName("year")]
    public int? Year { get; set; }
}

public class EnvironmentUpdateInfo
{
    [JsonPropertyName("targetVersion")]
    public string? TargetVersion { get; set; }
    
    [JsonPropertyName("available")]
    public bool? Available { get; set; }
    
    [JsonPropertyName("selected")]
    public bool? Selected { get; set; }
    
    [JsonPropertyName("scheduleDetails")]
    public EnvironmentUpdateScheduleDetails? ScheduleDetails { get; set; }
    
    [JsonPropertyName("expectedAvailability")]
    public EnvironmentUpdateExpectedAvailability? ExpectedAvailability { get; set; }
    
    [JsonPropertyName("targetVersionType")]
    public string? TargetVersionType { get; set; }
}

public class EnvironmentUpdatesListResponse
{
    [JsonPropertyName("value")]
    public List<EnvironmentUpdateInfo>? Value { get; set; }
}

[JsonSerializable(typeof(EnvironmentUpdatesListResponse))]
internal sealed partial class EnvironmentUpdatesListResponseContext : JsonSerializerContext {

}

public class SetEnvironmentUpdateScheduleDetails
{
    [JsonPropertyName("selectedDateTime")]
    public DateTime? SelectedDateTime { get; set; }
    
    [JsonPropertyName("ignoreUpdateWindow")]
    public bool? IgnoreUpdateWindow { get; set; }
}

public class SetEnvironmentUpdateRequest
{
    [JsonPropertyName("selected")]
    public bool? Selected { get; set; }
    
    [JsonPropertyName("targetVersionType")]
    public string? TargetVersionType { get; set; }
    
    [JsonPropertyName("scheduleDetails")]
    public SetEnvironmentUpdateScheduleDetails? ScheduleDetails { get; set; }
}

public class SetEnvironmentUpdateResponse
{
    [JsonPropertyName("selected")]
    public bool? Selected { get; set; }
    
    [JsonPropertyName("targetVersionType")]
    public string? TargetVersionType { get; set; }
    
    [JsonPropertyName("scheduleDetails")]
    public SetEnvironmentUpdateScheduleDetails? ScheduleDetails { get; set; }
}

[JsonSerializable(typeof(SetEnvironmentUpdateResponse))]
internal sealed partial class SetEnvironmentUpdateResponseContext : JsonSerializerContext {

}

public class ActiveSession
{
    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; set; }
    
    [JsonPropertyName("applicationFamily")]
    public string? ApplicationFamily { get; set; }
    
    [JsonPropertyName("sessionId")]
    public int? SessionId { get; set; }
    
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }
    
    [JsonPropertyName("clientType")]
    public string? ClientType { get; set; }
    
    [JsonPropertyName("logOnDate")]
    public string? LogOnDate { get; set; }
    
    [JsonPropertyName("entryPointOperation")]
    public string? EntryPointOperation { get; set; }
    
    [JsonPropertyName("entryPointObjectName")]
    public string? EntryPointObjectName { get; set; }
    
    [JsonPropertyName("entryPointObjectId")]
    public string? EntryPointObjectId { get; set; }
    
    [JsonPropertyName("entryPointObjectType")]
    public string? EntryPointObjectType { get; set; }
    
    [JsonPropertyName("currentObjectName")]
    public string? CurrentObjectName { get; set; }
    
    [JsonPropertyName("currentObjectId")]
    public int? CurrentObjectId { get; set; }
    
    [JsonPropertyName("currentObjectType")]
    public string? CurrentObjectType { get; set; }
    
    [JsonPropertyName("currentOperationDuration")]
    public long? CurrentOperationDuration { get; set; }
}

public class ActiveSessionsResponse
{
    [JsonPropertyName("value")]
    public List<ActiveSession>? Value { get; set; }
}

[JsonSerializable(typeof(ActiveSessionsResponse))]
internal sealed partial class ActiveSessionsResponseContext : JsonSerializerContext {

}

public class NotificationRecipient
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class NotificationRecipientsResponse
{
    [JsonPropertyName("value")]
    public List<NotificationRecipient>? Value { get; set; }
}

[JsonSerializable(typeof(NotificationRecipientsResponse))]
internal sealed partial class NotificationRecipientsResponseContext : JsonSerializerContext {

}

public class CreateNotificationRecipientRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

[JsonSerializable(typeof(NotificationRecipient))]
internal sealed partial class CreateNotificationRecipientResponseContext : JsonSerializerContext {

}

public class InstalledApp
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }
    
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    
    [JsonPropertyName("state")]
    public string? State { get; set; }
    
    [JsonPropertyName("lastOperationId")]
    public Guid? LastOperationId { get; set; }
    
    [JsonPropertyName("lastUpdateAttemptResult")]
    public string? LastUpdateAttemptResult { get; set; }
    
    [JsonPropertyName("lastUninstallOperationId")]
    public Guid? LastUninstallOperationId { get; set; }
    
    [JsonPropertyName("lastUninstallAttemptResult")]
    public string? LastUninstallAttemptResult { get; set; }
    
    [JsonPropertyName("appType")]
    public string? AppType { get; set; }
    
    [JsonPropertyName("canBeUninstalled")]
    public bool? CanBeUninstalled { get; set; }
}

public class InstalledAppsResponse
{
    [JsonPropertyName("value")]
    public List<InstalledApp>? Value { get; set; }
}

[JsonSerializable(typeof(InstalledAppsResponse))]
internal sealed partial class InstalledAppsResponseContext : JsonSerializerContext {

}

public class AppUpdateRequirement
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }
    
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class AvailableAppUpdate
{
    [JsonPropertyName("appId")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }
    
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    
    [JsonPropertyName("requirements")]
    public List<AppUpdateRequirement>? Requirements { get; set; }
}

public class AvailableAppUpdatesResponse
{
    [JsonPropertyName("value")]
    public List<AvailableAppUpdate>? Value { get; set; }
}

[JsonSerializable(typeof(AvailableAppUpdatesResponse))]
internal sealed partial class AvailableAppUpdatesResponseContext : JsonSerializerContext {

}

public class UpdateAppRequirement
{
    [JsonPropertyName("appId")]
    public Guid? AppId { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }
    
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class UpdateAppErrorData
{
    [JsonPropertyName("requirements")]
    public List<UpdateAppRequirement>? Requirements { get; set; }
}

public class UpdateAppErrorResponse
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("data")]
    public UpdateAppErrorData? Data { get; set; }
}

public class UpdateAppRequest
{
    [JsonPropertyName("useEnvironmentUpdateWindow")]
    public bool? UseEnvironmentUpdateWindow { get; set; }
    
    [JsonPropertyName("targetVersion")]
    public string? TargetVersion { get; set; }
    
    [JsonPropertyName("allowPreviewVersion")]
    public bool? AllowPreviewVersion { get; set; }
    
    [JsonPropertyName("installOrUpdateNeededDependencies")]
    public bool? InstallOrUpdateNeededDependencies { get; set; }
}

public class UpdateAppResponse
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("sourceAppVersion")]
    public string? SourceAppVersion { get; set; }
    
    [JsonPropertyName("targetAppVersion")]
    public string? TargetAppVersion { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("createdOn")]
    public DateTime? CreatedOn { get; set; }
    
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
    
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }
    
    [JsonPropertyName("canceledBy")]
    public string? CanceledBy { get; set; }
}

[JsonSerializable(typeof(UpdateAppResponse))]
internal sealed partial class UpdateAppResponseContext : JsonSerializerContext {

}

public class UninstallAppRequest
{
    [JsonPropertyName("useEnvironmentUpdateWindow")]
    public bool? UseEnvironmentUpdateWindow { get; set; }
    
    [JsonPropertyName("uninstallDependents")]
    public bool? UninstallDependents { get; set; }
    
    [JsonPropertyName("deleteData")]
    public bool? DeleteData { get; set; }
}

public class UninstallAppResponse
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("sourceAppVersion")]
    public string? SourceAppVersion { get; set; }
    
    [JsonPropertyName("targetAppVersion")]
    public string? TargetAppVersion { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("createdOn")]
    public DateTime? CreatedOn { get; set; }
    
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

[JsonSerializable(typeof(UninstallAppResponse))]
internal sealed partial class UninstallAppResponseContext : JsonSerializerContext {

}

public class AppOperation
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("createdOn")]
    public string? CreatedOn { get; set; }
    
    [JsonPropertyName("startedOn")]
    public string? StartedOn { get; set; }
    
    [JsonPropertyName("completedOn")]
    public string? CompletedOn { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("sourceVersion")]
    public string? SourceVersion { get; set; }
    
    [JsonPropertyName("targetVersion")]
    public string? TargetVersion { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public class AppOperationsResponse
{
    [JsonPropertyName("value")]
    public List<AppOperation>? Value { get; set; }
}

[JsonSerializable(typeof(AppOperationsResponse))]
internal sealed partial class AppOperationsResponseContext : JsonSerializerContext {

}

public partial class Environment
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
    
    [JsonPropertyName("tenantId")]
    public Guid? TenantId { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("locationName")]
    public string? LocationName { get; set; }
    
    [JsonPropertyName("geoName")]
    public string? GeoName { get; set; }
    
    [JsonPropertyName("appInsightsKey")]
    public string? AppInsightsKey { get; set; }
}

[JsonSerializable(typeof(List<Environment>))]
internal sealed partial class EnvironmentContext : JsonSerializerContext {

}

public class Feature
{
    [JsonPropertyName("@odata.etag")]
    public string? ODataEtag { get; set; }
    
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("enabled")]
    public string? Enabled { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("learnMoreLink")]
    public string? LearnMoreLink { get; set; }
    
    [JsonPropertyName("mandatoryBy")]
    public string? MandatoryBy { get; set; }
    
    [JsonPropertyName("canTry")]
    public bool? CanTry { get; set; }
    
    [JsonPropertyName("isOneWay")]
    public bool? IsOneWay { get; set; }
    
    [JsonPropertyName("dataUpdateRequired")]
    public bool? DataUpdateRequired { get; set; }
    
    [JsonPropertyName("mandatoryByVersion")]
    public string? MandatoryByVersion { get; set; }
    
    [JsonPropertyName("descriptionInEnglish")]
    public string? DescriptionInEnglish { get; set; }
}

public class FeaturesResponse
{
    [JsonPropertyName("value")]
    public List<Feature>? Value { get; set; }
}

[JsonSerializable(typeof(FeaturesResponse))]
internal sealed partial class FeaturesResponseContext : JsonSerializerContext {

}

public class Company
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }
    
    [JsonPropertyName("systemVersion")]
    public string? SystemVersion { get; set; }
    
    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
    
    [JsonPropertyName("businessProfileId")]
    public string? BusinessProfileId { get; set; }
    
    [JsonPropertyName("systemCreatedAt")]
    public DateTime? SystemCreatedAt { get; set; }
    
    [JsonPropertyName("systemCreatedBy")]
    public Guid? SystemCreatedBy { get; set; }
    
    [JsonPropertyName("systemModifiedAt")]
    public DateTime? SystemModifiedAt { get; set; }
    
    [JsonPropertyName("systemModifiedBy")]
    public Guid? SystemModifiedBy { get; set; }
}

public class CompaniesResponse
{
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }
    
    [JsonPropertyName("value")]
    public List<Company>? Value { get; set; }
}

[JsonSerializable(typeof(CompaniesResponse))]
internal sealed partial class CompaniesResponseContext : JsonSerializerContext {

}
