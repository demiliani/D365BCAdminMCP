using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace D365BCAdminMCP;

class Program
{
    static async Task Main(string[] args)
    {
        // Set up process exit handlers for clean shutdown
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        Console.WriteLine($"🚀 [DEBUG] D365BCAdminMCP starting (PID: {processId})");
        
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            Console.Error.WriteLine($"🔴 [DEBUG] Process exiting gracefully (PID: {processId})");
        };

        Console.CancelKeyPress += (s, e) =>
        {
            Console.Error.WriteLine($"🔴 [DEBUG] Ctrl+C received, shutting down (PID: {processId})");
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
            .WithToolsFromAssembly();

        builder.Services.AddSingleton<D365BCAdminService>();

        await builder.Build().RunAsync();
        
        Console.Error.WriteLine($"✅ [DEBUG] D365BCAdminMCP stopped cleanly (PID: {processId})");
    }
}


/*Cursor MCP Configuration - Add to your Cursor settings.json:

{
  "mcp.servers": {
    "D365BCAdminServiceMCP": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Users\\stefano\\OneDrive\\Projects\\AI\\D365AdminMCP\\D365BCAdminMCP\\D365BCAdminMCP.csproj"
      ],
      "cwd": "C:\\Users\\stefano\\OneDrive\\Projects\\AI\\D365AdminMCP\\D365BCAdminMCP"
    }
  }
}

*/


