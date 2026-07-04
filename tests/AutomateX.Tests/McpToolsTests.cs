using AutomateX.Modules.Executions;
using AutomateX.Modules.Mcp;
using Xunit;

namespace AutomateX.Tests;

public sealed class McpToolsTests
{
    [Theory]
    [InlineData("Summarize Ticket!", "summarize_ticket")]
    [InlineData("  Enrich  Lead  ", "enrich_lead")]
    [InlineData("notify-oncall", "notify_oncall")]
    [InlineData("!!!", "workflow")]
    public void Slug_produces_a_safe_tool_name(string name, string expected)
    {
        Assert.Equal(expected, McpTools.Slug(name));
    }

    [Fact]
    public void IsTerminal_only_for_succeeded_or_failed()
    {
        Assert.True(McpTools.IsTerminal(ExecutionStatus.Succeeded));
        Assert.True(McpTools.IsTerminal(ExecutionStatus.Failed));
        Assert.False(McpTools.IsTerminal(ExecutionStatus.Running));
        Assert.False(McpTools.IsTerminal(ExecutionStatus.Waiting));
    }

    [Fact]
    public void TerminalOutput_is_the_highest_order_succeeded_step()
    {
        var output = McpTools.TerminalOutput(
        [
            (0, ExecutionStatus.Succeeded, "\"first\""),
            (1, ExecutionStatus.Succeeded, "\"last\""),
            (2, ExecutionStatus.Skipped, "\"skipped\""),
        ]);

        Assert.Equal("\"last\"", output);
    }

    [Fact]
    public void ResultContent_parses_json_or_wraps_a_plain_string()
    {
        Assert.Equal("42", McpTools.ResultContent("42").GetRawText());
        Assert.Equal("ok:1", McpTools.ResultContent("ok:1").GetString());
    }
}
