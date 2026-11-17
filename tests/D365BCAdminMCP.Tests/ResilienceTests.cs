using System.Net;
using System.Net.Http.Headers;
using ModelContextProtocol;

namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class ResilienceTests : HttpTestBase
{
    private const string EnvironmentPath = "/admin/v2.29/applications/BusinessCentral/environments/Sandbox";

    private sealed class RecordingProgress : IProgress<ProgressNotificationValue>
    {
        public List<ProgressNotificationValue> Reports { get; } = new();
        public void Report(ProgressNotificationValue value) => Reports.Add(value);
    }

    private static Func<RecordedRequest, HttpResponseMessage> Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var call = 0;
        return _ => responses[Math.Min(call++, responses.Length - 1)]();
    }

    private static HttpResponseMessage Throttled(TimeSpan retryAfter)
    {
        var response = FakeAdminApi.Json(HttpStatusCode.TooManyRequests, new { code = "TooManyRequests", message = "Slow down" });
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private static HttpResponseMessage Ok() => FakeAdminApi.Json(HttpStatusCode.OK, new { value = Array.Empty<object>() });

    [Fact]
    public async Task ThrottledCalls_AreRetriedAfterTheRetryAfterDelay()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps$", Sequence(() => Throttled(TimeSpan.FromSeconds(7)), Ok));

        var json = Parse(await D365BCAdminService.get_installed_apps(TenantId, "Sandbox"));

        Assert.Equal(0, json.GetArrayLength());
        Assert.Equal(2, Api.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, RetryDelays);
    }

    [Fact]
    public async Task RetryAfter_IsCappedAtOneMinute()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps$", Sequence(() => Throttled(TimeSpan.FromHours(1)), Ok));

        await D365BCAdminService.get_installed_apps(TenantId, "Sandbox");

        Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, RetryDelays);
    }

    [Fact]
    public async Task ReadCalls_AreRetriedOnBadGatewayWithBackoffAndGiveUpAfterThreeAttempts()
    {
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps$", HttpStatusCode.BadGateway, new { code = "BadGateway", message = "Upstream failed" });

        var json = Parse(await D365BCAdminService.get_installed_apps(TenantId, "Sandbox"));

        Assert.Equal(D365BCAdminService.MaxAttempts, Api.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, RetryDelays);
        Assert.Contains("HTTP 502", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WriteCalls_AreNotRetriedOnBadGateway()
    {
        Api.On(HttpMethod.Post, "/Production/copy$", HttpStatusCode.BadGateway, new { code = "BadGateway", message = "Upstream failed" });

        var json = Parse(await D365BCAdminService.copy_environment(TenantId, "Production", "Testing", "Sandbox"));

        Assert.Single(Api.Requests);
        Assert.Empty(RetryDelays);
        Assert.Contains("HTTP 502", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WriteCalls_AreRetriedWhenThrottledAndResendTheSameBody()
    {
        Api.On(HttpMethod.Post, "/Production/copy$", Sequence(
            () => Throttled(TimeSpan.FromSeconds(1)),
            () => FakeAdminApi.Json(HttpStatusCode.Accepted, new { id = Guid.NewGuid(), type = "copy", status = "queued" })));

        var json = Parse(await D365BCAdminService.copy_environment(TenantId, "Production", "Testing", "Sandbox"));

        Assert.Equal("queued", json.GetProperty("status").GetString());
        Assert.Equal(2, Api.Requests.Count);
        Assert.Equal(Api.Requests[0].Body, Api.Requests[1].Body);
    }

    [Fact]
    public async Task CancellingTheRequest_StopsWaitingForThePteInstall()
    {
        using var folder = new TempFolder();
        var appId = Guid.NewGuid();
        var appFile = AppPackageBuilder.WriteAppJsonPackage(folder.File("Shop.app"), appId, "Shop", "1.0.0.0");
        using var cts = new CancellationTokenSource();
        var polls = 0;
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps$", HttpStatusCode.OK, new { value = Array.Empty<object>() })
            .On(HttpMethod.Post, $"{EnvironmentPath}/apps/pteInstall$", HttpStatusCode.OK, new { id = Guid.NewGuid(), appId, status = "running" })
            .On(HttpMethod.Get, $"{EnvironmentPath}/apps/{appId}/operations/", _ =>
            {
                polls++;
                cts.Cancel();
                return FakeAdminApi.Json(HttpStatusCode.OK, new { status = "running" });
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            D365BCAdminService.install_pte(TenantId, "Sandbox", appFile, maxWaitSeconds: 60, pollIntervalMs: 10, cancellationToken: cts.Token));

        Assert.Equal(1, polls);
    }

    [Fact]
    public async Task InstallPtesFromFolder_ReportsProgressForEachApp()
    {
        using var folder = new TempFolder();
        var baseId = Guid.NewGuid();
        var topId = Guid.NewGuid();
        AppPackageBuilder.WriteAppJsonPackage(folder.File("Base.app"), baseId, "Base", "1.0.0.0");
        AppPackageBuilder.WriteAppJsonPackage(folder.File("Top.app"), topId, "Top", "1.0.0.0", new DependencySpec(baseId, "Base", "1.0.0.0"));
        Api.On(HttpMethod.Get, $"{EnvironmentPath}/apps$", HttpStatusCode.OK, new { value = Array.Empty<object>() })
            .On(HttpMethod.Post, $"{EnvironmentPath}/apps/pteInstall$", request => FakeAdminApi.Json(HttpStatusCode.OK,
                new { id = Guid.NewGuid(), appId = request.Body!.Contains("Top.app") ? topId : baseId, status = "running" }))
            .On(HttpMethod.Get, "/operations/", HttpStatusCode.OK, new { status = "succeeded" });
        var progress = new RecordingProgress();

        var json = Parse(await D365BCAdminService.install_ptes_from_folder(TenantId, "Sandbox", folder.Path, maxWaitSeconds: 5, pollIntervalMs: 10, progress: progress));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.All(progress.Reports, report => Assert.Equal(3, report.Total));
        Assert.Equal(new float[] { 0, 1, 2, 3 }, progress.Reports.Select(r => r.Progress));
        Assert.Equal(new[] { "Base: succeeded", "Top: succeeded" }, progress.Reports.Skip(2).Select(r => r.Message));
    }
}
