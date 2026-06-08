using AutomateX.Modules.Triggers;
using Xunit;

namespace AutomateX.Tests;

public sealed class TriggerEditRulesTests
{
    [Theory]
    [InlineData(TriggerTypes.Cron, true)]
    [InlineData(TriggerTypes.Workflow, true)]
    [InlineData("matrix.onMessage", true)]
    [InlineData(TriggerTypes.Webhook, false)] // its config holds the hashed secret — rotate, don't edit
    public void Only_non_webhook_triggers_allow_config_edits(string type, bool expected) =>
        Assert.Equal(expected, TriggerEditRules.CanEditConfig(type));

    [Fact]
    public void Reconfigure_replaces_config_and_next_run()
    {
        var trigger = Trigger.Create(Guid.CreateVersion7(), TriggerTypes.Cron, """{"cron":"0 8 * * *"}""", DateTimeOffset.UtcNow);
        var newRun = DateTimeOffset.UtcNow.AddHours(1);

        trigger.Reconfigure("""{"cron":"0 9 * * *"}""", newRun);

        Assert.Equal("""{"cron":"0 9 * * *"}""", trigger.ConfigJson);
        Assert.Equal(newRun, trigger.NextRunAt);
    }

    [Fact]
    public void SetEnabled_toggles()
    {
        var trigger = Trigger.Create(Guid.CreateVersion7(), TriggerTypes.Cron, "{}", null);
        Assert.True(trigger.Enabled);

        trigger.SetEnabled(false);
        Assert.False(trigger.Enabled);

        trigger.SetEnabled(true);
        Assert.True(trigger.Enabled);
    }
}
