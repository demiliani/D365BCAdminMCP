namespace D365BCAdminMCP;

public enum LogSeverity
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    None = 4
}

// MCP stdio transport reserves stdout for JSON-RPC messages, so diagnostics must only go to stderr.
public static class Log
{
    public const string LevelEnvironmentVariable = "D365BC_MCP_LOG_LEVEL";

    public static LogSeverity MinimumLevel { get; set; } = ParseLevel(System.Environment.GetEnvironmentVariable(LevelEnvironmentVariable));

    public static TextWriter Writer { get; set; } = Console.Error;

    public static void Debug(string message) => Write(LogSeverity.Debug, message);
    public static void Info(string message) => Write(LogSeverity.Information, message);
    public static void Warn(string message) => Write(LogSeverity.Warning, message);
    public static void Error(string message) => Write(LogSeverity.Error, message);

    public static LogSeverity ParseLevel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "debug" or "trace" => LogSeverity.Debug,
        "warning" or "warn" => LogSeverity.Warning,
        "error" => LogSeverity.Error,
        "none" or "off" => LogSeverity.None,
        _ => LogSeverity.Information
    };

    private static void Write(LogSeverity level, string message)
    {
        if (level < MinimumLevel || MinimumLevel == LogSeverity.None)
        {
            return;
        }

        var label = level switch
        {
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Information => "INFO",
            LogSeverity.Warning => "WARN",
            _ => "ERROR"
        };
        Writer.WriteLine($"[{label}] {message}");
    }
}
