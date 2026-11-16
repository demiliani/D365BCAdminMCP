namespace D365BCAdminMCP.Tests;

[Collection(HttpCollection.Name)]
public class LogTests
{
    [Theory]
    [InlineData(null, LogSeverity.Information)]
    [InlineData("debug", LogSeverity.Debug)]
    [InlineData("Warning", LogSeverity.Warning)]
    [InlineData("ERROR", LogSeverity.Error)]
    [InlineData("off", LogSeverity.None)]
    [InlineData("nonsense", LogSeverity.Information)]
    public void ParseLevel_MapsEnvironmentValues(string? value, LogSeverity expected) =>
        Assert.Equal(expected, Log.ParseLevel(value));

    [Fact]
    public void Write_RespectsTheMinimumLevel()
    {
        var originalWriter = Log.Writer;
        var originalLevel = Log.MinimumLevel;
        using var output = new StringWriter();
        try
        {
            Log.Writer = output;
            Log.MinimumLevel = LogSeverity.Warning;

            Log.Debug("hidden debug");
            Log.Info("hidden info");
            Log.Warn("visible warning");
            Log.Error("visible error");
        }
        finally
        {
            Log.Writer = originalWriter;
            Log.MinimumLevel = originalLevel;
        }

        var text = output.ToString();
        Assert.DoesNotContain("hidden", text);
        Assert.Contains("[WARN] visible warning", text);
        Assert.Contains("[ERROR] visible error", text);
    }
}
