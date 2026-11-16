using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace D365BCAdminMCP;

class Program
{
    static async Task Main(string[] args)
    {
        // Set up process exit handlers for clean shutdown
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        Log.Info($"D365BCAdminMCP starting (PID: {processId})");

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            Log.Debug($"Process exiting gracefully (PID: {processId})");
        };

        Console.CancelKeyPress += (s, e) =>
        {
            Log.Info($"Ctrl+C received, shutting down (PID: {processId})");
        };

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Logging.AddConsole(options =>
        {
            // Configure all logs to go to stderr
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                // The client gets no response for a cancelled request; without this the SDK logs the cancellation as a tool failure.
                try
                {
                    return await next(context, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Log.Info($"{context.Params?.Name} cancelled by the client");
                    return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "Cancelled by the client" }] };
                }
            }));

        await builder.Build().RunAsync();

        Log.Info($"D365BCAdminMCP stopped cleanly (PID: {processId})");
    }
}
