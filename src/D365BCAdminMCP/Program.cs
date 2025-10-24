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


