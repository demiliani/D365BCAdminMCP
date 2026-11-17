using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP;

// Asks the user to type the environment name; returns what was typed, or null when the user declined or dismissed the prompt.
internal delegate Task<string?> AskForEnvironmentName(string message, CancellationToken cancellationToken);

internal sealed class EnvironmentNameConfirmation
{
    [Description("Type the name of the environment to confirm")]
    public string EnvironmentName { get; set; } = string.Empty;
}

public partial class D365BCAdminService
{
    private const string ConfirmEnvironmentNameDescription =
        "Only needed for production environments when the MCP client cannot ask the user directly: the environment name as typed by the user " +
        "to confirm. Never fill it in yourself; ask the user. Ignored when the client supports confirmation prompts (elicitation).";

    internal static AskForEnvironmentName? ConfirmationPrompt(McpServer? server)
    {
        if (server?.ClientCapabilities?.Elicitation == null)
        {
            return null;
        }

        return async (message, cancellationToken) =>
        {
            var result = await server.ElicitAsync<EnvironmentNameConfirmation>(message, cancellationToken: cancellationToken);
            return result.IsAccepted ? result.Content?.EnvironmentName : null;
        };
    }

    // Returns null when the action may go ahead, otherwise the tool response explaining why it did not.
    // When the client can prompt the user, the user is always asked: a confirmEnvironmentName chosen by the model does not count.
    internal async Task<string?> ConfirmProductionActionAsync(string environmentName, string action, string? confirmEnvironmentName, AskForEnvironmentName? ask)
    {
        var environment = await getEnvironment(environmentName);
        if (environment.Error != null)
        {
            return ErrorJson(environment.Error);
        }

        if (!string.Equals(environment.Value?.Type, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool Matches(string? typed) => string.Equals(typed?.Trim(), environmentName, StringComparison.OrdinalIgnoreCase);

        if (ask == null)
        {
            if (Matches(confirmEnvironmentName))
            {
                Log.Info($"{action} of production environment {environmentName} confirmed with confirmEnvironmentName");
                return null;
            }

            return JsonSerializer.Serialize(new
            {
                success = false,
                confirmationRequired = true,
                error = $"'{environmentName}' is a PRODUCTION environment. Ask the user to confirm by typing the environment name, " +
                        "then call the tool again with confirmEnvironmentName set to exactly what the user typed. Nothing was changed."
            });
        }

        var typed = await ask($"You are about to {action} the PRODUCTION environment '{environmentName}'. Type the environment name to confirm.", CancellationToken);
        if (Matches(typed))
        {
            Log.Info($"{action} of production environment {environmentName} confirmed by the user");
            return null;
        }

        Log.Info($"{action} of production environment {environmentName} not confirmed by the user");
        return JsonSerializer.Serialize(new
        {
            success = false,
            cancelled = true,
            message = typed == null
                ? "The user did not confirm the operation. Nothing was changed."
                : $"The typed name '{typed}' does not match '{environmentName}'. Nothing was changed."
        });
    }

    public Task<ApiResult<EnvironmentData>> getEnvironment(string environmentName) =>
        SendAsync<EnvironmentData>(HttpMethod.Get, EnvironmentUrl(environmentName));
}
