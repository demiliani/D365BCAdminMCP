using System.Reflection;
using ModelContextProtocol.Server;

namespace D365BCAdminMCP.Tests;

public class ToolAnnotationTests
{
    private static readonly string[] DestructiveTools =
    {
        "delete_environment", "rename_environment", "uninstall_app", "kill_active_sessions",
        "delete_environment_notification_recipient", "cancel_scheduled_pte", "set_environment_security_group",
        "clear_environment_security_group"
    };

    private static readonly List<MethodInfo> Tools = typeof(D365BCAdminService)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
        .ToList();

    private static McpServerToolAttribute Annotation(MethodInfo tool) => tool.GetCustomAttribute<McpServerToolAttribute>()!;

    // The MCP default for an unannotated tool is "destructive", so every tool must state its behaviour.
    private static bool DeclaresBehaviour(MethodInfo tool) =>
        tool.GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(McpServerToolAttribute))
            .NamedArguments.Any(a => a.MemberName is nameof(McpServerToolAttribute.ReadOnly) or nameof(McpServerToolAttribute.Destructive));

    [Fact]
    public void AllToolsAreRegistered()
    {
        Assert.Equal(51, Tools.Count);
    }

    [Fact]
    public void EveryToolDeclaresWhetherItIsReadOnlyOrDestructive()
    {
        Assert.Empty(Tools.Where(t => !DeclaresBehaviour(t)).Select(t => t.Name));
    }

    [Fact]
    public void GetTools_AreReadOnlyAndIdempotent()
    {
        var getTools = Tools.Where(t => t.Name.StartsWith("get_")).ToList();

        Assert.NotEmpty(getTools);
        Assert.All(getTools, t => Assert.True(Annotation(t).ReadOnly && Annotation(t).Idempotent, t.Name));
    }

    [Fact]
    public void OnlyToolsThatDeleteOrBreakThingsAreDestructive()
    {
        var destructive = Tools.Where(t => !Annotation(t).ReadOnly && Annotation(t).Destructive).Select(t => t.Name).Order();

        Assert.Equal(DestructiveTools.Order(), destructive);
    }
}
